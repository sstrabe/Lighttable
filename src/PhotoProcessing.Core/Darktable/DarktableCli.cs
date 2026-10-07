using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoProcessing.Core.Darktable;

public sealed record RenderResult(string OutputPath, TimeSpan Duration, string Log);

/// <summary>
/// Runs darktable-cli against a private config dir, so the pipeline neither touches nor depends
/// on the darktable GUI's settings, presets or library.
/// </summary>
public sealed partial class DarktableCli(string executable, string configDir)
{
    /// <summary>
    /// darktablerc keys the pipeline depends on. darktable rewrites the file on exit, so these are
    /// re-applied before every run. metadata_flags 21 = EXIF (0x1) + develop history (0x20): the
    /// history embedded in the baseline JPEG is where the editable history stack comes from.
    /// </summary>
    private static readonly Dictionary<string, string> RequiredConfig = new()
    {
        ["plugins/lighttable/export/metadata_flags"] = "21",
        ["plugins/darkroom/workflow"] = "scene-referred (sigmoid)",
        ["plugins/darkroom/chromatic-adaptation"] = "modern",
        ["plugins/imageio/format/jpeg/quality"] = "95",
        ["write_sidecar_files"] = "never",
    };

    public string Executable { get; } = executable;

    public async Task<RenderResult> RenderAsync(
        string rawPath, string? xmpPath, string outputJpeg, int maxSize, bool highQuality,
        bool debugParams = false, CancellationToken ct = default)
    {
        // darktable keeps its config dir's databases locked; serialize runs across processes.
        await using var configLock = await AcquireLockAsync(ct);
        EnsureConfig();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputJpeg))!);
        // darktable never overwrites; it would write IMG_0001_01.jpg instead.
        File.Delete(outputJpeg);

        var args = new List<string> { rawPath };
        if (xmpPath is not null) args.Add(xmpPath);
        args.AddRange(
        [
            // The output name goes through darktable's variable expansion, where '\' is an escape
            // character: C:\a\b.jpg would become C:ab.jpg. Windows accepts forward slashes.
            Path.GetFullPath(outputJpeg).Replace('\\', '/'),
            "--width", maxSize.ToString(CultureInfo.InvariantCulture),
            "--height", maxSize.ToString(CultureInfo.InvariantCulture),
            "--hq", highQuality ? "true" : "false",
            "--core",
        ]);
        if (debugParams) args.AddRange(["-d", "params"]);
        // Keep darktable's cache (compiled OpenCL kernels, …) with the config instead of AppData.
        args.AddRange(["--configdir", configDir, "--cachedir", Path.Combine(configDir, "cache"), "--library", ":memory:"]);

        var psi = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start darktable-cli");
        var log = new StringBuilder();
        var stdout = PumpAsync(process.StandardOutput, log);
        var stderr = PumpAsync(process.StandardError, log);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"darktable-cli timed out rendering {rawPath}");
        }

        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0 || !File.Exists(outputJpeg))
            throw new InvalidOperationException(
                $"darktable-cli failed (exit {process.ExitCode}) rendering {rawPath}:\n{Tail(log.ToString(), 40)}");

        return new RenderResult(outputJpeg, sw.Elapsed, log.ToString());
    }

    /// <summary>The illuminant xy color calibration used, from a <c>-d params</c> render log.</summary>
    public static (double X, double Y)? ParseAsShotXy(string log)
    {
        var match = CommitColorCalibration().Matches(log).LastOrDefault();
        if (match is null) return null;
        return (double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, "photoedit.lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250, ct);
            }
        }
    }

    private void EnsureConfig()
    {
        Directory.CreateDirectory(configDir);
        var rcPath = Path.Combine(configDir, "darktablerc");
        var lines = File.Exists(rcPath) ? File.ReadAllLines(rcPath).ToList() : [];
        foreach (var (key, value) in RequiredConfig)
        {
            var index = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
            var line = $"{key}={value}";
            if (index >= 0) lines[index] = line; else lines.Add(line);
        }

        File.WriteAllLines(rcPath, lines);
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder log)
    {
        while (await reader.ReadLineAsync() is { } line)
            lock (log) log.AppendLine(line);
    }

    private static string Tail(string text, int lines) =>
        string.Join('\n', text.Split('\n').TakeLast(lines));

    [GeneratedRegex(@"\[commit color calibration\][^\n]*?xy=([0-9.]+) ([0-9.]+)")]
    private static partial Regex CommitColorCalibration();
}
