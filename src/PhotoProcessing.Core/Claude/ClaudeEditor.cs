using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PhotoProcessing.Core.Editing;

namespace PhotoProcessing.Core.Claude;

public sealed record ClaudeRunResult(bool Success, string Summary, int? Turns, double? CostUsd, TimeSpan Duration, string TranscriptPath);

/// <summary>
/// Runs Claude Code headless (<c>claude -p</c>, your normal Claude login — no API key) in the editor
/// workspace, which carries the photo-editing CLAUDE.md. Claude may only read files, edit files, and
/// run <c>photoedit</c>; everything else is denied without prompting (<c>--permission-mode dontAsk</c>).
/// </summary>
public sealed class ClaudeEditor(PhotoProcessingSettings settings)
{
    public static readonly string[] AllowedTools =
        ["Read", "Edit", "Write", "Bash(photoedit *)", "PowerShell(photoedit *)"];

    /// <summary>Copies the editor template (CLAUDE.md, .claude/settings.json) into the runtime workspace.</summary>
    public void SyncWorkspace(string templateDir)
    {
        if (!Directory.Exists(templateDir))
            throw new DirectoryNotFoundException($"editor template not found at {templateDir}");

        foreach (var source in Directory.EnumerateFiles(templateDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(settings.EditorDir, Path.GetRelativePath(templateDir, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        Directory.CreateDirectory(settings.JobsDir);
    }

    public async Task<ClaudeRunResult> EditAsync(EditJob job, Action<string>? progress = null, CancellationToken ct = default)
    {
        var jobRef = Path.GetRelativePath(settings.EditorDir, job.Dir).Replace('\\', '/');
        var prompt = $"""
            Edit the raw photo in job folder `{jobRef}`. Follow the workflow in CLAUDE.md.
            {(job.Info.Instructions is { } instructions ? $"The photographer left instructions for this photo — they take priority over general taste:\n\"\"\"\n{instructions}\n\"\"\"\n" : "")}
            Finish by writing `{jobRef}/notes.md` and running `photoedit finalize {jobRef}`.
            """;

        var psi = new ProcessStartInfo(settings.Claude.ResolveExecutable())
        {
            WorkingDirectory = settings.EditorDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] args =
        [
            "-p",
            "--output-format", "stream-json", "--verbose",
            "--permission-mode", "dontAsk",
            "--max-turns", settings.Claude.MaxTurns.ToString(CultureInfo.InvariantCulture),
            "--no-session-persistence",
            .. string.IsNullOrWhiteSpace(settings.Claude.Model) ? Array.Empty<string>() : ["--model", settings.Claude.Model],
            .. string.IsNullOrWhiteSpace(settings.Claude.Effort) ? Array.Empty<string>() : ["--effort", settings.Claude.Effort],
            "--allowedTools", .. AllowedTools,
        ];
        foreach (var a in args) psi.ArgumentList.Add(a);

        // Claude's `photoedit ...` calls must reach this same build, with the same settings.
        psi.Environment["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        var transcriptPath = Path.Combine(job.Dir, "claude.jsonl");
        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {settings.Claude.Executable}");
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        ClaudeRunResult? result = null;
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(settings.Claude.Timeout);
        await using (var transcript = new StreamWriter(transcriptPath, append: false))
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                {
                    await transcript.WriteLineAsync(line);
                    await transcript.FlushAsync(timeout.Token);
                    result = Interpret(line, progress, sw, transcriptPath) ?? result;
                }

                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                if (ct.IsCancellationRequested) throw;
                return new ClaudeRunResult(false, $"timed out after {settings.Claude.Timeout}", null, null, sw.Elapsed, transcriptPath);
            }
        }

        var errors = await stderr;
        return result ?? new ClaudeRunResult(false,
            $"claude exited with code {process.ExitCode} without a result. {errors.Trim()}", null, null, sw.Elapsed, transcriptPath);
    }

    private static ClaudeRunResult? Interpret(string line, Action<string>? progress, Stopwatch sw, string transcriptPath)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "assistant" && progress is not null
                && root.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    var kind = block.TryGetProperty("type", out var k) ? k.GetString() : null;
                    if (kind == "text" && block.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } s)
                        progress(s.Trim());
                    else if (kind == "tool_use" && block.TryGetProperty("input", out var input))
                        progress($"[{block.GetProperty("name").GetString()}] {Summarize(input)}");
                }
            }

            if (type != "result")
                return null;

            var isError = root.TryGetProperty("is_error", out var e) && e.GetBoolean();
            var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;
            var summary = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : subtype ?? "";
            int? turns = root.TryGetProperty("num_turns", out var n) && n.TryGetInt32(out var nt) ? nt : null;
            double? cost = root.TryGetProperty("total_cost_usd", out var c) && c.TryGetDouble(out var cd) ? cd : null;
            return new ClaudeRunResult(!isError && subtype == "success", summary, turns, cost, sw.Elapsed, transcriptPath);
        }
    }

    private static string Summarize(JsonElement input)
    {
        foreach (var key in new[] { "command", "file_path", "path" })
            if (input.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString()!;
        var raw = input.GetRawText();
        return raw.Length > 160 ? raw[..160] + "…" : raw;
    }
}
