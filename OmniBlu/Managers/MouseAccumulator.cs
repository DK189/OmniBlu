namespace OmniBlu.Managers;

/// <summary>
/// Collects mouse movement, wheel and button changes and hands them out as HID mouse reports. A real mouse can move
/// thousands of times a second, but a Bluetooth link cannot carry that many reports. Adding the movements up and
/// sending the sum at the pace of the link keeps the pointer smooth.
/// <para>
/// One report holds at most <c>maxStep</c> units per axis. A bigger move is spread over several reports, so no distance
/// is lost, but the caller must send them one per tick, not all at once: the phone works out speed (and pointer
/// acceleration) from the size of each report, so a burst looks like a very fast flick.
/// </para>
/// </summary>
public sealed class MouseAccumulator
{
    public const byte LeftButton = 0x01;
    public const byte RightButton = 0x02;
    public const byte MiddleButton = 0x04;

    private readonly object sync = new();
    private readonly int maxStep;
    private double speed = 1.0;
    private double carryX;
    private double carryY;
    private int dx;
    private int dy;
    private int wheel;
    private byte buttons;
    private bool buttonsChanged;

    public MouseAccumulator(int maxStep = 127)
    {
        this.maxStep = Math.Clamp(maxStep, 1, 127);
    }

    /// <summary>Multiplies all movement. 1.0 is unchanged. The part of a step that is lost to rounding is kept for later.</summary>
    public double Speed
    {
        get
        {
            lock (sync)
            {
                return speed;
            }
        }
        set
        {
            lock (sync)
            {
                speed = Math.Clamp(value, 0.05, 10);
            }
        }
    }

    /// <summary>The biggest distance still waiting to be sent on one axis.</summary>
    public int PendingMagnitude
    {
        get
        {
            lock (sync)
            {
                return Math.Max(Math.Abs(dx), Math.Abs(dy));
            }
        }
    }

    public void Move(int x, int y)
    {
        lock (sync)
        {
            // Keep the fraction: at a speed below 1, slow movement would otherwise round down to nothing.
            var scaledX = (x * speed) + carryX;
            var scaledY = (y * speed) + carryY;
            var wholeX = (int)Math.Truncate(scaledX);
            var wholeY = (int)Math.Truncate(scaledY);
            carryX = scaledX - wholeX;
            carryY = scaledY - wholeY;

            dx += wholeX;
            dy += wholeY;
        }
    }

    public void Scroll(int notches)
    {
        lock (sync)
        {
            wheel += notches;
        }
    }

    public void SetButton(byte mask, bool down)
    {
        lock (sync)
        {
            var updated = (byte)(down ? buttons | mask : buttons & ~mask);
            if (updated != buttons)
            {
                buttons = updated;
                buttonsChanged = true;
            }
        }
    }

    /// <summary>Forgets movement and wheel that were not sent yet. Buttons are kept. Used when control ends.</summary>
    public void DiscardMovement()
    {
        lock (sync)
        {
            dx = 0;
            dy = 0;
            wheel = 0;
            carryX = 0;
            carryY = 0;
        }
    }

    /// <summary>Takes the next report. Returns false when there is nothing to send.</summary>
    public bool TryTake(out byte[] report)
    {
        lock (sync)
        {
            if (dx == 0 && dy == 0 && wheel == 0 && !buttonsChanged)
            {
                report = null;
                return false;
            }

            var x = Math.Clamp(dx, -maxStep, maxStep);
            var y = Math.Clamp(dy, -maxStep, maxStep);
            var w = Math.Clamp(wheel, -127, 127);
            dx -= x;
            dy -= y;
            wheel -= w;
            buttonsChanged = false;

            report = HidReports.Mouse(buttons, (sbyte)x, (sbyte)y, (sbyte)w);
            return true;
        }
    }
}
