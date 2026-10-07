using Microsoft.Extensions.Logging;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Editing;

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
}
