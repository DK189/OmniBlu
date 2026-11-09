namespace OmniBlu.Managers;

/// <summary>
/// Which keys this PC must keep for itself while control mode runs. These keys would otherwise open the Start menu or
/// switch windows, and the window would lose the focus and end control mode. They are sent to the phone instead.
/// </summary>
public static class KeyBlockRules
{
    /// <summary>Windows virtual-key code: Tab.</summary>
    public const int VkTab = 0x09;

    /// <summary>Windows virtual-key code: Escape.</summary>
    public const int VkEscape = 0x1B;

    /// <summary>Windows virtual-key code: left Windows key.</summary>
    public const int VkLeftWin = 0x5B;

    /// <summary>Windows virtual-key code: right Windows key.</summary>
    public const int VkRightWin = 0x5C;

    /// <summary>
    /// True when a key press must not reach the PC. The Windows keys are always blocked. Tab is blocked with Alt
    /// (switch window). Escape is blocked with Alt (switch window) or Ctrl (Start menu).
    /// Ctrl+Alt+Del cannot be blocked by any program.
    /// </summary>
    public static bool ShouldBlock(int virtualKey, bool altDown, bool ctrlDown) => virtualKey switch
    {
        VkLeftWin or VkRightWin => true,
        VkTab => altDown,
        VkEscape => altDown || ctrlDown,
        _ => false,
    };

    /// <summary>
    /// True when a key press is the control shortcut and the shortcut contains the Windows key. While control mode runs the
    /// Windows key is blocked, so Windows itself never sees such a shortcut. The keyboard hook must find it, and the other
    /// modifiers must match exactly.
    /// </summary>
    public static bool IsWinHotkey(HotkeyCombo hotkey, int virtualKey, bool ctrlDown, bool altDown, bool shiftDown, bool winDown)
    {
        if (hotkey == null || !hotkey.Modifiers.HasFlag(HotkeyModifiers.Win) || virtualKey != hotkey.VirtualKey)
        {
            return false;
        }

        return winDown
            && ctrlDown == hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl)
            && altDown == hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)
            && shiftDown == hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
    }
}
