using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using OmniBlu.Managers;
using OmniBlu.ViewModels;

namespace OmniBlu.UI;

public partial class MainWindow : Window
{
    /// <summary>Drag payload: "{source row}:{device key}".</summary>
    private static readonly DataFormat<string> PayloadFormat = DataFormat.CreateStringApplicationFormat("omniblu-device");

    /// <summary>The pointer must move this far (in pixels) before a press becomes a drag.</summary>
    private const double DragThreshold = 6;

    private PointerPressedEventArgs pressArgs;
    private Point pressPoint;
    private Border pressCard;

    private Button iconButton;

    // Control mode: the keyboard and mouse of this PC go to a phone.
    private ICursorLock cursorLock;
    private IRawMouse rawMouse;
    private SettingsManager settings;
    private IGlobalHotkey globalHotkey;
    private IKeyboardBlocker keyboardBlocker;
    private ControlOverlayWindow overlay;
    private Window controlHost; // the window that takes the keys and holds the cursor: this window, or the overlay
    private volatile HidControlSession controlSession;
    private bool usingRawMouse;
    private RelativeCursor relativeCursor;
    private double wheelRemainder;

    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            return;
        }

        DataContext = Program.HostApp.Services.GetRequiredService<MainViewModel>();
        cursorLock = Program.HostApp.Services.GetRequiredService<ICursorLock>();
        rawMouse = Program.HostApp.Services.GetRequiredService<IRawMouse>();
        settings = Program.HostApp.Services.GetRequiredService<SettingsManager>();
        globalHotkey = Program.HostApp.Services.GetRequiredService<IGlobalHotkey>();
        keyboardBlocker = Program.HostApp.Services.GetRequiredService<IKeyboardBlocker>();

        // The small panel in the corner takes over control mode when this window is hidden.
        overlay = new ControlOverlayWindow { DataContext = ViewModel };
        AttachControlHandlers(this);
        AttachControlHandlers(overlay);

        // The Settings panel closes when the user clicks anywhere outside it (the Settings button itself toggles it).
        AddHandler(PointerPressedEvent, SettingsOutsidePressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Safety: control mode must end whenever the window that hosts it stops being the active one, and when it closes.
        Deactivated += (_, _) =>
        {
            ViewModel.IsSettingsOpen = false;
            ViewModel.IsCapturingHotkey = false;
            if (controlHost == this)
            {
                EndControl("The window lost focus.");
            }
        };
        overlay.Deactivated += (_, _) =>
        {
            if (controlHost == overlay)
            {
                EndControl("The control panel lost focus.");
            }
        };
        Closing += (_, _) => EndControl("The window was closed.");

        // The control shortcut works in the whole system. It is off while the user chooses a new one.
        globalHotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(() => _ = HandleHotkeyAsync());
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Hotkey) or nameof(MainViewModel.IsCapturingHotkey))
            {
                ApplyHotkey();
            }
        };
        Opened += (_, _) => ApplyHotkey();
        Closed += (_, _) => globalHotkey.Unregister();

        foreach (var zone in new[] { AvailableZone, AudioZone, ControlZone })
        {
            DragDrop.AddDragOverHandler(zone, Zone_DragOver);
            DragDrop.AddDragLeaveHandler(zone, Zone_DragLeave);
            DragDrop.AddDropHandler(zone, Zone_Drop);
        }
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    /// <summary>
    /// Lets a window see every key and mouse event first (tunnel + handledEventsToo) while control mode runs. The same
    /// handlers serve the main window and the small panel, so control works from whichever one hosts it.
    /// </summary>
    private void AttachControlHandlers(Window window)
    {
        window.AddHandler(KeyDownEvent, ControlKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(KeyUpEvent, ControlKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerMovedEvent, ControlPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerPressedEvent, ControlPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerReleasedEvent, ControlPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerWheelChangedEvent, ControlWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>True when this window is on the screen and not minimized.</summary>
    private bool IsShown => IsVisible && WindowState != WindowState.Minimized;

    /// <summary>
    /// Used when OmniBlu starts in the tray: the window is never shown, so the "window opened" step does not run. This
    /// does the part that must not wait for it (the control shortcut).
    /// </summary>
    public void PrepareHidden()
    {
        ViewModel.AddLog("Started in the tray, with no window. Click the tray icon or use the control shortcut to show it.");
        ApplyHotkey();
    }

    /// <summary>Brings this window back from the tray or from the taskbar. Used when the tray icon is clicked.</summary>
    public void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private static DeviceViewModel DeviceOf(object sender) => (sender as StyledElement)?.DataContext as DeviceViewModel;

    // ---------------------------------------------------------------- buttons

    private async void Connect_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(sender, "Connect", ViewModel.ToggleConnectionAsync);

    private async void RemoveAudio_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(sender, "Close audio", ViewModel.UnassignAudioAsync);

    private async void RemoveControl_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(sender, "Stop control", ViewModel.UnassignControlAsync);

    /// <summary>Runs an action for the device of a button. An exception must never escape an async void handler.</summary>
    private async Task RunAsync(object sender, string what, Func<DeviceViewModel, Task> action)
    {
        var device = DeviceOf(sender);
        if (device == null)
        {
            return;
        }

        try
        {
            await action(device);
        }
        catch (Exception ex)
        {
            ViewModel.AddLog($"{what} failed: {ex.Message}");
        }
    }

    private void StartControl_Click(object sender, RoutedEventArgs e)
    {
        var device = DeviceOf(sender);
        if (device != null)
        {
            StartControl(device, this);
        }
    }

    private void Icon_Click(object sender, RoutedEventArgs e) => iconButton = sender as Button;

    // ---------------------------------------------------------------- title bar

    /// <summary>Drag the title bar to move the window.</summary>
    private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Hides the window. It is not in the task bar then; the tray icon or the control shortcut brings it back.</summary>
    private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    private void IconPick_Click(object sender, RoutedEventArgs e)
    {
        var device = DeviceOf(iconButton);
        if (device != null && sender is Button { Content: string icon })
        {
            device.Icon = icon;
        }

        iconButton?.Flyout?.Hide();
    }

    private void SettingsOutsidePressed(object sender, PointerPressedEventArgs e)
    {
        if (!ViewModel.IsSettingsOpen || e.Source is not Visual source)
        {
            return;
        }

        if (!SettingsPanel.IsVisualAncestorOf(source) && !SettingsToggle.IsVisualAncestorOf(source))
        {
            ViewModel.IsSettingsOpen = false;
        }
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => ViewModel.IsSettingsOpen = false;

    private void ResetSettings_Click(object sender, RoutedEventArgs e) => ViewModel.ResetSettings();

    /// <summary>Mouse wheel scrolls the rows sideways (a plain wheel only scrolls vertically by default).</summary>
    private void HorizontalScroll_Wheel(object sender, PointerWheelEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            viewer.Offset = new Vector(Math.Max(0, viewer.Offset.X - e.Delta.Y * 80), viewer.Offset.Y);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------- control shortcut

    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.HotkeyStatus = string.Empty;
        ViewModel.IsCapturingHotkey = true;
    }

    /// <summary>The user pressed keys while the Settings panel waits for a new shortcut.</summary>
    private void CaptureHotkey(KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            ViewModel.IsCapturingHotkey = false;
        }
        else if (HotkeyCapture.IsModifierKey(e.Key))
        {
            // Only a modifier so far. Wait for the key that goes with it.
        }
        else if (HotkeyCapture.TryFrom(e.Key, e.KeyModifiers, out var combo))
        {
            ViewModel.Hotkey = combo;
            ViewModel.IsCapturingHotkey = false;
        }
        else
        {
            ViewModel.HotkeyStatus = "Hold Ctrl, Alt, Shift or Win and press a letter, digit, F1 to F12, Space, the ` key or a navigation key.";
        }
    }

    /// <summary>Registers the shortcut with the system, or removes it while the user is choosing a new one.</summary>
    private void ApplyHotkey()
    {
        if (!globalHotkey.IsSupported)
        {
            ViewModel.HotkeyStatus = "A shortcut for the whole system is not available on this platform.";
            return;
        }

        if (ViewModel.IsCapturingHotkey)
        {
            globalHotkey.Unregister();
            return;
        }

        if (globalHotkey.Register(ViewModel.Hotkey))
        {
            ViewModel.HotkeyStatus = string.Empty;
            ViewModel.AddLog($"Control shortcut {ViewModel.Hotkey} is ready.");
        }
        else
        {
            var message = $"The shortcut {ViewModel.Hotkey} does not work. {globalHotkey.LastError} Choose another one in Settings.";
            ViewModel.HotkeyStatus = message;
            ViewModel.AddLog(message);
        }
    }

    /// <summary>
    /// The control shortcut was pressed. Control running: switch to the next connected device. Not running: control the
    /// device used last (or the first connected one). The main window hosts control when it is shown; when it is hidden
    /// or minimized, only the small panel in the corner appears. No device connected: show the main window.
    /// </summary>
    private async Task HandleHotkeyAsync()
    {
        try
        {
            var vm = ViewModel;
            var connected = vm.ControlDevices.Where(d => d.IsHidActive).ToList();
            var targetKey = ControlCycler.Next(connected.Select(d => d.Key).ToList(), vm.ControllingDevice?.Key, vm.LastControlledKey);

            if (targetKey == null)
            {
                if (controlSession != null)
                {
                    vm.AddLog("Shortcut: no other device is connected, so there is nothing to switch to. Press Right Ctrl to stop.");
                    return;
                }

                await ActivateAsync(this);
                vm.AddLog("Shortcut: no device is connected over HID. The window is shown.");
                return;
            }

            var target = connected.First(d => d.Key == targetKey);
            if (controlSession != null)
            {
                // Keep the same window. If the small panel were hidden for a moment, its "lost focus" event could arrive
                // after the new control starts and end it.
                var host = controlHost ?? this;
                EndControl($"Switched to {target.Name} with the shortcut.", keepPanel: true);
                StartControl(target, host);
                return;
            }

            Window newHost = IsShown ? this : overlay;
            if (!await ActivateAsync(newHost))
            {
                vm.AddLog("Shortcut: Windows did not let the window come to the front, so control did not start.");
                if (newHost == overlay)
                {
                    overlay.Hide();
                }

                return;
            }

            StartControl(target, newHost);
        }
        catch (Exception ex)
        {
            ViewModel.AddLog($"Shortcut failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a window and makes it the active one. Control mode needs the keyboard focus, so it must not start before
    /// the window is really active. Returns false when Windows did not allow it.
    /// </summary>
    private static async Task<bool> ActivateAsync(Window window)
    {
        if (window is ControlOverlayWindow panel)
        {
            panel.PlaceAtCorner();
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        for (var i = 0; i < 30 && !window.IsActive; i++)
        {
            await Task.Delay(50);
        }

        return window.IsActive;
    }

    // ------------------------------------------------------------- control mode

    /// <summary>
    /// Hides and locks the cursor, then sends every key and mouse movement to the phone until the exit key is pressed.
    /// The window <paramref name="host"/> (the main window or the small panel) must be active: it takes the keys and the
    /// cursor is locked inside it. Ctrl+Alt+Del still belongs to this PC and takes the focus away, which ends control mode.
    /// </summary>
    private void StartControl(DeviceViewModel device, Window host)
    {
        if (controlSession != null)
        {
            return;
        }

        // Raw input first: the movement as the mouse reports it, with no Windows pointer acceleration (like a real
        // mouse on the phone). If it cannot start, the position of the cursor is used instead.
        var useRaw = false;
        if (settings.UseRawMouse && rawMouse.IsSupported)
        {
            useRaw = rawMouse.Start((dx, dy) => controlSession?.MouseMove(dx, dy));
            if (!useRaw)
            {
                ViewModel.AddLog($"Raw mouse input could not start ({rawMouse.LastError}). Using the cursor position instead.");
            }
        }

        var session = ViewModel.BeginControl(device, useRaw);
        if (session == null)
        {
            rawMouse.Stop();
            keyboardBlocker.Stop(); // it may still run after a switch to another device
            if (host == overlay)
            {
                overlay.Hide();
            }

            return;
        }

        // Keep the Windows key, Alt+Tab and similar keys away from this PC. They go to the phone instead. If this
        // cannot start, those keys still work on the PC and take the focus away, which ends control mode.
        if (keyboardBlocker.IsSupported && !keyboardBlocker.Start(BlockedKey, ViewModel.Hotkey, BlockerHotkey))
        {
            ViewModel.AddLog($"The Windows key could not be blocked ({keyboardBlocker.LastError}). It stays with this PC.");
        }

        session.Faulted += (_, _) => Dispatcher.UIThread.Post(() => EndControl("The phone stopped accepting input."));
        usingRawMouse = useRaw;
        controlSession = session;
        controlHost = host;
        wheelRemainder = 0;

        host.Cursor = new Cursor(StandardCursorType.None);
        host.Focus();
        LockCursor(host);
    }

    /// <summary>
    /// The keyboard hook found the control shortcut while control runs (only for shortcuts with the Windows key, which Windows
    /// cannot see then). It does the same as the system shortcut.
    /// </summary>
    private void BlockerHotkey() => Dispatcher.UIThread.Post(() => _ = HandleHotkeyAsync());

    /// <summary>
    /// Ends control mode. <paramref name="keepPanel"/> keeps the small panel on screen, for a switch to another device.
    /// The same switch keeps the key blocker running, so the Windows key can stay held to switch again.
    /// </summary>
    private void EndControl(string reason, bool keepPanel = false)
    {
        var session = controlSession;
        if (session == null)
        {
            return;
        }

        var host = controlHost;
        controlSession = null;
        controlHost = null;
        relativeCursor = null;
        usingRawMouse = false;
        rawMouse.Stop();
        if (!keepPanel)
        {
            keyboardBlocker.Stop();
        }

        cursorLock.Unclip();
        if (host != null)
        {
            host.Cursor = Cursor.Default;
        }

        if (host == overlay && !keepPanel)
        {
            overlay.Hide();
        }

        ViewModel.EndControl(reason);

        // Release every key and button on the phone. This must not throw into the UI.
        _ = StopSessionAsync(session);
    }

    private async Task StopSessionAsync(HidControlSession session)
    {
        try
        {
            await session.StopAsync();

            // How the mouse behaved: this tells "the link stalled" from "the mouse moved too fast" when it felt wrong.
            ViewModel.AddLog(session.GetSummary());
        }
        catch (Exception ex)
        {
            ViewModel.AddLog($"Could not release the keys on the phone: {ex.Message}");
        }
    }

    /// <summary>Keeps the cursor inside the host window and puts it in the middle.</summary>
    private void LockCursor(Window host)
    {
        // The centre is a whole screen pixel, computed once.
        var center = host.PointToScreen(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2));

        if (usingRawMouse)
        {
            // The movement comes from raw input, so the cursor is not needed to measure it. Freeze it on one pixel.
            cursorLock.Clip(center.X, center.Y, center.X + 1, center.Y + 1);
            cursorLock.SetPosition(center.X, center.Y);
            return;
        }

        var topLeft = host.PointToScreen(new Point(0, 0));
        var bottomRight = host.PointToScreen(new Point(host.Bounds.Width, host.Bounds.Height));
        cursorLock.Clip(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);

        // The cursor is always put back on exactly this pixel.
        relativeCursor = new RelativeCursor(cursorLock, center.X, center.Y);
        relativeCursor.Center();
    }

    private void ControlKeyDown(object sender, KeyEventArgs e)
    {
        if (controlSession == null)
        {
            if (ViewModel.IsCapturingHotkey)
            {
                CaptureHotkey(e);
            }

            return;
        }

        e.Handled = true;
        if (e.Key == KeyMapper.ExitKey)
        {
            EndControl("Right Ctrl was pressed.");
        }
        else
        {
            ForwardKey(e.Key, down: true);
        }
    }

    private void ControlKeyUp(object sender, KeyEventArgs e)
    {
        if (controlSession == null)
        {
            return;
        }

        e.Handled = true;
        ForwardKey(e.Key, down: false);
    }

    /// <summary>Sends one key press or release to the phone.</summary>
    private void ForwardKey(Key key, bool down)
    {
        var session = controlSession;
        if (session == null)
        {
            return;
        }

        if (KeyMapper.TryGetModifier(key, out var bit))
        {
            if (down)
            {
                session.ModifierDown(bit);
            }
            else
            {
                session.ModifierUp(bit);
            }
        }
        else if (KeyMapper.TryGetUsage(key, out var usage))
        {
            if (down)
            {
                session.KeyDown(usage);
            }
            else
            {
                session.KeyUp(usage);
            }
        }
    }

    /// <summary>
    /// A key that the PC must not see (Windows key, Alt+Tab...) was caught. It is sent to the phone on the UI thread,
    /// in the same order as the other keys.
    /// </summary>
    private void BlockedKey(int virtualKey, bool down)
    {
        if (KeyMapper.TryGetKeyFromVirtualKey(virtualKey, out var key))
        {
            Dispatcher.UIThread.Post(() => ForwardKey(key, down));
        }
    }

    private void ControlPointerMoved(object sender, PointerEventArgs e)
    {
        if (controlSession == null)
        {
            return;
        }

        e.Handled = true;

        // With raw input the movement already arrives on its own thread. Nothing to read here.
        if (usingRawMouse)
        {
            return;
        }

        // Fallback: the movement is read from the cursor itself, not from this event (see RelativeCursor). The event
        // only tells us that the mouse moved. Events that were queued before the cursor was centred read as no movement.
        if (relativeCursor != null && relativeCursor.TryRead(out var dx, out var dy))
        {
            controlSession.MouseMove(dx, dy);
        }
    }

    private void ControlPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (controlSession == null)
        {
            return;
        }

        e.Handled = true;
        ForwardButton(e.GetCurrentPoint((Visual)sender).Properties.PointerUpdateKind, down: true);
    }

    private void ControlPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (controlSession == null)
        {
            return;
        }

        e.Handled = true;
        ForwardButton(e.InitialPressMouseButton, down: false);
    }

    private void ForwardButton(PointerUpdateKind kind, bool down)
    {
        var mask = kind switch
        {
            PointerUpdateKind.LeftButtonPressed => MouseAccumulator.LeftButton,
            PointerUpdateKind.RightButtonPressed => MouseAccumulator.RightButton,
            PointerUpdateKind.MiddleButtonPressed => MouseAccumulator.MiddleButton,
            _ => (byte)0,
        };

        if (mask != 0)
        {
            controlSession.MouseButton(mask, down);
        }
    }

    private void ForwardButton(MouseButton button, bool down)
    {
        var mask = button switch
        {
            MouseButton.Left => MouseAccumulator.LeftButton,
            MouseButton.Right => MouseAccumulator.RightButton,
            MouseButton.Middle => MouseAccumulator.MiddleButton,
            _ => (byte)0,
        };

        if (mask != 0)
        {
            controlSession.MouseButton(mask, down);
        }
    }

    private void ControlWheel(object sender, PointerWheelEventArgs e)
    {
        if (controlSession == null)
        {
            return;
        }

        e.Handled = true;

        // Touchpads give fractions of a notch. Keep the remainder so slow scrolling is not lost.
        wheelRemainder += e.Delta.Y;
        var notches = (int)Math.Truncate(wheelRemainder);
        wheelRemainder -= notches;
        if (notches != 0)
        {
            controlSession.Scroll(notches);
        }
    }

    // --------------------------------------------------------------- drag source

    private void Card_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        var card = (Border)sender;
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed || IsInteractiveSource(e.Source, card))
        {
            return;
        }

        pressArgs = e;
        pressPoint = e.GetPosition(this);
        pressCard = card;
    }

    private void Card_PointerReleased(object sender, PointerReleasedEventArgs e) => ResetPress();

    private async void Card_PointerMoved(object sender, PointerEventArgs e)
    {
        if (pressArgs == null || !ReferenceEquals(sender, pressCard))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetPress();
            return;
        }

        var delta = e.GetPosition(this) - pressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        var args = pressArgs;
        var card = pressCard;
        ResetPress();

        var device = DeviceOf(card);
        var source = Enum.Parse<RowKind>((string)card.Tag);
        if (device == null)
        {
            return;
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(PayloadFormat, $"{source}:{device.Key}"));

        card.Opacity = 0.45;
        try
        {
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            ViewModel.AddLog($"Drag failed: {ex.Message}");
        }
        finally
        {
            card.Opacity = 1;
        }
    }

    private void ResetPress()
    {
        pressArgs = null;
        pressCard = null;
    }

    /// <summary>True when the press started on a control that needs its own mouse handling.</summary>
    private static bool IsInteractiveSource(object source, Control card)
    {
        for (var visual = source as Visual; visual != null && !ReferenceEquals(visual, card); visual = visual.GetVisualParent())
        {
            if (visual is Button or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    // --------------------------------------------------------------- drop target

    private void Zone_DragOver(object sender, DragEventArgs e)
    {
        var zone = (Border)sender;
        var target = Enum.Parse<RowKind>((string)zone.Tag);

        var verdict = TryReadPayload(e, out var source, out var device)
            ? ViewModel.Evaluate(target, source, device)
            : DropVerdict.Reject;

        e.DragEffects = verdict switch
        {
            DropVerdict.Add => DragDropEffects.Copy,
            DropVerdict.Reorder or DropVerdict.Remove => DragDropEffects.Move,
            _ => DragDropEffects.None,
        };

        zone.Classes.Set("accept", verdict != DropVerdict.Reject);
        zone.Classes.Set("reject", verdict == DropVerdict.Reject);

        AutoScroll(zone, e);

        if (target != RowKind.Available)
        {
            if (verdict is DropVerdict.Add or DropVerdict.Reorder)
            {
                ShowInsertLine(target, e.GetPosition(ListOf(target)));
            }
            else
            {
                InsertLineOf(target).IsVisible = false;
            }
        }

        e.Handled = true;
    }

    private void Zone_DragLeave(object sender, DragEventArgs e)
    {
        var zone = (Border)sender;
        zone.Classes.Set("accept", false);
        zone.Classes.Set("reject", false);
        AudioInsertLine.IsVisible = false;
        ControlInsertLine.IsVisible = false;
    }

    private async void Zone_Drop(object sender, DragEventArgs e)
    {
        try
        {
            var zone = (Border)sender;
            var target = Enum.Parse<RowKind>((string)zone.Tag);
            Zone_DragLeave(sender, e);

            if (!TryReadPayload(e, out var source, out var device))
            {
                return;
            }

            // Read everything from the event before the first await: the event is not valid after that.
            var verdict = ViewModel.Evaluate(target, source, device);
            var index = target == RowKind.Available ? 0 : InsertIndex(target, e.GetPosition(ListOf(target)));
            e.Handled = true;

            switch (verdict)
            {
                case DropVerdict.Add:
                case DropVerdict.Reorder:
                    if (target == RowKind.Audio)
                    {
                        await ViewModel.AssignAudioAsync(device, index);
                    }
                    else
                    {
                        await ViewModel.AssignControlAsync(device, index);
                    }

                    break;

                case DropVerdict.Remove:
                    // Dragging a card back to Available removes only the role of the row it came from.
                    if (source == RowKind.Audio)
                    {
                        await ViewModel.UnassignAudioAsync(device);
                    }
                    else
                    {
                        await ViewModel.UnassignControlAsync(device);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            ViewModel.AddLog($"Drop failed: {ex.Message}");
        }
    }

    private bool TryReadPayload(DragEventArgs e, out RowKind source, out DeviceViewModel device)
    {
        source = default;
        device = null;

        var text = e.DataTransfer.TryGetValue(PayloadFormat);
        var parts = text?.Split(':', 2);
        if (parts is not { Length: 2 } || !Enum.TryParse(parts[0], out source))
        {
            return false;
        }

        device = ViewModel.FindDevice(parts[1]);
        return device != null;
    }

    /// <summary>Scrolls the row while a card is dragged near its left or right edge.</summary>
    private static void AutoScroll(Border zone, DragEventArgs e)
    {
        const double edge = 56;
        const double step = 28;

        var viewer = zone.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer == null)
        {
            return;
        }

        var x = e.GetPosition(viewer).X;
        if (x < edge)
        {
            viewer.Offset = new Vector(Math.Max(0, viewer.Offset.X - step), viewer.Offset.Y);
        }
        else if (x > viewer.Bounds.Width - edge)
        {
            viewer.Offset = new Vector(viewer.Offset.X + step, viewer.Offset.Y);
        }
    }

    // ------------------------------------------------------------ insert marker

    private ItemsControl ListOf(RowKind kind) => kind == RowKind.Audio ? AudioList : ControlList;

    private Rectangle InsertLineOf(RowKind kind) => kind == RowKind.Audio ? AudioInsertLine : ControlInsertLine;

    private List<Control> CardsOf(RowKind kind) =>
        ListOf(kind).ItemsPanelRoot?.Children.OfType<Control>().ToList() ?? new List<Control>();

    /// <summary>Index in a row where a drop at <paramref name="point"/> (relative to the list) inserts the card.</summary>
    private int InsertIndex(RowKind kind, Point point)
    {
        var cards = CardsOf(kind);
        for (var i = 0; i < cards.Count; i++)
        {
            // Rows are a single horizontal line, so only the X position matters.
            if (point.X <= BoundsIn(cards[i], ListOf(kind)).Center.X)
            {
                return i;
            }
        }

        return cards.Count;
    }

    private void ShowInsertLine(RowKind kind, Point point)
    {
        var line = InsertLineOf(kind);
        var cards = CardsOf(kind);
        var index = InsertIndex(kind, point);

        double x = 0, y = 0, height = line.Height;
        if (cards.Count > 0)
        {
            var anchor = BoundsIn(cards[Math.Min(index, cards.Count - 1)], ListOf(kind));
            height = anchor.Height;
            y = anchor.Top;
            x = index < cards.Count ? anchor.Left - 6 : anchor.Right + 2;
        }

        line.Height = height;
        line.Margin = new Thickness(Math.Max(0, x), y, 0, 0);
        line.IsVisible = true;
    }

    private static Rect BoundsIn(Control child, Visual relativeTo)
    {
        var origin = child.TranslatePoint(default, relativeTo) ?? default;
        return new Rect(origin, child.Bounds.Size);
    }
}
