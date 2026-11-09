#if WINDOWS
using System.Runtime.InteropServices;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>
/// Windows implementation of <see cref="IGlobalHotkey"/> with <c>RegisterHotKey</c>. The shortcut belongs to a thread of
/// its own, because Windows sends the "pressed" message to the thread that registered it. Windows takes the key press
/// away from every other program, so the shortcut is not sent to the window that has the focus.
/// </summary>
internal sealed class WindowsGlobalHotkey : IGlobalHotkey, IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;
    private const uint PmNoRemove = 0;
    private const int HotkeyId = 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly object sync = new();
    private Thread thread;
    private uint threadId;

    public bool IsSupported => true;

    public string LastError { get; private set; }

    public event EventHandler Pressed;

    public bool Register(HotkeyCombo combo)
    {
        Unregister();

        var ready = new ManualResetEventSlim(false);
        var registered = false;
        Thread starting;

        lock (sync)
        {
            LastError = "The shortcut did not start in time.";
            starting = thread = new Thread(() => Run(combo, ready, ok => registered = ok))
            {
                IsBackground = true,
                Name = "OmniBlu shortcut",
            };
        }

        starting.Start();

        // Wait outside the lock: the new thread takes the same lock to publish its id.
        if (ready.Wait(2000) && registered)
        {
            return true;
        }

        Unregister();
        return false;
    }

    public void Unregister()
    {
        Thread running;
        uint id;
        lock (sync)
        {
            running = thread;
            id = threadId;
            thread = null;
            threadId = 0;
        }

        if (running == null)
        {
            return;
        }

        if (id != 0)
        {
            PostThreadMessage(id, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        running.Join(1000);
    }

    public void Dispose() => Unregister();

    private void Run(HotkeyCombo combo, ManualResetEventSlim ready, Action<bool> result)
    {
        var signalled = false;
        try
        {
            // Peeking creates the message queue of this thread, so a quit message sent early is not lost.
            PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            lock (sync)
            {
                threadId = GetCurrentThreadId();
            }

            var modifiers = (uint)combo.Modifiers | ModNoRepeat;
            if (!RegisterHotKey(IntPtr.Zero, HotkeyId, modifiers, (uint)combo.VirtualKey))
            {
                // The Windows error code is read first: later calls could overwrite it.
                var error = Marshal.GetLastWin32Error();
                LastError = error == ErrorHotkeyAlreadyRegistered
                    ? "Another program already uses this shortcut."
                    : $"Windows could not register this shortcut (error {error}).";
                result(false);
                ready.Set();
                signalled = true;
                return;
            }

            LastError = null;
            result(true);
            ready.Set();
            signalled = true;

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Id == WmHotkey)
                {
                    RaisePressed();
                }
            }

            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
        catch (Exception ex)
        {
            if (!signalled)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                result(false);
                ready.Set();
            }
        }
    }

    private void RaisePressed()
    {
        try
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A failing listener must not stop the message loop. The next press is delivered normally.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
#endif
