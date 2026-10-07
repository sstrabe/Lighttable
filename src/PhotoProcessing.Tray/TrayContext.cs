using System.ComponentModel;
using System.Diagnostics;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Status;

namespace PhotoProcessing.Tray;

/// <summary>
/// Notification-area icon for the watcher. The watcher runs as a boot-time task in session 0 and
/// can't show UI, so this reads the status file it publishes (state/status.json) every 2 seconds.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string WatcherTask = "PhotoProcessing Watcher";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private sealed record View(Color Color, bool Spinning, string Headline, string Detail, string Tooltip, bool Problem);

    private readonly PhotoProcessingSettings _settings;
    private readonly string _folderUrl;
    private readonly StatusStore _store;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _headline = new() { Enabled = false };
    private readonly ToolStripMenuItem _detail = new() { Enabled = false };
    private readonly ToolStripMenuItem _last = new() { Enabled = false };
    private readonly ToolStripMenuItem _openLast;
    private readonly ToolStripMenuItem _signIn;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = (int)RefreshInterval.TotalMilliseconds };
    private readonly System.Windows.Forms.Timer _spin = new() { Interval = 110 };
    private readonly Dictionary<Color, Icon> _icons = new();
    private Icon[]? _spinner;
    private int _frame;
    private View? _view;
    private WatcherStatus? _status;
    private DateTime? _lastAnnounced;
    private bool _problemAnnounced;

    public TrayContext(PhotoProcessingSettings settings, string folderUrl)
    {
        _settings = settings;
        _folderUrl = folderUrl;
        _store = new StatusStore(settings.Home);

        _openLast = new ToolStripMenuItem("Open last edit (previews && notes)", null, (_, _) => OpenLastJob());
        _signIn = new ToolStripMenuItem("Sign in to Heimdall…", null, (_, _) => SignIn());
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _headline,
            _detail,
            _last,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Check inbox now", null, (_, _) => CheckNow()),
            new ToolStripMenuItem("Open Nextcloud folder", null, (_, _) => Open(_folderUrl)) { Font = new Font(menu.Font, FontStyle.Bold) },
            _openLast,
            new ToolStripMenuItem("Open today's log", null, (_, _) => OpenLog()),
            new ToolStripSeparator(),
            _signIn,
            new ToolStripMenuItem("Restart watcher", null, (_, _) => RestartWatcher()),
            new ToolStripMenuItem("Hide this icon", null, (_, _) => Quit()),
        ]);

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => Open(_folderUrl);
        _icon.BalloonTipClicked += (_, _) => Open(_folderUrl);

        _refresh.Tick += (_, _) => Refresh();
        _spin.Tick += (_, _) =>
        {
            _spinner ??= Enumerable.Range(0, TrayIcons.SpinnerFrames).Select(f => TrayIcons.Create(TrayIcons.Busy, f)).ToArray();
            _frame = (_frame + 1) % _spinner.Length;
            _icon.Icon = _spinner[_frame];
        };

        // Don't announce a photo that finished before the icon started.
        _lastAnnounced = _store.Read()?.Last?.FinishedUtc;
        Refresh();
        _refresh.Start();
    }

    private void Refresh()
    {
        _status = _store.Read();
        var view = Compute(_status);

        if (_view is null || view.Color != _view.Color || view.Spinning != _view.Spinning)
        {
            if (view.Spinning) _spin.Start();
            else
            {
                _spin.Stop();
                _icon.Icon = StaticIcon(view.Color);
            }
        }

        _icon.Text = Truncate(view.Tooltip, 127);
        _headline.Text = view.Headline;
        _detail.Text = view.Detail;
        _detail.Visible = view.Detail.Length > 0;

        var last = _status?.Last;
        _last.Text = last is null ? "No photos edited yet"
            : $"Last: {Path.GetFileNameWithoutExtension(last.Photo)}.jpg · {Local(last.FinishedUtc)}{(last.Fallback ? " (fallback export)" : "")}";
        _openLast.Enabled = last?.JobDir is { } dir && Directory.Exists(dir);

        Announce(view, last);
        _view = view;
    }

    private View Compute(WatcherStatus? s)
    {
        if (s is null)
            return new(TrayIcons.Unknown, false, "Watcher hasn't reported yet", $"Is the '{WatcherTask}' task installed?",
                "PhotoProcessing: no status yet", false);

        if (!IsAlive(s.ProcessId))
            return new(TrayIcons.Error, false, "Watcher is not running", $"Last seen {Local(s.UpdatedUtc)}",
                "PhotoProcessing: watcher is not running", true);

        var now = DateTime.UtcNow;
        switch (s.State)
        {
            case WatcherState.Processing:
                var elapsed = now - (s.CurrentSinceUtc ?? now);
                return new(TrayIcons.Busy, true, $"Editing {s.CurrentPhoto}", $"{s.CurrentStage} · {elapsed:m\\:ss}",
                    $"Editing {s.CurrentPhoto}\n{s.CurrentStage}", false);

            case WatcherState.Error:
                var retry = s.NextAttemptUtc is { } n ? $"Retrying at {Local(n)}" : "";
                return new(TrayIcons.Error, false, s.Message, retry, $"PhotoProcessing: {s.Message}", true);

            case WatcherState.Starting:
                return new(TrayIcons.Unknown, false, "Starting…", s.Message, "PhotoProcessing: starting", false);

            default:
                var stale = s.LastPollUtc is not { } polled || now - polled > s.PollInterval * 3 + TimeSpan.FromMinutes(2);
                if (stale)
                    return new(TrayIcons.Warning, false, "Inbox hasn't been checked recently",
                        s.LastPollUtc is { } p ? $"Last check {Local(p)}" : "", "PhotoProcessing: no recent inbox check", true);

                var headline = s.InboxCount == 0 ? "Watching the inbox" : $"{s.InboxCount} photo(s) waiting";
                return new(TrayIcons.Idle, false, headline, $"Last check {Local(s.LastPollUtc!.Value)}",
                    $"PhotoProcessing: {(s.InboxCount == 0 ? "inbox empty" : headline)}", false);
        }
    }

    private void Announce(View view, LastResult? last)
    {
        if (last is not null && last.FinishedUtc > (_lastAnnounced ?? DateTime.MinValue))
        {
            _lastAnnounced = last.FinishedUtc;
            var name = Path.GetFileNameWithoutExtension(last.Photo);
            _icon.ShowBalloonTip(8000, last.Fallback ? $"{name} exported (Claude did not finish)" : $"{name} edited",
                Truncate(last.Summary ?? "Saved to the Processed folder.", 200), last.Fallback ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        if (view.Problem && !_problemAnnounced)
        {
            _problemAnnounced = true;
            _icon.ShowBalloonTip(8000, "PhotoProcessing needs attention", $"{view.Headline}. {view.Detail}".Trim(' ', '.'), ToolTipIcon.Warning);
        }
        else if (!view.Problem)
        {
            _problemAnnounced = false;
        }
    }

    private void CheckNow()
    {
        _store.RequestPoll();
        _headline.Text = "Inbox check requested…";
    }

    private void OpenLastJob()
    {
        if (_status?.Last?.JobDir is { } dir && Directory.Exists(dir)) Open(dir);
    }

    private void OpenLog()
    {
        var log = Path.Combine(_settings.LogsDir, $"photoedit-{DateTime.Now:yyyyMMdd}.log");
        Open(File.Exists(log) ? log : _settings.LogsDir);
    }

    /// <summary>
    /// Runs `photoedit login`, which opens Heimdall in the browser, waits for the redirect back and
    /// wakes the watcher. The watcher can't do this itself: it has no desktop to open a browser on.
    /// </summary>
    private async void SignIn()
    {
        _signIn.Enabled = false;
        _headline.Text = "Signing in to Heimdall in your browser…";
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "photoedit.exe"), "login")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var message = LastLine(p.ExitCode == 0 ? await output : error);
            _icon.ShowBalloonTip(8000, p.ExitCode == 0 ? "Signed in to Heimdall" : "Heimdall sign-in failed",
                Truncate(message, 200), p.ExitCode == 0 ? ToolTipIcon.Info : ToolTipIcon.Error);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            _icon.ShowBalloonTip(8000, "Heimdall sign-in failed", Truncate(e.Message, 200), ToolTipIcon.Error);
        }
        finally
        {
            _signIn.Enabled = true;
        }

        static string LastLine(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
    }

    private void RestartWatcher()
    {
        _headline.Text = "Restarting watcher…";
        Task.Run(() =>
        {
            RunHidden("schtasks.exe", $"/End /TN \"{WatcherTask}\"");
            Thread.Sleep(1500);
            RunHidden("schtasks.exe", $"/Run /TN \"{WatcherTask}\"");
        });
    }

    private void Quit()
    {
        _refresh.Stop();
        _spin.Stop();
        _icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _refresh.Dispose();
            _spin.Dispose();
            foreach (var icon in _icons.Values.Concat(_spinner ?? [])) icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private Icon StaticIcon(Color color)
    {
        if (!_icons.TryGetValue(color, out var icon))
            _icons[color] = icon = TrayIcons.Create(color);
        return icon;
    }

    private static bool IsAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.ProcessName.Equals("photoedit", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return true; // exists but can't be inspected
        }
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { }
    }

    private static void RunHidden(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(10_000);
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("HH:mm");

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
