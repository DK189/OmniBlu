using System.Diagnostics;
using System.Threading.Channels;

namespace OmniBlu.Managers;

/// <summary>
/// One "control mode" session: sends the keyboard and mouse of this PC to one device until it is stopped.
/// Keyboard reports are sent in the order they happened. Mouse movement is added up and sent at a steady pace, one
/// report per tick. Stopping always releases every key and button, so nothing stays stuck on the phone.
/// </summary>
public sealed class HidControlSession
{
    /// <summary>After this many failed sends in a row, the phone is considered gone.</summary>
    private const int MaxFailures = 5;

    /// <summary>
    /// Biggest distance in one mouse report. Smaller than the 127 a report can hold: the phone's pointer acceleration
    /// reacts to the size of a report, so big reports look like sudden fast jumps.
    /// </summary>
    public const int MaxMouseStep = 40;

    /// <summary>Time between mouse reports. About the connection interval of a Bluetooth LE link.</summary>
    private static readonly TimeSpan MouseInterval = TimeSpan.FromMilliseconds(8);

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly IHidService hid;
    private readonly string key;
    private readonly HidKeyState keys = new();
    private readonly MouseAccumulator mouse;
    private readonly Channel<byte[]> keyboardReports = Channel.CreateUnbounded<byte[]>();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Task pump;
    private volatile bool stopRequested;
    private int stopped;

    // Numbers for the log, to tell apart "the link stalled" from "the mouse moved too fast".
    private int moves;
    private int largestMove;
    private int reports;
    private int largestReport;
    private int largestBacklog;
    private long slowestSend;

    /// <param name="maxMouseStep">Biggest distance in one mouse report. See <see cref="MaxMouseStep"/>.</param>
    public HidControlSession(IHidService hid, string key, int maxMouseStep = MaxMouseStep)
    {
        this.hid = hid;
        this.key = key;
        mouse = new MouseAccumulator(maxMouseStep);
        pump = Task.Run(PumpAsync);
    }

    /// <summary>Raised once, on a background thread, when the phone stops accepting input.</summary>
    public event EventHandler Faulted;

    /// <summary>Multiplies mouse movement (1.0 = unchanged). Can be changed while the session runs.</summary>
    public double MouseSpeed
    {
        get => mouse.Speed;
        set => mouse.Speed = value;
    }

    public void KeyDown(byte usage) => Enqueue(keys.Press(usage));

    public void KeyUp(byte usage) => Enqueue(keys.Release(usage));

    public void ModifierDown(byte bit) => Enqueue(keys.SetModifier(bit, true));

    public void ModifierUp(byte bit) => Enqueue(keys.SetModifier(bit, false));

    public void MouseMove(int dx, int dy)
    {
        var magnitude = Math.Max(Math.Abs(dx), Math.Abs(dy));
        Interlocked.Increment(ref moves);
        InterlockedMax(ref largestMove, magnitude);
        mouse.Move(dx, dy);
    }

    public void MouseButton(byte mask, bool down) => mouse.SetButton(mask, down);

    public void Scroll(int notches) => mouse.Scroll(notches);

    /// <summary>One line for the log: how the mouse behaved during the session.</summary>
    public string GetSummary() =>
        $"Mouse: {moves} movement(s), largest {largestMove}px at once; {reports} report(s), largest {largestReport}; " +
        $"most waiting to be sent {largestBacklog}; slowest send {slowestSend} ms; " +
        $"session {clock.Elapsed.TotalSeconds:0.0}s.";

    /// <summary>Releases all keys and buttons, sends that to the phone, and ends the session.</summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) == 1)
        {
            return;
        }

        // Movement that was not sent yet is no longer wanted. The releases below are.
        mouse.DiscardMovement();
        Enqueue(keys.Clear());
        mouse.SetButton(MouseAccumulator.LeftButton, false);
        mouse.SetButton(MouseAccumulator.RightButton, false);
        mouse.SetButton(MouseAccumulator.MiddleButton, false);
        stopRequested = true;

        // Give the pump a moment to send the release reports. Never wait forever: the phone may be gone.
        await Task.WhenAny(pump, Task.Delay(StopTimeout));
    }

    private void Enqueue(byte[] report)
    {
        if (report != null)
        {
            keyboardReports.Writer.TryWrite(report);
        }
    }

    private async Task PumpAsync()
    {
        var failures = 0;
        long nextMouseAt = 0;

        try
        {
            while (true)
            {
                var sentAnything = false;

                while (keyboardReports.Reader.TryRead(out var report))
                {
                    sentAnything = true;
                    failures = await hid.SendKeyboardReportAsync(key, report) ? 0 : failures + 1;
                    if (failures >= MaxFailures)
                    {
                        Faulted?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }

                // At most one mouse report per tick. Sending the pieces of a big move back to back would look like a
                // sudden fast flick to the phone. When stopping, the release goes out at once.
                var now = clock.ElapsedMilliseconds;
                if (stopRequested || now >= nextMouseAt)
                {
                    InterlockedMax(ref largestBacklog, mouse.PendingMagnitude);

                    if (mouse.TryTake(out var mouseReport))
                    {
                        sentAnything = true;
                        RecordReport(mouseReport, now);
                        nextMouseAt = now + (long)MouseInterval.TotalMilliseconds;

                        // A long send means the Bluetooth link stalled. Movement then piles up and is let out afterwards.
                        var sendStarted = clock.ElapsedMilliseconds;
                        failures = await hid.SendMouseReportAsync(key, mouseReport) ? 0 : failures + 1;
                        slowestSend = Math.Max(slowestSend, clock.ElapsedMilliseconds - sendStarted);

                        if (failures >= MaxFailures)
                        {
                            Faulted?.Invoke(this, EventArgs.Empty);
                            return;
                        }
                    }
                }

                if (!sentAnything)
                {
                    if (stopRequested)
                    {
                        return;
                    }

                    await Task.Delay(IdleDelay);
                }
            }
        }
        catch
        {
            // A broken send must end the session quietly. The window is told through Faulted.
            Faulted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RecordReport(byte[] report, long now)
    {
        reports++;
        largestReport = Math.Max(largestReport, Math.Max(Math.Abs((sbyte)report[1]), Math.Abs((sbyte)report[2])));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
