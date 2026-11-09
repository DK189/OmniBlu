#if WINDOWS
using System.Runtime.InteropServices;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>
/// Windows implementation of <see cref="IKeyboardBlocker"/> with a low-level keyboard hook. The hook lives on a thread of
/// its own with a message loop. The hook function must return fast (Windows removes slow hooks), so it only decides and
/// reports; the app does the real work on another thread.
/// </summary>
internal sealed class WindowsKeyboardBlocker : IKeyboardBlocker, IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0;
    private const uint LlkhfAltDown = 0x20;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;

    private readonly object sync = new();
    private readonly HashSet<int> blocked = new(); // used by the hook thread only
    private Thread thread;
    private uint threadId;
    private readonly HashSet<int> hotkeyKeys = new(); // keys of the shortcut that were swallowed; hook thread only
    private Action<int, bool> onKey;
    private HotkeyCombo hotkey;
    private Action onHotkey;
    private HookProc hookProc; // must stay referenced: native code calls it

    public bool IsSupported => true;

    public string LastError { get; private set; }

    public bool Start(Action<int, bool> onKey, HotkeyCombo hotkey, Action onHotkey)
    {
        var ready = new ManualResetEventSlim(false);
        var started = false;
        Thread starting;

        lock (sync)
        {
            if (thread != null)
            {
                return true;
            }

            LastError = "Blocking keys did not start in time.";
            this.onKey = onKey;
            this.hotkey = hotkey;
            this.onHotkey = onHotkey;
            starting = thread = new Thread(() => Run(ready, ok => started = ok))
            {
                IsBackground = true,
                Name = "OmniBlu key blocker",
            };
        }

        starting.Start();

        // Wait outside the lock: the new thread takes the same lock to publish its id.
        if (ready.Wait(2000) && started)
        {
            return true;
        }

        Stop();
        return false;
    }

    public void Stop()
    {
        Thread running;
        uint id;
        lock (sync)
        {
            running = thread;
            id = threadId;
            thread = null;
            threadId = 0;
            onKey = null;
            hotkey = null;
            onHotkey = null;
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

    public void Dispose() => Stop();

    private void Run(ManualResetEventSlim ready, Action<bool> result)
    {
        var hook = IntPtr.Zero;
        var signalled = false;
        try
        {
            // Peeking creates the message queue of this thread, so a quit message sent early is not lost.
            PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            lock (sync)
            {
                threadId = GetCurrentThreadId();
            }

            blocked.Clear();
            hotkeyKeys.Clear();
            hookProc = HookFunction;
            hook = SetWindowsHookEx(WhKeyboardLl, hookProc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                // The Windows error code is read first: later calls could overwrite it.
                LastError = $"Installing the keyboard hook failed (Windows error {Marshal.GetLastWin32Error()}).";
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
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
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
        finally
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
            }
        }
    }

    private IntPtr HookFunction(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var message = (uint)wParam;
                var down = message is WmKeyDown or WmSysKeyDown;
                var up = message is WmKeyUp or WmSysKeyUp;
                if (down || up)
                {
                    var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
                    var key = (int)data.VirtualKey;

                    // The control shortcut with the Windows key: Windows cannot see it (the Windows key is blocked), so it is
                    // found here. The key is swallowed and not sent to the phone. Holding it down does not repeat the action.
                    if (down && IsWinHotkey(key, (data.Flags & LlkhfAltDown) != 0))
                    {
                        if (hotkeyKeys.Add(key))
                        {
                            onHotkey?.Invoke();
                        }

                        return (IntPtr)1;
                    }

                    if (up && hotkeyKeys.Remove(key))
                    {
                        return (IntPtr)1;
                    }

                    // A key-up is blocked only when its key-down was blocked. Otherwise the PC would miss a key-up
                    // and think the key is still held (for example a Windows key held while control started).
                    var block = down
                        ? KeyBlockRules.ShouldBlock(key, (data.Flags & LlkhfAltDown) != 0, (GetAsyncKeyState(VkControl) & 0x8000) != 0)
                        : blocked.Remove(key);
                    if (block)
                    {
                        if (down)
                        {
                            blocked.Add(key);
                        }

                        onKey?.Invoke(key, down);
                        return (IntPtr)1;
                    }
                }
            }
            catch
            {
                // An error here must never break the keyboard of the PC. The key goes through as usual.
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private bool IsWinHotkey(int key, bool altFlag)
    {
        var combo = hotkey;
        if (combo == null)
        {
            return false;
        }

        // The Windows key was swallowed by this hook, so its state is the list of swallowed keys, not the system state.
        var winDown = blocked.Contains(KeyBlockRules.VkLeftWin) || blocked.Contains(KeyBlockRules.VkRightWin);
        return KeyBlockRules.IsWinHotkey(
            combo,
            key,
            (GetAsyncKeyState(VkControl) & 0x8000) != 0,
            altFlag || (GetAsyncKeyState(VkMenu) & 0x8000) != 0,
            (GetAsyncKeyState(VkShift) & 0x8000) != 0,
            winDown);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string moduleName);
}
#endif
