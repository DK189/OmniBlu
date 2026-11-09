using OmniBlu.Models;

namespace OmniBlu.Managers;

public sealed class DeviceStateChangedEventArgs : EventArgs
{
    public DeviceStateChangedEventArgs(string key, ConnectionState connection, AudioState audio, string message)
    {
        Key = key;
        Connection = connection;
        Audio = audio;
        Message = message;
    }

    public string Key { get; }

    public ConnectionState Connection { get; }

    public AudioState Audio { get; }

    /// <summary>Human-readable message for the event log. Can be empty.</summary>
    public string Message { get; }
}

/// <summary>
/// Talks to the operating system Bluetooth stack. This PC acts as the peripheral (an A2DP sink), and the smart device
/// is the host. All events can be raised on any thread.
/// </summary>
public interface IBluetoothService
{
    /// <summary>Snapshot of the known devices.</summary>
    IReadOnlyList<SmartDeviceInfo> Devices { get; }

    /// <summary>Raised when devices appear, disappear or change. Read <see cref="Devices"/> again.</summary>
    event EventHandler DevicesChanged;

    /// <summary>Raised when the connection or audio state of a device changes.</summary>
    event EventHandler<DeviceStateChangedEventArgs> StateChanged;

    event EventHandler<string> LogMessage;

    /// <summary>Makes this PC ready to receive audio from the device (Start). The phone can then connect.</summary>
    Task<bool> ConnectAsync(string key);

    /// <summary>Stops everything for the device (audio stream and ready mode).</summary>
    Task DisconnectAsync(string key);

    /// <summary>Opens the audio stream from a ready device (Open).</summary>
    Task<bool> OpenAudioAsync(string key);

    /// <summary>Closes the audio stream and keeps the device ready.</summary>
    Task CloseAudioAsync(string key);
}
