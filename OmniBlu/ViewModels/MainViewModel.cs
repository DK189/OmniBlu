using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniBlu.Managers;
using OmniBlu.Models;

namespace OmniBlu.ViewModels;

public enum RowKind
{
    Available,
    Audio,
    Control,
}

public enum DropVerdict
{
    Reject,
    Add,
    Reorder,
    Remove,
}

/// <summary>
/// State of the main window. Row 1 (Available) lists devices and connects them (A2DP). Row 2 (Audio) holds the devices
/// whose audio stream is open. Row 3 (Controllable) holds the devices this PC can send keyboard and mouse input to (HID).
/// Rows 2 and 3 are independent: a device can be in both. Their order is the priority.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 200;
    private const string DefaultIcon = "📱";
    private const string DefaultUnsupportedIcon = "🔹";

    private readonly IBluetoothService bluetooth;
    private readonly IHidService hid;
    private readonly SettingsManager settings;
    private readonly FileLog fileLog;
    private readonly HashSet<string> hidOnlyKeys = new();
    private HidControlSession activeSession;
    private double mouseSpeed;
    private bool useRawMouse;
    private bool showAllDevices;
    private readonly IAutoStart autoStart;
    private StartupMode startupMode;
    private HotkeyCombo hotkey;
    private bool loading;

    public MainViewModel(IBluetoothService bluetooth, IHidService hid, SettingsManager settings, FileLog fileLog, IAutoStart autoStart)
    {
        this.autoStart = autoStart;
        startupMode = autoStart.IsSupported ? autoStart.Read() : StartupMode.Off;
        this.bluetooth = bluetooth;
        this.hid = hid;
        this.settings = settings;
        this.fileLog = fileLog;
        mouseSpeed = settings.MouseSpeed;
        useRawMouse = settings.UseRawMouse;
        showAllDevices = settings.ShowAllDevices;
        hotkey = settings.Hotkey;

        bluetooth.DevicesChanged += (_, _) => Dispatcher.UIThread.Post(() => SyncDevices(bluetooth.Devices));
        bluetooth.StateChanged += (_, e) => Dispatcher.UIThread.Post(() => ApplyState(e));
        bluetooth.LogMessage += (_, message) => Dispatcher.UIThread.Post(() => AddLog(message));

        hid.StateChanged += (_, e) => Dispatcher.UIThread.Post(() => ApplyHidState(e));
        hid.UnknownClientConnected += (_, e) => Dispatcher.UIThread.Post(() => AddHidOnlyDevice(e));
        hid.LogMessage += (_, message) => Dispatcher.UIThread.Post(() => AddLog(message));

        AddLog("OmniBlu started.");
        SyncDevices(bluetooth.Devices);
        var hidden = bluetooth.Devices.Count - Devices.Count;
        AddLog($"Found {bluetooth.Devices.Count} paired Bluetooth device(s), {bluetooth.Devices.Count(d => d.SupportsA2dp)} with A2DP."
            + (hidden > 0 ? $" {hidden} hidden (not a phone or computer). You can show them in Settings." : string.Empty));
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    /// <summary>Devices with an open audio stream. The order is the priority (index 0 = #1).</summary>
    public ObservableCollection<DeviceViewModel> AudioDevices { get; } = new();

    /// <summary>Devices this PC can send keyboard and mouse input to. The order is the priority (index 0 = #1).</summary>
    public ObservableCollection<DeviceViewModel> ControlDevices { get; } = new();

    public ObservableCollection<string> Log { get; } = new();

    /// <summary>The device whose input is being controlled from this PC (control mode), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlling), nameof(ControlBanner), nameof(ControlPositionText), nameof(HasSwitchHint))]
    private DeviceViewModel controllingDevice;

    /// <summary>Speed of the mouse in control mode. Saved, and applied at once if control mode is running.</summary>
    public double MouseSpeed
    {
        get => mouseSpeed;
        set
        {
            if (SetProperty(ref mouseSpeed, Math.Round(Math.Clamp(value, 0.1, 5), 1)))
            {
                settings.SetMouseSpeed(mouseSpeed);
                if (activeSession != null)
                {
                    activeSession.MouseSpeed = mouseSpeed;
                }
            }
        }
    }

    /// <summary>True while the event log is shown. It is hidden when the app starts, and not saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogToggleText))]
    private bool isLogVisible;

    /// <summary>Text of the button that shows or hides the event log.</summary>
    public string LogToggleText => IsLogVisible ? "▾ Event log" : "▸ Event log";

    /// <summary>True while the Settings panel is shown.</summary>
    [ObservableProperty]
    private bool isSettingsOpen;

    /// <summary>Read the mouse as raw input in control mode. Saved. It is used the next time control mode starts.</summary>
    public bool UseRawMouse
    {
        get => useRawMouse;
        set
        {
            if (SetProperty(ref useRawMouse, value))
            {
                settings.SetUseRawMouse(value);
                AddLog($"Setting changed: mouse input is read {(value ? "as raw input" : "from the cursor position")}.");
            }
        }
    }

    /// <summary>
    /// Show every paired device in the Available row. Off by default: only phones and computers are shown. Saved, and the
    /// list is refreshed at once.
    /// </summary>
    public bool ShowAllDevices
    {
        get => showAllDevices;
        set
        {
            if (SetProperty(ref showAllDevices, value))
            {
                settings.SetShowAllDevices(value);
                AddLog($"Setting changed: {(value ? "all paired devices are shown" : "only phones and computers are shown")}.");
                SyncDevices(bluetooth.Devices);
            }
        }
    }

    /// <summary>The shortcut for the whole system that starts control mode. Saved. The window registers it with the system.</summary>
    public HotkeyCombo Hotkey
    {
        get => hotkey;
        set
        {
            if (value != null && SetProperty(ref hotkey, value))
            {
                settings.SetHotkey(value);
                OnPropertyChanged(nameof(HotkeyButtonText));
                OnPropertyChanged(nameof(SwitchHint));
                AddLog($"Setting changed: the control shortcut is {value}.");
            }
        }
    }

    /// <summary>True when this platform can start OmniBlu with the system. The choice in Settings is disabled otherwise.</summary>
    public bool IsStartupSupported => autoStart.IsSupported;

    /// <summary>
    /// What OmniBlu does when Windows starts: 0 = nothing, 1 = open the window, 2 = run in the tray. Windows keeps this
    /// choice (not the settings file), so it is read from Windows at start and written to Windows at once.
    /// </summary>
    public int StartupModeIndex
    {
        get => (int)startupMode;
        set
        {
            var mode = Enum.IsDefined(typeof(StartupMode), value) ? (StartupMode)value : StartupMode.Off;
            if (mode == startupMode)
            {
                return;
            }

            if (autoStart.Apply(mode))
            {
                startupMode = mode;
                StartupStatus = string.Empty;
                AddLog($"Setting changed: start with Windows is {mode}.");
            }
            else
            {
                StartupStatus = autoStart.LastError ?? "The start-up setting could not be saved.";
                AddLog(StartupStatus);
            }

            // The list shows the real state again, also when saving failed.
            OnPropertyChanged();
        }
    }

    /// <summary>Why the start-up setting could not be saved. Empty when there is nothing to say.</summary>
    [ObservableProperty]
    private string startupStatus = string.Empty;

    /// <summary>True while the Settings panel waits for the user to press a new shortcut.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyButtonText))]
    private bool isCapturingHotkey;

    /// <summary>Tells the user whether the shortcut is active, or why it is not. Empty when there is nothing to say.</summary>
    [ObservableProperty]
    private string hotkeyStatus = string.Empty;

    /// <summary>Text of the shortcut button in the Settings panel.</summary>
    public string HotkeyButtonText => IsCapturingHotkey ? "Press the new shortcut (Esc cancels)" : Hotkey.ToString();

    /// <summary>Key of the device that was controlled most recently, or null. The shortcut returns to it first.</summary>
    public string LastControlledKey { get; private set; }

    partial void OnIsSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            IsCapturingHotkey = false;
        }
    }

    /// <summary>Puts every setting in the Settings panel back to its default value.</summary>
    public void ResetSettings()
    {
        MouseSpeed = 1.0;
        UseRawMouse = true;
        ShowAllDevices = false;
        Hotkey = HotkeyCombo.Default;
        if (autoStart.IsSupported)
        {
            StartupModeIndex = (int)StartupMode.Off;
        }
    }

    public bool IsControlling => ControllingDevice != null;

    public string ControlBanner => ControllingDevice == null ? string.Empty : $"Controlling {ControllingDevice.Name}";

    /// <summary>Where the controlled device is in the Controllable row, for example "Device #2 of 3".</summary>
    public string ControlPositionText => ControllingDevice == null
        ? string.Empty
        : $"Device #{ControllingDevice.ControlPriority} of {ControlDevices.Count}";

    /// <summary>True when there is more than one device in the Controllable row, so the shortcut has somewhere to switch to.</summary>
    public bool HasSwitchHint => ControllingDevice != null && ControlDevices.Count > 1;

    /// <summary>Tells how to switch to the next device while control runs.</summary>
    public string SwitchHint => $"{Hotkey}: next device";

    public bool IsDevicesEmpty => Devices.Count == 0;

    public bool IsAudioEmpty => AudioDevices.Count == 0;

    public bool IsControlEmpty => ControlDevices.Count == 0;

    /// <summary>Where the event log is also saved.</summary>
    public string LogFilePath => fileLog.FilePath;

    public void AddLog(string message)
    {
        fileLog.Write(message);
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > MaxLogLines)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    public DeviceViewModel FindDevice(string key) => Devices.FirstOrDefault(d => d.Key == key);

    private ObservableCollection<DeviceViewModel> ListOf(RowKind kind) => kind == RowKind.Audio ? AudioDevices : ControlDevices;

    // ------------------------------------------------------------- row 1: connect

    public async Task ToggleConnectionAsync(DeviceViewModel device)
    {
        if (!device.CanToggle)
        {
            return;
        }

        if (device.IsReady)
        {
            // Disconnect clears both roles.
            if (ControlDevices.Contains(device))
            {
                await UnassignControlAsync(device);
            }

            await bluetooth.DisconnectAsync(device.Key);
        }
        else
        {
            await bluetooth.ConnectAsync(device.Key);
        }
    }

    // ------------------------------------------------------------ drop rules

    public DropVerdict Evaluate(RowKind target, RowKind source, DeviceViewModel device)
    {
        if (device == null)
        {
            return DropVerdict.Reject;
        }

        switch (target)
        {
            case RowKind.Available:
                return source == RowKind.Available ? DropVerdict.Reject : DropVerdict.Remove;

            case RowKind.Audio:
                if (!device.SupportsA2dp || !device.IsReady || device.IsBusy)
                {
                    return DropVerdict.Reject;
                }

                return AudioDevices.Contains(device) ? DropVerdict.Reorder : DropVerdict.Add;

            default:
                // HID support cannot be detected in advance, so any device may be dropped here.
                return ControlDevices.Contains(device) ? DropVerdict.Reorder : DropVerdict.Add;
        }
    }

    // -------------------------------------------------------------- row 2: audio

    /// <summary>Opens the audio stream of a ready device and puts it in the Audio row, or moves it inside the row.</summary>
    public async Task AssignAudioAsync(DeviceViewModel device, int index)
    {
        if (AudioDevices.Contains(device))
        {
            MoveInRow(RowKind.Audio, device, index);
            return;
        }

        if (!await bluetooth.OpenAudioAsync(device.Key))
        {
            return;
        }

        // The state event may already have added the card at the end. Put it where the user dropped it.
        if (AudioDevices.Contains(device))
        {
            MoveInRow(RowKind.Audio, device, index);
        }
        else
        {
            AudioDevices.Insert(Math.Clamp(index, 0, AudioDevices.Count), device);
            Renumber();
        }
    }

    /// <summary>Closes the audio stream. The device stays connected (ready).</summary>
    public async Task UnassignAudioAsync(DeviceViewModel device)
    {
        if (AudioDevices.Remove(device))
        {
            Renumber();
        }

        await bluetooth.CloseAudioAsync(device.Key);
    }

    // ------------------------------------------------------------ row 3: control

    /// <summary>Lets this PC send input to the device, or moves it inside the row. Starts advertising if needed.</summary>
    public async Task AssignControlAsync(DeviceViewModel device, int index)
    {
        if (ControlDevices.Contains(device))
        {
            MoveInRow(RowKind.Control, device, index);
            return;
        }

        if (!await hid.EnableAsync(device.Key))
        {
            return;
        }

        if (ControlDevices.Contains(device))
        {
            MoveInRow(RowKind.Control, device, index);
        }
        else
        {
            ControlDevices.Insert(Math.Clamp(index, 0, ControlDevices.Count), device);
            Renumber();
        }
    }

    /// <summary>Stops sending input to the device. A2DP is not changed.</summary>
    public async Task UnassignControlAsync(DeviceViewModel device)
    {
        if (ControlDevices.Remove(device))
        {
            Renumber();
        }

        await hid.DisableAsync(device.Key);
    }

    /// <summary>
    /// Starts control mode: the keyboard and mouse of this PC go to the device. Returns the session to feed with input,
    /// or null when the device is not connected over HID. Call <see cref="EndControl"/> when it is stopped.
    /// </summary>
    public HidControlSession BeginControl(DeviceViewModel device, bool rawMouse)
    {
        if (ControllingDevice != null || !RequireHid(device))
        {
            return null;
        }

        ControllingDevice = device;
        LastControlledKey = device.Key;
        IsSettingsOpen = false;
        AddLog($"Control mode started for {device.Name} (mouse speed {MouseSpeed:0.0}x, "
            + $"{(rawMouse ? "raw mouse input" : "mouse read from the cursor position")}). Press Right Ctrl to stop.");

        // Raw counts are small steps from a real mouse, so a report may use its full range. Cursor movement in whole
        // pixels is already accelerated by Windows, so it is limited more (see HidControlSession.MaxMouseStep).
        activeSession = new HidControlSession(hid, device.Key, rawMouse ? 127 : HidControlSession.MaxMouseStep)
        {
            MouseSpeed = MouseSpeed,
        };
        return activeSession;
    }

    public void EndControl(string reason)
    {
        if (ControllingDevice == null)
        {
            return;
        }

        AddLog($"Control mode ended for {ControllingDevice.Name}. {reason}");
        ControllingDevice = null;
        activeSession = null;
    }

    private bool RequireHid(DeviceViewModel device)
    {
        if (device.IsHidActive)
        {
            return true;
        }

        AddLog($"{device.Name} is not connected over HID yet. Pair this PC on the phone first.");
        return false;
    }

    // -------------------------------------------------------------- row order

    private void MoveInRow(RowKind kind, DeviceViewModel device, int index)
    {
        var list = ListOf(kind);
        var existing = list.IndexOf(device);
        index = Math.Clamp(index, 0, list.Count);
        if (existing < index)
        {
            index--;
        }

        if (existing != index)
        {
            list.Move(existing, index);
            AddLog($"{device.Name} priority in {(kind == RowKind.Audio ? "Audio" : "Controllable")} is now #{index + 1}.");
        }

        Renumber();
    }

    private void Renumber()
    {
        foreach (var device in Devices)
        {
            device.AudioPriority = AudioDevices.IndexOf(device) + 1;
            device.ControlPriority = ControlDevices.IndexOf(device) + 1;
        }

        OnPropertyChanged(nameof(IsAudioEmpty));
        OnPropertyChanged(nameof(IsControlEmpty));
    }

    // ------------------------------------------------------ events from the services

    private void ApplyState(DeviceStateChangedEventArgs e)
    {
        var device = FindDevice(e.Key);
        if (device == null)
        {
            return;
        }

        device.ConnectionState = e.Connection;
        device.AudioState = e.Audio;

        // The audio row mirrors reality. The phone can open or close the stream by itself.
        if (e.Audio == AudioState.Streaming && !AudioDevices.Contains(device))
        {
            AudioDevices.Add(device);
            Renumber();
        }
        else if (e.Audio == AudioState.Off && AudioDevices.Remove(device))
        {
            Renumber();
        }
    }

    private void ApplyHidState(HidStateChangedEventArgs e)
    {
        var device = FindDevice(e.Key);
        if (device == null)
        {
            return;
        }

        device.HidState = e.State;

        if (e.State == HidState.Active && !device.HidVerified)
        {
            device.HidVerified = true;
            settings.SetHidVerified(device.Key, true);
            AddLog($"{device.Name} works over HID. Remembered for next time.");
        }
    }

    /// <summary>A phone connected over HID that is not in the list. Show it, so it is never lost.</summary>
    private void AddHidOnlyDevice(UnknownHidClientEventArgs e)
    {
        if (FindDevice(e.Key) != null)
        {
            return;
        }

        var info = new SmartDeviceInfo(e.Key, string.IsNullOrWhiteSpace(e.Name) ? "Unknown phone" : e.Name, null, e.Address);
        var saved = settings.GetDevice(e.Key);

        loading = true;
        try
        {
            var device = new DeviceViewModel(info, saved?.Name ?? info.Name, saved?.Icon ?? DefaultUnsupportedIcon, saved?.HidVerified ?? false);
            device.PropertyChanged += Device_PropertyChanged;
            hidOnlyKeys.Add(e.Key);
            Devices.Add(device);
            OnPropertyChanged(nameof(IsDevicesEmpty));
        }
        finally
        {
            loading = false;
        }

        AddLog($"{info.Name} connected over HID but is not in the device list. Added it.");
    }

    private void SyncDevices(IReadOnlyList<SmartDeviceInfo> snapshot)
    {
        loading = true;
        try
        {
            // This PC acts as a peripheral, so only phones and computers are listed unless the user asks for all.
            // A device that is already in the Audio or Control row stays, so a setting change cannot drop it from use.
            var shown = snapshot
                .Where(d => ShowAllDevices || d.IsHostLike || IsInUse(d.Key))
                .ToList();
            var keys = shown.Select(d => d.Key).ToHashSet();
            foreach (var gone in Devices.Where(d => !keys.Contains(d.Key) && !hidOnlyKeys.Contains(d.Key)).ToList())
            {
                AudioDevices.Remove(gone);
                ControlDevices.Remove(gone);
                Devices.Remove(gone);
            }

            foreach (var info in shown)
            {
                var saved = settings.GetDevice(info.Key);
                var device = FindDevice(info.Key);
                if (device == null)
                {
                    device = new DeviceViewModel(
                        info,
                        saved?.Name ?? info.Name,
                        saved?.Icon ?? DefaultIconFor(info.SupportsA2dp),
                        saved?.HidVerified ?? false);
                    device.PropertyChanged += Device_PropertyChanged;
                    Devices.Add(device);
                }
                else
                {
                    device.UpdateFrom(info, saved?.Name != null);
                }
            }

            Renumber();
            OnPropertyChanged(nameof(IsDevicesEmpty));
        }
        finally
        {
            loading = false;
        }
    }

    private bool IsInUse(string key) =>
        AudioDevices.Any(d => d.Key == key) || ControlDevices.Any(d => d.Key == key);

    // ------------------------------------------------------------- customization

    private void Device_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (loading || sender is not DeviceViewModel device)
        {
            return;
        }

        if (e.PropertyName is not (nameof(DeviceViewModel.Name) or nameof(DeviceViewModel.Icon)))
        {
            return;
        }

        var name = device.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            // An empty name is not allowed. Go back to the name from Windows.
            loading = true;
            device.Name = device.SystemName;
            loading = false;
            name = device.SystemName;
        }

        var icon = string.IsNullOrWhiteSpace(device.Icon) ? DefaultIconFor(device.SupportsA2dp) : device.Icon;

        settings.SetDevice(
            device.Key,
            name == device.SystemName ? null : name,
            icon == DefaultIconFor(device.SupportsA2dp) ? null : icon);
    }

    private static string DefaultIconFor(bool supportsA2dp) => supportsA2dp ? DefaultIcon : DefaultUnsupportedIcon;
}
