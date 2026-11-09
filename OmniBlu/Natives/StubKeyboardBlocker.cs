#if LINUX
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. Keys cannot be blocked, so they stay with the PC.</summary>
internal sealed class StubKeyboardBlocker : IKeyboardBlocker
{
    public bool IsSupported => false;

    public string LastError => "Blocking keys is not available on this platform.";

    public bool Start(Action<int, bool> onKey, HotkeyCombo hotkey, Action onHotkey) => false;

    public void Stop()
    {
    }
}
#endif
