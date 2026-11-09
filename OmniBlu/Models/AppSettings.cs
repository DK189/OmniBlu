namespace OmniBlu.Models;

/// <summary>User settings saved to disk.</summary>
public sealed class AppSettings
{
    /// <summary>Per-device customization, keyed by <see cref="SmartDeviceInfo.Key"/>.</summary>
    public Dictionary<string, DeviceSettings> Devices { get; set; } = new();

    /// <summary>Multiplies mouse movement sent to a phone in control mode. 1.0 is unchanged.</summary>
    public double MouseSpeed { get; set; } = 1.0;

    /// <summary>
    /// Read the mouse as raw input in control mode (like a real mouse, with no Windows pointer acceleration). When off,
    /// the position of the cursor is used. Can be changed in the Settings panel.
    /// </summary>
    public bool UseRawMouse { get; set; } = true;

    /// <summary>
    /// Show every paired Bluetooth device. When off (the default), only phones and computers are shown, because this PC
    /// works as a speaker and a keyboard and mouse for them. Headsets, game controllers and similar devices are hidden.
    /// </summary>
    public bool ShowAllDevices { get; set; }

    /// <summary>
    /// Shortcut for the whole system that starts control mode, shows the window, or switches to the next device,
    /// for example "Win+`". See <c>HotkeyCombo</c>.
    /// </summary>
    public string Hotkey { get; set; } = "Win+`";
}

/// <summary>What the user changed, and what the app learned, for one device.</summary>
public sealed class DeviceSettings
{
    /// <summary>Custom name. Null means "use the name from Windows".</summary>
    public string Name { get; set; }

    /// <summary>Custom icon (an emoji). Null means "use the default icon".</summary>
    public string Icon { get; set; }

    /// <summary>True once this device has connected over HID at least once. HID support cannot be detected in advance.</summary>
    public bool HidVerified { get; set; }
}
