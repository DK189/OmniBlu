namespace OmniBlu.Managers;

/// <summary>State of the HID connection (keyboard and mouse sent to the device).</summary>
public enum HidState
{
    Off,

    /// <summary>This PC is advertising as a keyboard and mouse. The phone has not connected yet.</summary>
    Waiting,

    /// <summary>The phone is connected and receives input.</summary>
    Active,
}

public sealed class HidStateChangedEventArgs : EventArgs
{
    public HidStateChangedEventArgs(string key, HidState state)
    {
        Key = key;
        State = state;
    }

    public string Key { get; }

    public HidState State { get; }
}

/// <summary>A phone connected over HID that is not in the device list.</summary>
public sealed class UnknownHidClientEventArgs : EventArgs
{
    public UnknownHidClientEventArgs(string key, string name, string address)
    {
        Key = key;
        Name = name;
        Address = address;
    }

    public string Key { get; }

    public string Name { get; }

    public string Address { get; }
}

/// <summary>
/// Lets this PC act as a Bluetooth keyboard and mouse (HID over GATT) for a phone. The phone is the host and connects
/// by itself. The PC only advertises while at least one device is enabled. All events can be raised on any thread.
/// </summary>
public interface IHidService
{
    event EventHandler<HidStateChangedEventArgs> StateChanged;

    event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected;

    event EventHandler<string> LogMessage;

    /// <summary>Allows this PC to send input to the device. Starts advertising if needed.</summary>
    Task<bool> EnableAsync(string key);

    /// <summary>Stops sending input to the device. Stops advertising when no device is enabled.</summary>
    Task DisableAsync(string key);

    /// <summary>Presses and releases the left mouse button on the device.</summary>
    Task<bool> SendClickAsync(string key);

    /// <summary>Presses and releases one key (HID usage code, with optional modifier bits) on the device.</summary>
    Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0);

    /// <summary>Sends one raw keyboard input report (8 bytes: modifiers, reserved, 6 keys). Returns false if it was not delivered.</summary>
    Task<bool> SendKeyboardReportAsync(string key, byte[] report);

    /// <summary>Sends one raw mouse input report (4 bytes: buttons, x, y, wheel). Returns false if it was not delivered.</summary>
    Task<bool> SendMouseReportAsync(string key, byte[] report);

    /// <summary>
    /// Types text on the device, one character at a time. Returns how many characters were sent. Characters that
    /// cannot be typed (see <see cref="HidKeyboard"/>) are skipped.
    /// </summary>
    Task<int> SendTextAsync(string key, string text);
}
