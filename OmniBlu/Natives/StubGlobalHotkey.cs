#if LINUX
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. Global shortcuts are not available.</summary>
internal sealed class StubGlobalHotkey : IGlobalHotkey
{
    public bool IsSupported => false;

    public string LastError => "Global shortcuts are not available on this platform.";

    public event EventHandler Pressed
    {
        add { }
        remove { }
    }

    public bool Register(HotkeyCombo combo) => false;

    public void Unregister()
    {
    }
}
#endif
