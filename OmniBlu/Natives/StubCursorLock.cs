#if LINUX
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. The cursor is not locked, so control mode has no mouse.</summary>
internal sealed class StubCursorLock : ICursorLock
{
    public bool IsSupported => false;

    public void Clip(int left, int top, int right, int bottom)
    {
    }

    public void Unclip()
    {
    }

    public void SetPosition(int x, int y)
    {
    }

    public bool TryGetPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        return false;
    }
}
#endif
