using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class PairingModeTests
{
    /// <summary>A service that only counts the pairing calls. Everything else is not needed here.</summary>
    private sealed class FakeHid : IHidService
    {
        public int Started;
        public int Stopped;
        public bool StartResult = true;

        public event EventHandler<HidStateChangedEventArgs> StateChanged { add { } remove { } }

        public event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected;

        public event EventHandler<string> LogMessage { add { } remove { } }

        public void RaiseUnknownClient() => UnknownClientConnected?.Invoke(this, new UnknownHidClientEventArgs("ble:1", "Ally", "74:97:79:56:ee:38"));

        public Task<bool> StartPairingAsync()
        {
            if (StartResult)
            {
                Interlocked.Increment(ref Started);
            }

            return Task.FromResult(StartResult);
        }

        public Task StopPairingAsync()
        {
            Interlocked.Increment(ref Stopped);
            return Task.CompletedTask;
        }

        public Task<bool> EnableAsync(string key) => Task.FromResult(true);

        public Task DisableAsync(string key) => Task.CompletedTask;

        public Task<bool> SendClickAsync(string key) => Task.FromResult(false);

        public Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0) => Task.FromResult(false);

        public Task<bool> SendKeyboardReportAsync(string key, byte[] report) => Task.FromResult(false);

        public Task<bool> SendMouseReportAsync(string key, byte[] report) => Task.FromResult(false);

        public Task<int> SendTextAsync(string key, string text) => Task.FromResult(0);
    }

    /// <summary>A delay that the test finishes by hand, so no test waits for real time.</summary>
    private sealed class ManualClock
    {
        private readonly TaskCompletionSource<bool> due = new();
        public TimeSpan Asked;

        public Task Delay(TimeSpan time, CancellationToken token)
        {
            Asked = time;
            token.Register(() => due.TrySetCanceled(token));
            return due.Task;
        }

        public void TimeIsUp() => due.TrySetResult(true);
    }

    private static (PairingMode Mode, FakeHid Hid, ManualClock Clock, List<PairingChangedEventArgs> Events) Create(bool startOk = true)
    {
        var hid = new FakeHid { StartResult = startOk };
        var clock = new ManualClock();
        var mode = new PairingMode(hid, TimeSpan.FromMinutes(2), clock.Delay);
        var events = new List<PairingChangedEventArgs>();
        mode.Changed += (_, e) => { lock (events) { events.Add(e); } };
        return (mode, hid, clock, events);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }

    [Fact]
    public async Task Starting_turns_the_service_on_and_waits_for_the_time_limit()
    {
        var (mode, hid, clock, events) = Create();

        Assert.True(await mode.StartAsync());

        Assert.True(mode.IsActive);
        Assert.Equal(1, hid.Started);
        Assert.Equal(TimeSpan.FromMinutes(2), clock.Asked);
        var started = Assert.Single(events);
        Assert.True(started.IsActive);
        Assert.Null(started.Reason);
    }

    [Fact]
    public async Task Starting_twice_does_not_start_the_service_twice()
    {
        var (mode, hid, _, events) = Create();

        await mode.StartAsync();
        Assert.True(await mode.StartAsync());

        Assert.Equal(1, hid.Started);
        Assert.Single(events);
    }

    [Fact]
    public async Task When_the_service_cannot_advertise_pairing_mode_stays_off()
    {
        var (mode, hid, _, events) = Create(startOk: false);

        Assert.False(await mode.StartAsync());

        Assert.False(mode.IsActive);
        Assert.Equal(0, hid.Started);
        Assert.Empty(events);
    }

    [Fact]
    public async Task The_time_limit_ends_pairing_mode_and_turns_the_service_off()
    {
        var (mode, hid, clock, events) = Create();
        await mode.StartAsync();

        clock.TimeIsUp();
        await WaitUntil(() => !mode.IsActive);

        Assert.Equal(1, hid.Stopped);
        Assert.Equal(PairingEndReason.TimeUp, events.Last().Reason);
        Assert.False(events.Last().IsActive);
    }

    [Fact]
    public async Task The_user_can_stop_it_and_the_timer_does_not_stop_it_a_second_time()
    {
        var (mode, hid, clock, events) = Create();
        await mode.StartAsync();

        await mode.StopAsync();
        clock.TimeIsUp(); // too late: the timer was cancelled
        await Task.Delay(100);

        Assert.False(mode.IsActive);
        Assert.Equal(1, hid.Stopped);
        Assert.Equal(PairingEndReason.Stopped, events.Last().Reason);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task A_new_device_that_connects_ends_pairing_mode()
    {
        var (mode, hid, _, events) = Create();
        await mode.StartAsync();

        hid.RaiseUnknownClient();
        await WaitUntil(() => !mode.IsActive);

        Assert.Equal(1, hid.Stopped);
        Assert.Equal(PairingEndReason.DeviceConnected, events.Last().Reason);
    }

    [Fact]
    public async Task A_new_device_when_pairing_mode_is_off_changes_nothing()
    {
        var (mode, hid, _, events) = Create();

        hid.RaiseUnknownClient();
        await Task.Delay(100);

        Assert.False(mode.IsActive);
        Assert.Equal(0, hid.Stopped);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Stopping_when_it_is_off_does_nothing()
    {
        var (mode, hid, _, events) = Create();

        await mode.StopAsync();

        Assert.Equal(0, hid.Stopped);
        Assert.Empty(events);
    }

    [Fact]
    public async Task It_can_be_started_again_after_it_ended()
    {
        var (mode, hid, _, _) = Create();
        await mode.StartAsync();
        await mode.StopAsync();

        Assert.True(await mode.StartAsync());

        Assert.True(mode.IsActive);
        Assert.Equal(2, hid.Started);
        Assert.Equal(1, hid.Stopped);
    }

    [Fact]
    public void The_default_time_limit_is_two_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), PairingMode.DefaultDuration);
    }
}
