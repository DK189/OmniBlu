#if WINDOWS
using System.Runtime.InteropServices;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>
/// Windows implementation of <see cref="IRawMouse"/> with Raw Input. It runs its own thread with a hidden message-only
/// window that receives <c>WM_INPUT</c>. A separate thread means a slow user interface cannot delay or batch the
/// mouse movement.
/// </summary>
internal sealed class WindowsRawMouse : IRawMouse
{
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageMouse = 0x02;
    private static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    private readonly object sync = new();
    private Thread thread;
    private IntPtr window;
    private Action<int, int> onMove;
    private WndProc wndProc; // must stay referenced: native code calls it

    public bool IsSupported => true;

    public string LastError { get; private set; }

    public bool Start(Action<int, int> onMove)
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

            LastError = "Raw input did not start in time.";
            this.onMove = onMove;
            starting = thread = new Thread(() => Run(ready, ok => started = ok))
            {
                IsBackground = true,
                Name = "OmniBlu raw mouse",
            };
        }

        starting.Start();

        // Wait outside the lock: the new thread takes the same lock to publish its window.
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
        IntPtr handle;
        lock (sync)
        {
            running = thread;
            handle = window;
            thread = null;
            window = IntPtr.Zero;
            onMove = null;
        }

        if (running == null)
        {
            return;
        }

        if (handle != IntPtr.Zero)
        {
            PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        running.Join(1000);
    }

    private void Run(ManualResetEventSlim ready, Action<bool> result)
    {
        var className = "OmniBluRawMouse" + Guid.NewGuid().ToString("N");
        var instance = IntPtr.Zero;
        var classRegistered = false;
        var signalled = false;

        void Fail(string step)
        {
            // The Windows error code is read first: later calls could overwrite it.
            LastError = $"{step} failed (Windows error {Marshal.GetLastWin32Error()}).";
            result(false);
            ready.Set();
            signalled = true;
        }

        try
        {
            instance = GetModuleHandle(null);
            wndProc = WindowProc;

            var windowClass = new WndClassEx
            {
                cbSize = Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = wndProc,
                hInstance = instance,
                lpszClassName = className,
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                Fail("Registering the window class");
                return;
            }

            classRegistered = true;

            var hwnd = CreateWindowEx(0, className, "OmniBlu raw mouse", 0, 0, 0, 0, 0, MessageOnlyParent, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                Fail("Creating the window");
                return;
            }

            var device = new RawInputDevice { UsagePage = UsagePageGeneric, Usage = UsageMouse, Flags = RidevInputSink, Target = hwnd };
            if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                Fail("Registering for raw input");
                DestroyWindow(hwnd);
                return;
            }

            lock (sync)
            {
                window = hwnd;
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

            var remove = new RawInputDevice { UsagePage = UsagePageGeneric, Usage = UsageMouse, Flags = RidevRemove, Target = IntPtr.Zero };
            RegisterRawInputDevices(new[] { remove }, 1, (uint)Marshal.SizeOf<RawInputDevice>());
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
            if (classRegistered)
            {
                UnregisterClass(className, instance);
            }
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmInput:
                ReadInput(lParam);
                break;

            case WmClose:
                DestroyWindow(hwnd);
                return IntPtr.Zero;

            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ReadInput(IntPtr inputHandle)
    {
        var callback = onMove;
        if (callback == null)
        {
            return;
        }

        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        if (GetRawInputData(inputHandle, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(inputHandle, RidInput, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeMouse)
            {
                return;
            }

            var mouse = Marshal.PtrToStructure<RawMouseData>(buffer + (int)headerSize);
            if (RawMouse.TryGetRelative(mouse.Flags, mouse.LastX, mouse.LastY, out var dx, out var dy))
            {
                callback(dx, dy);
            }
        }
        catch
        {
            // One bad event must not stop the message loop. The next event is read normally.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouseData
    {
        public ushort Flags;
        public uint Buttons;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string moduleName);
}
#endif
