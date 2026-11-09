namespace OmniBlu.Managers;

/// <summary>
/// A keyboard shortcut that works in the whole system, also when this app is not the active window. Only one shortcut
/// is registered at a time.
/// </summary>
public interface IGlobalHotkey
{
    /// <summary>False on platforms where global shortcuts are not implemented.</summary>
    bool IsSupported { get; }

    /// <summary>Why the last <see cref="Register"/> failed, for the user. Null when it did not fail.</summary>
    string LastError { get; }

    /// <summary>Raised on a background thread each time the shortcut is pressed.</summary>
    event EventHandler Pressed;

    /// <summary>
    /// Registers the shortcut and replaces the one registered before. Returns false when it cannot be used, for
    /// example when another program already owns it.
    /// </summary>
    bool Register(HotkeyCombo combo);

    /// <summary>Removes the shortcut. Safe to call at any time, even when none is registered.</summary>
    void Unregister();
}
