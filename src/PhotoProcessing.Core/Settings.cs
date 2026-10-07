namespace PhotoProcessing.Core;

/// <summary>Bound from the "PhotoProcessing" config section (appsettings.json, user secrets, PHOTOPROC_ env vars).</summary>
public sealed class PhotoProcessingSettings
{
    /// <summary>
    /// Runtime root: darktable config, the Claude editor workspace, jobs, logs.
    /// Deliberately not under AppData: files a packaged (MSIX) app such as the Claude desktop app
    /// creates there are redirected into its private package storage, invisible to the watcher task.
    /// </summary>
    public string Home { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".photoprocessing");

    /// <summary>darktable-cli.exe; empty = PATH, then the default per-user and machine install locations.</summary>
    public string DarktableCli { get; set; } = "";

    /// <summary>Long edge of the previews Claude looks at.</summary>
    public int PreviewSize { get; set; } = 1500;

    public string DarktableConfigDir => Path.Combine(Home, "darktable-config");
    public string EditorDir => Path.Combine(Home, "editor");
    public string JobsDir => Path.Combine(EditorDir, "jobs");
    public string LogsDir => Path.Combine(Home, "logs");

    public string ResolveDarktableCli()
    {
        if (!string.IsNullOrWhiteSpace(DarktableCli))
            return File.Exists(DarktableCli) ? DarktableCli : throw new FileNotFoundException("configured darktable-cli not found", DarktableCli);

        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, "darktable-cli.exe"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "darktable", "bin", "darktable-cli.exe"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "darktable", "bin", "darktable-cli.exe"));

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("darktable-cli.exe not found; set PhotoProcessing:DarktableCli");
    }
}
