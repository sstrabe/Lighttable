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

    public ClaudeSettings Claude { get; set; } = new();
    public NextcloudSettings Nextcloud { get; set; } = new();
    public HeimdallSettings Heimdall { get; set; } = new();

    public string DarktableConfigDir => Path.Combine(Home, "darktable-config");

    /// <summary>The Heimdall sign-in (refresh token, username) that `photoedit login` stores.</summary>
    public string HeimdallSignInPath => Path.Combine(Home, "state", "heimdall.json");
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

public sealed class ClaudeSettings
{
    /// <summary>claude CLI executable (resolved from PATH by default).</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>
    /// <see cref="Executable"/> as a full path: PATH first, then the native installer's
    /// %USERPROFILE%\.local\bin (a task started at boot may not have the user's PATH).
    /// </summary>
    public string ResolveExecutable()
    {
        if (Path.IsPathRooted(Executable))
            return Executable;

        var name = Path.HasExtension(Executable) ? Executable : Executable + ".exe";
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", name))
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"'{Executable}' not found on PATH or in ~/.local/bin; set PhotoProcessing:Claude:Executable");
    }

    /// <summary>Optional model alias/id passed as --model; empty = the CLI's default for your plan.</summary>
    public string Model { get; set; } = "";

    /// <summary>Optional --effort level (low, medium, high, xhigh, max); empty = CLI default.</summary>
    public string Effort { get; set; } = "";

    public int MaxTurns { get; set; } = 60;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(20);
}

public sealed class NextcloudSettings
{
    /// <summary>Server base URL, e.g. https://cloud.sstrabe.dev</summary>
    public string BaseUrl { get; set; } = "https://cloud.sstrabe.dev";

    public string InboxFolder { get; set; } = "Photos/Processing/Inbox";
    public string OutputFolder { get; set; } = "Photos/Processing/Processed";
    public string ArchiveFolder { get; set; } = "Photos/Processing/Archive";
    public string FailedFolder { get; set; } = "Photos/Processing/Failed";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>A file must be unchanged for this long before it is picked up (guards half-finished uploads).</summary>
    public TimeSpan SettleTime { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 2;

    public string[] RawExtensions { get; set; } =
        [".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".raf", ".orf", ".rw2", ".pef", ".srw", ".x3f", ".3fr", ".iiq", ".erf", ".mos", ".mrw", ".kdc", ".rwl"];
}

/// <summary>Sign in with Heimdall (OpenID Connect, authorization code + PKCE); its tokens authorize Nextcloud WebDAV.</summary>
public sealed class HeimdallSettings
{
    /// <summary>The realm's issuer; the endpoints come from its discovery document.</summary>
    public string Issuer { get; set; } = "https://sso.heimdall.sstrabe.dev/realms/heimdall";

    /// <summary>From the app's OAuth2 page on Heimdall.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Only for a confidential app; a public app has none. Keep it in user secrets.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Registered on the app's OAuth2 page, exactly. `photoedit login` listens on this loopback address
    /// for Heimdall's redirect back.
    /// </summary>
    public string RedirectUri { get; set; } = "http://127.0.0.1:38517/callback";

    /// <summary>
    /// profile gives preferred_username, the WebDAV path. offline_access keeps the watcher signed in
    /// after you sign out of Heimdall, until it goes 30 days unused.
    /// </summary>
    public string Scope { get; set; } = "openid profile nextcloud.files.write offline_access";
}
