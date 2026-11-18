namespace OmniBlu.Managers;

/// <summary>Why pairing mode ended.</summary>
public enum PairingEndReason
{
    /// <summary>The user stopped it.</summary>
    Stopped,

    /// <summary>The time limit was reached.</summary>
    TimeUp,

    /// <summary>A new device connected over HID. It is added to the device list, so the job is done.</summary>
    DeviceConnected,
}

/// <summary>Tells that pairing mode started or ended.</summary>
public sealed class PairingChangedEventArgs : EventArgs
{
    public PairingChangedEventArgs(bool isActive, PairingEndReason? reason)
    {
        IsActive = isActive;
        Reason = reason;
    }

    /// <summary>True when pairing mode just started; false when it just ended.</summary>
    public bool IsActive { get; }

    /// <summary>Why it ended, or null when it just started.</summary>
    public PairingEndReason? Reason { get; }
}

/// <summary>
/// Pairing mode: this PC advertises as a Bluetooth keyboard and mouse without any device in the Controllable row, so a new
/// computer or phone that was never paired can find it and pair. It ends by itself after a time limit, or when a new device
/// connects over HID (that device is then added to the list), or when the user stops it.
/// </summary>
public sealed class PairingMode
{
    /// <summary>How long pairing mode lasts when nobody stops it.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(2);

    private readonly IHidService hid;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource timer;
    private volatile bool active;

    /// <param name="hid">The service that advertises.</param>
    /// <param name="duration">The time limit.</param>
    /// <param name="delay">Waits for the time limit. It is replaced in tests; the default is <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public PairingMode(IHidService hid, TimeSpan duration, Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        this.hid = hid;
        Duration = duration;
        this.delay = delay ?? Task.Delay;
        hid.UnknownClientConnected += (_, _) => _ = EndAsync(PairingEndReason.DeviceConnected);
    }

    /// <summary>Raised when pairing mode starts and when it ends. It can be raised on any thread.</summary>
    public event EventHandler<PairingChangedEventArgs> Changed;

    /// <summary>True while this PC advertises for pairing.</summary>
    public bool IsActive => active;

    /// <summary>The time limit.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Starts pairing mode. Returns false when advertising could not start. Starting while it is on does nothing.</summary>
    public async Task<bool> StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (active)
            {
                return true;
            }

            if (!await hid.StartPairingAsync())
            {
                return false;
            }

            active = true;
            timer = new CancellationTokenSource();
            _ = RunTimerAsync(timer.Token);
        }
        finally
        {
            gate.Release();
        }

        Changed?.Invoke(this, new PairingChangedEventArgs(true, null));
        return true;
    }

    /// <summary>Stops pairing mode. It does nothing when it is not on.</summary>
    public Task StopAsync() => EndAsync(PairingEndReason.Stopped);

    private async Task RunTimerAsync(CancellationToken token)
    {
        try
        {
            await delay(Duration, token);
        }
        catch (OperationCanceledException)
        {
            return; // pairing mode ended for another reason
        }

        await EndAsync(PairingEndReason.TimeUp);
    }

    private async Task EndAsync(PairingEndReason reason)
    {
        await gate.WaitAsync();
        try
        {
            if (!active)
            {
                return;
            }

            active = false;
            timer?.Cancel();
            timer?.Dispose();
            timer = null;

            try
            {
                await hid.StopPairingAsync();
            }
            catch (Exception)
            {
                // The service writes its own log line. Pairing mode is over either way.
            }
        }
        finally
        {
            gate.Release();
        }

        Changed?.Invoke(this, new PairingChangedEventArgs(false, reason));
    }
}
