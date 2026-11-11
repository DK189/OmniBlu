using Avalonia.Input;
using OmniBlu.Managers;

namespace OmniBlu.UI;

/// <summary>Turns a key press in the window into a <see cref="HotkeyCombo"/> when the user chooses a new shortcut.</summary>
internal static class HotkeyCapture
{
    /// <summary>True for the keys that are only modifiers. Pressing one alone does not finish the choice.</summary>
    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>
    /// Gives the shortcut for a key press. Returns false when the key cannot be used (not supported) or when no
    /// modifier is held, because a shortcut without a modifier would take over a normal key.
    /// </summary>
    public static bool TryFrom(Key key, KeyModifiers modifiers, out HotkeyCombo combo)
    {
        combo = null;

        var name = NameOf(key);
        if (name == null)
        {
            return false;
        }

        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= HotkeyModifiers.Ctrl;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= HotkeyModifiers.Win;
        }

        if (result == HotkeyModifiers.None)
        {
            return false;
        }

        combo = new HotkeyCombo(result, name);
        return true;
    }

    private static string NameOf(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            return key.ToString();
        }

        return key switch
        {
            Key.Space => "Space",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.Pause => "Pause",
            Key.OemTilde => "`",
            Key.OemQuestion => "/",
            _ => null,
        };
    }
}
