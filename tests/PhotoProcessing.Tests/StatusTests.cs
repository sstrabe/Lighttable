using PhotoProcessing.Core.Status;

namespace PhotoProcessing.Tests;

public sealed class StatusTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "photoedit-status-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Status_round_trips_and_stamps_the_update_time()
    {
        var store = new StatusStore(_home);
        Assert.Null(store.Read());

        var finished = new DateTime(2026, 10, 3, 12, 48, 34, DateTimeKind.Utc);
        store.Write(new WatcherStatus
        {
            State = WatcherState.Processing,
            ProcessId = 42,
            PollInterval = TimeSpan.FromMinutes(1),
            CurrentPhoto = "IMG_4865.CR2",
            CurrentStage = "Claude is rendering a preview",
            Last = new LastResult("IMG_4899.CR2", "Shared/Processing/Processed/IMG_4899.jpg", finished, false, "done", null),
        });

        var read = store.Read()!;
        Assert.Equal(WatcherState.Processing, read.State);
        Assert.Equal("IMG_4865.CR2", read.CurrentPhoto);
        Assert.Equal(TimeSpan.FromMinutes(1), read.PollInterval);
        Assert.Equal(finished, read.Last!.FinishedUtc);
        Assert.True(DateTime.UtcNow - read.UpdatedUtc < TimeSpan.FromMinutes(1));
        Assert.Contains("\"processing\"", File.ReadAllText(store.StatusPath));
    }

    [Fact]
    public void Poll_request_is_consumed_once()
    {
        var store = new StatusStore(_home);
        Assert.False(store.ConsumePollRequest());
        store.RequestPoll();
        Assert.True(store.ConsumePollRequest());
        Assert.False(store.ConsumePollRequest());
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }
}
