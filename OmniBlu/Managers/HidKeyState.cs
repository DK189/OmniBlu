namespace OmniBlu.Managers;

/// <summary>
/// Which keys are held down right now, as a HID keyboard report. A real keyboard reports its whole state each time:
/// the modifier bits plus up to 6 pressed keys.
/// </summary>
public sealed class HidKeyState
{
    public const int MaxKeys = 6;

    private readonly object sync = new();
    private readonly List<byte> keys = new();
    private byte modifiers;

    /// <summary>Marks a key as pressed. Returns the new report, or null if nothing changed (for example key repeat).</summary>
    public byte[] Press(byte usage)
    {
        lock (sync)
        {
            if (keys.Contains(usage) || keys.Count >= MaxKeys)
            {
                return null;
            }

            keys.Add(usage);
            return Report();
        }
    }

    /// <summary>Marks a key as released. Returns the new report, or null if the key was not held.</summary>
    public byte[] Release(byte usage)
    {
        lock (sync)
        {
            return keys.Remove(usage) ? Report() : null;
        }
    }

    /// <summary>Presses or releases a modifier (for example Shift). Returns the new report, or null if nothing changed.</summary>
    public byte[] SetModifier(byte bit, bool down)
    {
        lock (sync)
        {
            var updated = (byte)(down ? modifiers | bit : modifiers & ~bit);
            if (updated == modifiers)
            {
                return null;
            }

            modifiers = updated;
            return Report();
        }
    }

    /// <summary>Releases everything. Always returns a report, so the phone is told even if nothing seemed held.</summary>
    public byte[] Clear()
    {
        lock (sync)
        {
            keys.Clear();
            modifiers = 0;
            return Report();
        }
    }

    private byte[] Report()
    {
        var report = new byte[HidReports.KeyboardReportSize];
        report[0] = modifiers;
        for (var i = 0; i < keys.Count; i++)
        {
            report[2 + i] = keys[i];
        }

        return report;
    }
}
