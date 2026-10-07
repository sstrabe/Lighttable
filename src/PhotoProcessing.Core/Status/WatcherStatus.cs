using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoProcessing.Core.Status;

public enum WatcherState
{
    Starting,
    Idle,
    Processing,
    Error,
}

public sealed record LastResult(
    string Photo,
    string Output,
    DateTime FinishedUtc,
    bool Fallback,
    string? Summary,
    string? JobDir);

/// <summary>What the watcher is doing, published for the tray icon (state/status.json).</summary>
public sealed record WatcherStatus
{
    public WatcherState State { get; init; }
    public string Message { get; init; } = "";
    public int ProcessId { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public TimeSpan PollInterval { get; init; }
    public DateTime? LastPollUtc { get; init; }
    public DateTime? NextAttemptUtc { get; init; }
    public int InboxCount { get; init; }
    public string? CurrentPhoto { get; init; }
    public string? CurrentStage { get; init; }
    public DateTime? CurrentSinceUtc { get; init; }
    public LastResult? Last { get; init; }
    public int ProcessedSinceStart { get; init; }
}

/// <summary>
/// The status file and the "check now" request file under &lt;Home&gt;/state. Writes are atomic
/// (temp file + replace) so the tray never reads a half-written file.
/// </summary>
public sealed class StatusStore(string home)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string StatusPath => Path.Combine(home, "state", "status.json");
    public string PokePath => Path.Combine(home, "state", "poll-now");

    public void Write(WatcherStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);
        var temp = StatusPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status with { UpdatedUtc = DateTime.UtcNow }, Json));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, StatusPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50); // the tray may be reading it this instant
            }
        }
    }

    public WatcherStatus? Read()
    {
        try
        {
            using var stream = new FileStream(StatusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<WatcherStatus>(stream, Json);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Asks the watcher to check the inbox now instead of at its next poll.</summary>
    public void RequestPoll()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PokePath)!);
        File.WriteAllText(PokePath, DateTime.UtcNow.ToString("O"));
    }

    public bool ConsumePollRequest()
    {
        if (!File.Exists(PokePath)) return false;
        try { File.Delete(PokePath); } catch (IOException) { }
        return true;
    }
}
