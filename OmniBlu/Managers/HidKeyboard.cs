namespace OmniBlu.Managers;

/// <summary>A key to press: a HID usage code plus modifier bits (for example Shift).</summary>
public readonly record struct HidKey(byte Usage, byte Modifiers);

/// <summary>
/// Turns characters and key names into HID keyboard codes. A HID keyboard sends key positions, not characters, so the
/// phone turns them into letters with its own layout. This table is for the US layout, which most phones use by
/// default. Characters outside it (accents, other scripts) cannot be sent this way.
/// </summary>
public static class HidKeyboard
{
    public const byte ShiftModifier = 0x02;

    private const string Unshifted = "-=[]\\;'`,./";
    private const string Shifted = "_+{}|:\"~<>?";
    private static readonly byte[] PunctuationUsages = { 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38 };
    private const string ShiftedDigits = ")!@#$%^&*(";

    private static readonly Dictionary<string, byte> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x28,
        ["Escape"] = 0x29,
        ["Backspace"] = 0x2A,
        ["Tab"] = 0x2B,
        ["Space"] = 0x2C,
        ["Delete"] = 0x4C,
        ["Home"] = 0x4A,
        ["PageUp"] = 0x4B,
        ["End"] = 0x4D,
        ["PageDown"] = 0x4E,
        ["Right"] = 0x4F,
        ["Left"] = 0x50,
        ["Down"] = 0x51,
        ["Up"] = 0x52,
    };

    /// <summary>Finds a key by name, such as "Enter", "Escape" or "Up". Names are not case sensitive.</summary>
    public static bool TryGetNamed(string name, out byte usage)
    {
        usage = 0;
        return name != null && Named.TryGetValue(name, out usage);
    }

    /// <summary>Finds the key that types a character on a US layout. Returns false for characters it cannot type.</summary>
    public static bool TryMap(char c, out HidKey key)
    {
        key = default;

        if (c is >= 'a' and <= 'z')
        {
            key = new HidKey((byte)(0x04 + (c - 'a')), 0);
        }
        else if (c is >= 'A' and <= 'Z')
        {
            key = new HidKey((byte)(0x04 + (c - 'A')), ShiftModifier);
        }
        else if (c is >= '1' and <= '9')
        {
            key = new HidKey((byte)(0x1E + (c - '1')), 0);
        }
        else if (c == '0')
        {
            key = new HidKey(0x27, 0);
        }
        else if (ShiftedDigits.IndexOf(c) is var digit and >= 0)
        {
            key = new HidKey(digit == 0 ? (byte)0x27 : (byte)(0x1E + digit - 1), ShiftModifier);
        }
        else if (Unshifted.IndexOf(c) is var plain and >= 0)
        {
            key = new HidKey(PunctuationUsages[plain], 0);
        }
        else if (Shifted.IndexOf(c) is var shift and >= 0)
        {
            key = new HidKey(PunctuationUsages[shift], ShiftModifier);
        }
        else if (c == ' ')
        {
            key = new HidKey(Named["Space"], 0);
        }
        else if (c is '\n' or '\r')
        {
            key = new HidKey(Named["Enter"], 0);
        }
        else if (c == '\t')
        {
            key = new HidKey(Named["Tab"], 0);
        }
        else
        {
            return false;
        }

        return true;
    }
}
