using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Editing;
using PhotoProcessing.Core.Heimdall;
using PhotoProcessing.Core.Nextcloud;
using PhotoProcessing.Core.Status;

namespace PhotoProcessing.Cli;

/// <summary>
/// Polls the Nextcloud inbox. Each settled raw is downloaded, edited by Claude, exported to the
/// output folder, and moved (with its darktable sidecar and Claude's notes) to the archive folder,
/// so the inbox itself is the queue. A photo that keeps failing is moved to the failed folder.
/// An optional "&lt;same name&gt;.txt" next to the raw is passed to Claude as instructions.
/// Progress is published to state/status.json for the tray icon.
/// </summary>
public sealed class InboxWatcher(
    PhotoProcessingSettings settings,
    HeimdallSession heimdall,
    ILogger<InboxWatcher> logger) : BackgroundService
{
    private static readonly string[] InstructionExtensions = [".txt", ".md"];
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Pause after a sign-in failure or a missing inbox: fail2ban on the server bans IPs that keep
    /// failing logins, and neither problem fixes itself within a minute. `photoedit login` cuts the
    /// wait short by requesting a poll.
    /// </summary>
    private static readonly TimeSpan SetupProblemDelay = TimeSpan.FromMinutes(15);

    private readonly NextcloudSettings _nc = settings.Nextcloud;
    private readonly Dictionary<string, (string ETag, DateTime FirstSeen)> _seen = new();
    private readonly StatusStore _store = new(settings.Home);
    private WatcherStatus _status = new();
    private NextcloudClient? _cloud;
    private bool _foldersReady;
    private string AttemptsPath => Path.Combine(settings.Home, "state", "attempts.json");

    private NextcloudClient Cloud => _cloud ??= new NextcloudClient(_nc.BaseUrl, heimdall.Username, heimdall.GetAccessTokenAsync);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("watching {Inbox} on {Server} as {User} every {Interval}",
            _nc.InboxFolder, _nc.BaseUrl, heimdall.Load()?.Username ?? "nobody (not signed in to Heimdall)", _nc.PollInterval);
        Publish(_ => new WatcherStatus
        {
            State = WatcherState.Starting,
            Message = "Connecting to Nextcloud",
            ProcessId = Environment.ProcessId,
            PollInterval = _nc.PollInterval,
            Last = _store.Read()?.Last, // keep showing the previous run's last photo
        });

        while (!ct.IsCancellationRequested)
        {
            var delay = _nc.PollInterval;
            try
            {
                var signedIn = heimdall.Load()?.Username;
                if (_cloud is not null && signedIn != _cloud.Username)
                {
                    logger.LogInformation("Heimdall sign-in changed from {Old} to {New}", _cloud.Username, signedIn ?? "nobody");
                    ResetCloud();
                }

                // Inside the loop: at boot the network may not be up yet, so this retries every poll.
                if (!_foldersReady)
                    _foldersReady = await PrepareFoldersAsync(ct);

                if (_foldersReady)
                {
                    await PollOnceAsync(ct);
                }
                else
                {
                    delay = SetupProblemDelay;
                    Publish(s => s with
                    {
                        State = WatcherState.Error,
                        Message = $"Inbox {_nc.InboxFolder} not found",
                        NextAttemptUtc = DateTime.UtcNow + delay,
                    });
                }
            }
            catch (Exception e) when (IsAuthFailure(e))
            {
                logger.LogError("{Message}; retrying in {Delay}, or as soon as `photoedit login` finishes",
                    FirstLine(e.Message), SetupProblemDelay);
                var problem = e switch
                {
                    HeimdallException { SignInRequired: true } => "Sign in to Heimdall needed",
                    HeimdallException => $"Heimdall: {Short(e.Message)}",
                    _ => $"Nextcloud refused {_cloud?.Username}'s Heimdall sign-in",
                };
                ResetCloud();
                delay = SetupProblemDelay;
                Publish(s => s with
                {
                    State = WatcherState.Error,
                    Message = problem,
                    CurrentPhoto = null, CurrentStage = null,
                    NextAttemptUtc = DateTime.UtcNow + delay,
                });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "poll failed");
                Publish(s => s with
                {
                    State = WatcherState.Error,
                    Message = $"Inbox check failed: {Short(e.Message)}",
                    CurrentPhoto = null, CurrentStage = null,
                    NextAttemptUtc = DateTime.UtcNow + delay,
                });
            }

            await WaitAsync(delay, ct);
        }
    }

    /// <summary>Sleeps until the next poll, waking early when the tray asks for a check.</summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        var until = DateTime.UtcNow + delay;
        while (DateTime.UtcNow < until)
        {
            if (_store.ConsumePollRequest())
            {
                logger.LogInformation("inbox check requested");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    /// <summary>Checks the inbox exists (it is a share, so never create it) and creates the output folders.</summary>
    private async Task<bool> PrepareFoldersAsync(CancellationToken ct)
    {
        if (!await Cloud.ExistsAsync(_nc.InboxFolder, ct))
        {
            var top = await Cloud.ListAsync("", ct);
            logger.LogError(
                "inbox '{Inbox}' does not exist for {User}. Their top-level folders: {Folders}. " +
                "If the inbox is in a share, accept it and set PhotoProcessing:Nextcloud:InboxFolder (and the other folders) to where it is mounted. Checking again in {Delay}.",
                _nc.InboxFolder, Cloud.Username, string.Join(", ", top.Where(i => i.IsFolder).Select(i => i.Path)), SetupProblemDelay);
            return false;
        }

        foreach (var folder in new[] { _nc.OutputFolder, _nc.ArchiveFolder })
            await Cloud.EnsureFolderAsync(folder, ct);
        logger.LogInformation("inbox found; output → {Output}, archive → {Archive}", _nc.OutputFolder, _nc.ArchiveFolder);
        return true;
    }

    /// <summary>Rebuilds the client from the stored sign-in next poll; a new user has their own folders to check.</summary>
    private void ResetCloud()
    {
        _cloud?.Dispose();
        _cloud = null;
        _foldersReady = false;
    }

    private static bool IsAuthFailure(Exception e) =>
        e is HeimdallException or HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden };

    public async Task PollOnceAsync(CancellationToken ct)
    {
        var items = await Cloud.ListAsync(_nc.InboxFolder, ct);
        var files = items.Where(i => !i.IsFolder).ToList();
        var raws = files.Where(f => _nc.RawExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant())).ToList();
        PublishIdle(raws.Count);

        foreach (var gone in _seen.Keys.Except(raws.Select(r => r.Path)).ToList())
            _seen.Remove(gone);

        var attempts = LoadAttempts();
        var remaining = raws.Count;
        foreach (var raw in raws.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!_seen.TryGetValue(raw.Path, out var seen) || seen.ETag != raw.ETag)
            {
                _seen[raw.Path] = (raw.ETag, DateTime.UtcNow);
                if (_nc.SettleTime > TimeSpan.Zero) continue;
            }
            else if (DateTime.UtcNow - seen.FirstSeen < _nc.SettleTime)
            {
                continue;
            }

            var key = $"{raw.Path}|{raw.ETag}";
            if (attempts.TryGetValue(key, out var a) && DateTime.UtcNow - a.Last < RetryDelay)
                continue;

            var stem = Path.GetFileNameWithoutExtension(raw.Name);
            var instructions = files.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f.Name).Equals(stem, StringComparison.OrdinalIgnoreCase)
                && InstructionExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()));

            try
            {
                await ProcessAsync(raw, instructions, ct);
                attempts.Remove(key);
            }
            catch (Exception e) when (e is not OperationCanceledException && !IsAuthFailure(e))
            {
                var count = (attempts.TryGetValue(key, out var prev) ? prev.Count : 0) + 1;
                attempts[key] = new Attempt(count, DateTime.UtcNow);
                logger.LogError(e, "{Raw}: attempt {Count}/{Max} failed", raw.Name, count, _nc.MaxAttempts);
                if (count >= _nc.MaxAttempts)
                {
                    await MoveToFailedAsync(raw, instructions, e, ct);
                    attempts.Remove(key);
                }
            }

            SaveAttempts(attempts);
            PublishIdle(--remaining);
        }
    }

    private async Task ProcessAsync(RemoteItem raw, RemoteItem? instructionsFile, CancellationToken ct)
    {
        logger.LogInformation("{Raw}: picked up ({Size:N0} bytes)", raw.Name, raw.Size);
        Publish(s => s with
        {
            State = WatcherState.Processing,
            Message = $"Editing {raw.Name}",
            CurrentPhoto = raw.Name,
            CurrentStage = "Downloading",
            CurrentSinceUtc = DateTime.UtcNow,
        });

        var instructions = instructionsFile is null ? null : await Cloud.DownloadStringAsync(instructionsFile.Path, ct);
        var job = EditJob.Create(settings.JobsDir, raw.Name, raw.Path, instructions);
        await Cloud.DownloadAsync(raw.Path, job.RawPath, ct);

        var outcome = await new EditPipeline(settings, logger).RunAsync(job, ct,
            stage => Publish(s => s with { CurrentStage = stage }));
        job = outcome.Job;

        Publish(s => s with { CurrentStage = "Uploading to Nextcloud" });
        var outputPath = await Cloud.UniquePathAsync($"{_nc.OutputFolder}/{Path.GetFileName(job.OutputJpegPath)}", ct);
        await Cloud.UploadAsync(job.OutputJpegPath, outputPath, ct);

        var archivePath = await Cloud.UniquePathAsync($"{_nc.ArchiveFolder}/{raw.Name}", ct);
        await Cloud.MoveAsync(raw.Path, archivePath, ct);
        await Cloud.UploadAsync(job.OutputSidecarPath, archivePath + ".xmp", ct);
        if (File.Exists(job.NotesPath))
            await Cloud.UploadAsync(job.NotesPath, Path.ChangeExtension(archivePath, ".notes.md"), ct);
        if (instructionsFile is not null)
            await Cloud.MoveAsync(instructionsFile.Path,
                await Cloud.UniquePathAsync($"{_nc.ArchiveFolder}/{instructionsFile.Name}", ct), ct);

        logger.LogInformation("{Raw}: done → {Output}{Fallback}", raw.Name, outputPath,
            outcome.UsedFallback ? " (fallback export, Claude did not finish)" : "");
        Publish(s => s with
        {
            Last = new LastResult(raw.Name, outputPath, DateTime.UtcNow, outcome.UsedFallback,
                FirstLine(outcome.Claude.Summary), job.Dir),
            ProcessedSinceStart = s.ProcessedSinceStart + 1,
            CurrentPhoto = null, CurrentStage = null, CurrentSinceUtc = null,
        });
    }

    private async Task MoveToFailedAsync(RemoteItem raw, RemoteItem? instructions, Exception error, CancellationToken ct)
    {
        try
        {
            await Cloud.EnsureFolderAsync(_nc.FailedFolder, ct);
            var target = await Cloud.UniquePathAsync($"{_nc.FailedFolder}/{raw.Name}", ct);
            await Cloud.MoveAsync(raw.Path, target, ct);
            await Cloud.UploadStringAsync($"{DateTime.Now:u}\n{error}", target + ".error.txt", ct);
            if (instructions is not null)
                await Cloud.MoveAsync(instructions.Path, await Cloud.UniquePathAsync($"{_nc.FailedFolder}/{instructions.Name}", ct), ct);
            logger.LogError("{Raw}: gave up, moved to {Target}", raw.Name, target);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "{Raw}: could not move to the failed folder", raw.Name);
        }
    }

    private void PublishIdle(int inboxCount) => Publish(s => s with
    {
        State = WatcherState.Idle,
        Message = inboxCount == 0 ? "Inbox empty" : $"{inboxCount} photo(s) waiting",
        LastPollUtc = DateTime.UtcNow,
        NextAttemptUtc = DateTime.UtcNow + _nc.PollInterval,
        InboxCount = inboxCount,
        CurrentPhoto = null, CurrentStage = null, CurrentSinceUtc = null,
    });

    /// <summary>Status is advisory: a failed write must never break processing.</summary>
    private void Publish(Func<WatcherStatus, WatcherStatus> change)
    {
        try
        {
            lock (_store)
            {
                _status = change(_status);
                _store.Write(_status);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "could not write status");
        }
    }

    private static string Short(string message) => FirstLine(message) is { Length: > 120 } s ? s[..120] + "…" : FirstLine(message);

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    public override void Dispose()
    {
        _cloud?.Dispose();
        base.Dispose();
    }

    private sealed record Attempt(int Count, DateTime Last);

    private Dictionary<string, Attempt> LoadAttempts()
    {
        try
        {
            return File.Exists(AttemptsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Attempt>>(File.ReadAllText(AttemptsPath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveAttempts(Dictionary<string, Attempt> attempts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AttemptsPath)!);
        File.WriteAllText(AttemptsPath, JsonSerializer.Serialize(attempts));
    }
}
