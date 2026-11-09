#if WINDOWS
using System.Runtime.InteropServices;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Windows implementation of <see cref="ICursorLock"/> with the user32 cursor functions.</summary>
internal sealed class WindowsCursorLock : ICursorLock
{
    public WindowsCursorLock()
    {
        // The cursor must never stay trapped. Windows frees it when the process ends, but make it explicit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Unclip();
    }

    public bool IsSupported => true;

    public void Clip(int left, int top, int right, int bottom)
    {
        var rect = new Rect { Left = left, Top = top, Right = right, Bottom = bottom };
        ClipCursor(ref rect);
    }

    public void Unclip() => ClipCursor(IntPtr.Zero);

    public void SetPosition(int x, int y) => SetCursorPos(x, y);

    public bool TryGetPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref Rect rect);

    [DllImport("user32.dll", EntryPoint = "ClipCursor")]
    private static extern bool ClipCursor(IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
}
#endif
