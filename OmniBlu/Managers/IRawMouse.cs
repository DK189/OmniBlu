namespace OmniBlu.Managers;

/// <summary>
/// Reads the movement of the mouse as the mouse itself reports it (raw input), before the operating system applies
/// pointer speed and acceleration. This is what a real mouse connected to the phone would send.
/// </summary>
public interface IRawMouse
{
    /// <summary>False on platforms where raw input is not implemented. Control mode then reads the cursor position.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Starts reading. <paramref name="onMove"/> is called on a background thread with the movement in device counts
    /// (right and down are positive). Returns false when reading could not start.
    /// </summary>
    bool Start(Action<int, int> onMove);

    /// <summary>Why the last <see cref="Start"/> failed, for the log. Null when it did not fail.</summary>
    string LastError { get; }

    /// <summary>Stops reading. Safe to call at any time, even when not started.</summary>
    void Stop();
}

/// <summary>Rules for turning a raw mouse event into a movement.</summary>
public static class RawMouse
{
    /// <summary>Flag in a raw mouse event: the position is absolute (touch screen, pen, remote desktop), not a movement.</summary>
    public const ushort MoveAbsolute = 0x01;

    /// <summary>
    /// Gives the movement of a raw mouse event. Absolute positions are not movement and are ignored. A pointer
    /// device that sends them would otherwise look like a huge jump.
    /// </summary>
    public static bool TryGetRelative(ushort flags, int x, int y, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;

        if ((flags & MoveAbsolute) != 0 || (x == 0 && y == 0))
        {
            return false;
        }

        dx = x;
        dy = y;
        return true;
    }
}
