using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Editing;
using PhotoProcessing.Core.Heimdall;
using PhotoProcessing.Core.Nextcloud;
using PhotoProcessing.Core.Status;

namespace PhotoProcessing.Cli;

public static class Commands
{
    /// <summary>Runs the full Claude edit for one local raw (or an existing job) — the watcher's pipeline without Nextcloud.</summary>
    public static async Task<int> EditAsync(PhotoProcessingSettings settings, string rawOrJob, string? instructions, CancellationToken ct)
    {
        using var loggers = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
            .AddProvider(new FileLoggerProvider(settings.LogsDir)));
        var logger = loggers.CreateLogger("edit");

        var job = File.Exists(rawOrJob)
            ? EditJob.CreateFromFile(settings.JobsDir, rawOrJob, instructions)
            : EditJob.Open(settings.JobsDir, rawOrJob);

        var outcome = await new EditPipeline(settings, logger).RunAsync(job, ct);
        Console.WriteLine();
        Console.WriteLine($"output:  {outcome.Job.OutputJpegPath}");
        Console.WriteLine($"sidecar: {outcome.Job.OutputSidecarPath}");
        Console.WriteLine($"job:     {outcome.Job.Dir}");
        if (File.Exists(outcome.Job.NotesPath))
            Console.WriteLine($"\n{File.ReadAllText(outcome.Job.NotesPath).Trim()}");
        return outcome.UsedFallback ? 3 : 0;
    }

    public static async Task<int> WatchAsync(PhotoProcessingSettings settings, IConfiguration configuration, string[] args, CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args[1..],
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddConfiguration(configuration);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        builder.Logging.AddProvider(new FileLoggerProvider(settings.LogsDir));
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(_ => new HeimdallSession(settings.Heimdall, settings.HeimdallSignInPath));
        builder.Services.AddHostedService<InboxWatcher>();

        using var host = builder.Build();
        await host.RunAsync(ct);
        return 0;
    }

    /// <summary>Signs in with Heimdall in the browser and stores the sign-in the watcher uses.</summary>
    public static async Task<int> LoginAsync(PhotoProcessingSettings settings, bool switchAccount, CancellationToken ct)
    {
        using var heimdall = new HeimdallSession(settings.Heimdall, settings.HeimdallSignInPath);
        var signIn = await heimdall.SignInAsync(url =>
        {
            Console.WriteLine($"Opening Heimdall in your browser. If it doesn't open, go to:\n  {url}");
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }, switchAccount, ct);

        new StatusStore(settings.Home).RequestPoll(); // a waiting watcher picks the sign-in up now
        if (!signIn.Offline)
            Console.WriteLine("warning: Heimdall did not grant offline_access, so the watcher is signed out when you sign out of Heimdall.");
        Console.WriteLine($"Signed in to Heimdall as {signIn.Username}.");
        return 0;
    }

    public static async Task<int> CheckAsync(PhotoProcessingSettings settings, CancellationToken ct)
    {
        var ok = true;
        void Report(bool pass, string what, string detail)
        {
            ok &= pass;
            Console.WriteLine($"{(pass ? "ok  " : "FAIL")} {what}: {detail}");
        }

        try
        {
            var dt = settings.ResolveDarktableCli();
            Report(true, "darktable-cli", $"{dt} ({await RunAsync(dt, ["--version"], ct)})");
        }
        catch (Exception e) { Report(false, "darktable-cli", e.Message); }

        try
        {
            var claude = settings.Claude.ResolveExecutable();
            Report(true, "claude", $"{claude} ({await RunAsync(claude, ["--version"], ct)})");
        }
        catch (Exception e) { Report(false, "claude", e.Message); }

        Report(File.Exists(Path.Combine(EditPipeline.TemplateDir, "CLAUDE.md")), "editor template", EditPipeline.TemplateDir);
        Console.WriteLine($"     runtime home: {settings.Home}");

        using var heimdall = new HeimdallSession(settings.Heimdall, settings.HeimdallSignInPath);
        try
        {
            await heimdall.GetAccessTokenAsync(ct);
            var signIn = heimdall.Load()!;
            Report(true, "heimdall", $"signed in as {signIn.Username} since {signIn.SignedInUtc.ToLocalTime():g}" +
                (signIn.Offline ? "" : " (without offline_access: it ends when you sign out of Heimdall)"));
        }
        catch (Exception e)
        {
            Report(false, "heimdall", e.Message);
            Report(false, "nextcloud", "needs a Heimdall sign-in");
            return 1;
        }

        try
        {
            using var cloud = new NextcloudClient(settings.Nextcloud.BaseUrl, heimdall.Username, heimdall.GetAccessTokenAsync);
            var who = $"{cloud.Username}@{settings.Nextcloud.BaseUrl}";
            if (await cloud.ExistsAsync(settings.Nextcloud.InboxFolder, ct))
            {
                var inbox = await cloud.ListAsync(settings.Nextcloud.InboxFolder, ct);
                Report(true, "nextcloud", $"{who}, {settings.Nextcloud.InboxFolder} has {inbox.Count(i => !i.IsFolder)} file(s)");
            }
            else
            {
                var top = await cloud.ListAsync("", ct);
                Report(false, "nextcloud", $"logged in as {who}, but {settings.Nextcloud.InboxFolder} does not exist. " +
                    $"Top-level folders: {string.Join(", ", top.Where(i => i.IsFolder).Select(i => i.Path))}");
            }
        }
        catch (Exception e) { Report(false, "nextcloud", e.Message); }

        return ok ? 0 : 1;
    }

    private static async Task<string> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
    }
}
