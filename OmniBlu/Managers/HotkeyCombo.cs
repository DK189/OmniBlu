namespace OmniBlu.Managers;

/// <summary>Modifier keys of a shortcut. The values are the ones Windows uses for <c>RegisterHotKey</c>.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A keyboard shortcut for the whole system, for example "Win+`" or "Ctrl+Alt+B". It always has at least one modifier, so it can
/// never take over a plain key.
/// </summary>
public sealed record HotkeyCombo(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>The shortcut used when the user has not chosen one.</summary>
    public const string DefaultText = "Win+`";

    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Pause"] = 0x13,
        ["`"] = 0xC0, // the key above Tab
    };

    /// <summary>The default shortcut.</summary>
    public static HotkeyCombo Default => TryParse(DefaultText, out var combo) ? combo : null;

    /// <summary>Windows virtual-key code of <see cref="Key"/>, or 0 when the key is not supported.</summary>
    public int VirtualKey => TryGetVirtualKey(Key, out var code) ? code : 0;

    /// <summary>
    /// Reads text such as "Ctrl+Alt+B". Keys that can be used: letters, digits, F1 to F12, Space, the arrow keys,
    /// Home, End, PageUp, PageDown, Insert, Delete, Pause and the ` key (above Tab). At least one modifier is needed.
    /// </summary>
    public static bool TryParse(string text, out HotkeyCombo combo)
    {
        combo = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Ctrl;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Win;
            }
            else if (key == null && TryGetVirtualKey(part, out _))
            {
                key = Normalize(part);
            }
            else
            {
                return false;
            }
        }

        if (key == null || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        combo = new HotkeyCombo(modifiers, key);
        return true;
    }

    /// <summary>True when <paramref name="key"/> can be used in a shortcut.</summary>
    public static bool IsSupportedKey(string key) => TryGetVirtualKey(key, out _);

    /// <summary>The text form, always in the same order: Ctrl, Alt, Shift, Win, then the key.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join("+", parts);
    }

    private static bool TryGetVirtualKey(string key, out int code)
    {
        code = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                code = c; // The virtual-key code of a letter or digit is its upper-case ASCII code.
                return true;
            }

            return NamedKeys.TryGetValue(key, out code);
        }

        if ((key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 12)
        {
            code = 0x70 + number - 1;
            return true;
        }

        return NamedKeys.TryGetValue(key, out code);
    }

    /// <summary>Gives a key its standard spelling: "b" becomes "B", "pageup" becomes "PageUp".</summary>
    private static string Normalize(string key)
    {
        if (key.Length == 1)
        {
            return key.ToUpperInvariant();
        }

        if (key[0] is 'F' or 'f')
        {
            return "F" + key[1..];
        }

        return NamedKeys.Keys.First(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
