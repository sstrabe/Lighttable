using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Claude;
using PhotoProcessing.Core.Darktable;
using PhotoProcessing.Core.Editing;

namespace PhotoProcessing.Cli;

public sealed record EditOutcome(EditJob Job, ClaudeRunResult Claude, bool UsedFallback);

/// <summary>One photo, start to finish: baseline render → Claude edits → guaranteed final export.</summary>
public sealed partial class EditPipeline(PhotoProcessingSettings settings, ILogger logger)
{
    public static string TemplateDir => Path.Combine(AppContext.BaseDirectory, "editor");

    /// <param name="onStage">Receives short, human-readable stage descriptions (for the tray icon).</param>
    public async Task<EditOutcome> RunAsync(EditJob job, CancellationToken ct, Action<string>? onStage = null)
    {
        var darktable = new DarktableCli(settings.ResolveDarktableCli(), settings.DarktableConfigDir);
        if (job.Info.Baseline is null)
        {
            logger.LogInformation("{Job}: rendering darktable defaults", job.Id);
            onStage?.Invoke("Rendering darktable's defaults");
            await job.InitAsync(darktable, settings.PreviewSize, ct);
        }

        var editor = new ClaudeEditor(settings);
        editor.SyncWorkspace(TemplateDir);
        logger.LogInformation("{Job}: Claude is editing ({Exif})", job.Id, job.Info.Exif);
        onStage?.Invoke("Claude is studying the photo");
        var result = await editor.EditAsync(job, line =>
        {
            logger.LogDebug("{Job}: {Line}", job.Id, line);
            if (DescribeProgress(line) is { } stage) onStage?.Invoke(stage);
        }, ct);
        logger.Log(result.Success ? LogLevel.Information : LogLevel.Warning,
            "{Job}: Claude finished: success={Success} turns={Turns} in {Duration:mm\\:ss}: {Summary}",
            job.Id, result.Success, result.Turns, result.Duration, result.Summary);

        // Claude's photoedit calls updated job.json from another process.
        job = EditJob.Open(settings.JobsDir, job.Dir);
        var fallback = false;
        if (!job.IsFinalized)
        {
            fallback = true;
            logger.LogWarning("{Job}: Claude did not finalize; exporting the current recipe instead", job.Id);
            onStage?.Invoke("Exporting (Claude did not finish)");
            await job.FinalizeAsync(darktable, ct);
            await File.AppendAllTextAsync(job.NotesPath,
                $"\n\n> Automatic fallback export: Claude did not finish ({result.Summary}).\n", ct);
        }

        return new EditOutcome(job, result, fallback);
    }

    /// <summary>Turns Claude's tool calls into a short status line; null for anything not worth showing.</summary>
    internal static string? DescribeProgress(string line)
    {
        if (!line.StartsWith('[')) return null;
        if (line.Contains("photoedit finalize")) return "Exporting full resolution";
        if (line.Contains("photoedit zoom")) return "Claude is checking detail at 1:1";
        if (line.Contains("photoedit render")) return "Claude is rendering a preview";
        if (line.Contains("photoedit info")) return "Claude is reading the shooting data";
        if (PreviewRead().Match(line) is { Success: true } m) return $"Claude is looking at {m.Groups[1].Value}";
        if (line.Contains("recipe.json") && (line.StartsWith("[Edit]") || line.StartsWith("[Write]"))) return "Claude is adjusting the edit";
        if (line.Contains("notes.md")) return "Claude is writing notes";
        return null;
    }

    [GeneratedRegex(@"^\[Read\].*[\\/](v\d+(?:-zoom)?)\.jpg", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewRead();
}
