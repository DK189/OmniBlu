#if LINUX
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. Raw input is not available, so the cursor position is used.</summary>
internal sealed class StubRawMouse : IRawMouse
{
    public bool IsSupported => false;

    public string LastError => "Raw input is not available on this platform.";

    public bool Start(Action<int, int> onMove) => false;

    public void Stop()
    {
    }
}
#endif
