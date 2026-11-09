using Avalonia.Input;

namespace OmniBlu.UI;

/// <summary>Turns the keys of this PC into HID keyboard codes, for control mode.</summary>
public static class KeyMapper
{
    /// <summary>The key that ends control mode. It is not sent to the phone.</summary>
    public const Key ExitKey = Key.RightCtrl;

    private static readonly Dictionary<Key, byte> Modifiers = new()
    {
        [Key.LeftCtrl] = 0x01,
        [Key.LeftShift] = 0x02,
        [Key.LeftAlt] = 0x04,
        [Key.LWin] = 0x08,
        [Key.RightShift] = 0x20,
        [Key.RightAlt] = 0x40,
        [Key.RWin] = 0x80,
    };

    private static readonly Dictionary<Key, byte> Keys = BuildKeys();

    /// <summary>
    /// The key for a Windows virtual-key code, for the few keys that are kept away from the PC in control mode
    /// (see <see cref="OmniBlu.Managers.KeyBlockRules"/>): the Windows keys, Tab and Escape.
    /// </summary>
    public static bool TryGetKeyFromVirtualKey(int virtualKey, out Key key)
    {
        key = virtualKey switch
        {
            Managers.KeyBlockRules.VkLeftWin => Key.LWin,
            Managers.KeyBlockRules.VkRightWin => Key.RWin,
            Managers.KeyBlockRules.VkTab => Key.Tab,
            Managers.KeyBlockRules.VkEscape => Key.Escape,
            _ => Key.None,
        };

        return key != Key.None;
    }

    /// <summary>Modifier keys (Shift, Ctrl...) are a bit in the report, not a key slot.</summary>
    public static bool TryGetModifier(Key key, out byte bit) => Modifiers.TryGetValue(key, out bit);

    /// <summary>The HID usage code of a normal key. Returns false for keys with no code here.</summary>
    public static bool TryGetUsage(Key key, out byte usage) => Keys.TryGetValue(key, out usage);

    private static Dictionary<Key, byte> BuildKeys()
    {
        var map = new Dictionary<Key, byte>();

        for (var i = 0; i < 26; i++)
        {
            map[Key.A + i] = (byte)(0x04 + i);
        }

        for (var i = 0; i < 9; i++)
        {
            map[Key.D1 + i] = (byte)(0x1E + i);
        }

        map[Key.D0] = 0x27;

        for (var i = 0; i < 12; i++)
        {
            map[Key.F1 + i] = (byte)(0x3A + i);
        }

        map[Key.Enter] = 0x28;
        map[Key.Escape] = 0x29;
        map[Key.Back] = 0x2A;
        map[Key.Tab] = 0x2B;
        map[Key.Space] = 0x2C;
        map[Key.OemMinus] = 0x2D;
        map[Key.OemPlus] = 0x2E;
        map[Key.OemOpenBrackets] = 0x2F;
        map[Key.OemCloseBrackets] = 0x30;
        map[Key.OemPipe] = 0x31;
        map[Key.OemSemicolon] = 0x33;
        map[Key.OemQuotes] = 0x34;
        map[Key.OemTilde] = 0x35;
        map[Key.OemComma] = 0x36;
        map[Key.OemPeriod] = 0x37;
        map[Key.OemQuestion] = 0x38;
        map[Key.CapsLock] = 0x39;
        map[Key.Insert] = 0x49;
        map[Key.Home] = 0x4A;
        map[Key.PageUp] = 0x4B;
        map[Key.Delete] = 0x4C;
        map[Key.End] = 0x4D;
        map[Key.PageDown] = 0x4E;
        map[Key.Right] = 0x4F;
        map[Key.Left] = 0x50;
        map[Key.Down] = 0x51;
        map[Key.Up] = 0x52;

        return map;
    }
}
