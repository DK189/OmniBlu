namespace OmniBlu.Managers;

/// <summary>
/// Keeps some keys away from the operating system while control mode runs (see <see cref="KeyBlockRules"/>) and reports
/// them, so the app can send them to the phone.
/// </summary>
public interface IKeyboardBlocker
{
    /// <summary>False on platforms where blocking keys is not implemented. The keys then stay with the PC.</summary>
    bool IsSupported { get; }

    /// <summary>Why the last <see cref="Start"/> failed, for the log. Null when it did not fail.</summary>
    string LastError { get; }

    /// <summary>
    /// Starts blocking. <paramref name="onKey"/> is called on a background thread with the Windows virtual-key code and
    /// whether the key went down (true) or up (false), for every key that was blocked. Returns false when it could not start.
    /// If it is already running, nothing changes and it returns true.
    /// <para>
    /// <paramref name="hotkey"/> is the control shortcut. When it contains the Windows key, the blocker finds it itself (Windows
    /// cannot, because the Windows key is blocked) and calls <paramref name="onHotkey"/> on a background thread. The keys of
    /// that shortcut are not reported to <paramref name="onKey"/>. Other shortcuts are left to the system.
    /// </para>
    /// </summary>
    bool Start(Action<int, bool> onKey, HotkeyCombo hotkey, Action onHotkey);

    /// <summary>Stops blocking. Safe to call at any time, even when not started.</summary>
    void Stop();
}
