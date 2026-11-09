#if WINDOWS
using Microsoft.Extensions.Hosting;
using OmniBlu.Managers;
using OmniBlu.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Audio;

namespace OmniBlu.Natives;

/// <summary>
/// Windows implementation. This PC is the peripheral: it uses <see cref="AudioPlaybackConnection"/> to act as an
/// A2DP sink, and the phone is the host.
/// <para>
/// Connect = <c>Start()</c> (ready, the phone can connect). Open audio = <c>OpenAsync()</c> (audio stream).
/// Every native create, start, open and dispose call runs under one lock, because the Windows runtime can crash
/// (access violation 0xc0000005) when these calls overlap or hit a device that just went away.
/// </para>
/// </summary>
internal sealed class WindowsBluetoothService : IBluetoothService, IHostedService, IDisposable
{
    private const string PairedContainerProperty = "System.Devices.Aep.ContainerId";
    private const string PairedAddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string PairedMajorClassProperty = "System.Devices.Aep.Bluetooth.Cod.Major";
    private static readonly string[] PairedProperties = { PairedContainerProperty, PairedAddressProperty, PairedMajorClassProperty };
    private const string EndpointContainerProperty = "System.Devices.ContainerId";

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();

    private readonly Dictionary<string, PairedRecord> pairedById = new();
    private readonly Dictionary<string, EndpointRecord> endpointById = new();
    private readonly Dictionary<string, ActiveConnection> connections = new();

    private DeviceWatcher pairedWatcher;
    private DeviceWatcher endpointWatcher;
    private IReadOnlyList<SmartDeviceInfo> devices = Array.Empty<SmartDeviceInfo>();
    private bool stopped;

    public IReadOnlyList<SmartDeviceInfo> Devices
    {
        get
        {
            lock (sync)
            {
                return devices;
            }
        }
    }

    public event EventHandler DevicesChanged;

    public event EventHandler<DeviceStateChangedEventArgs> StateChanged;

    public event EventHandler<string> LogMessage;

    // ------------------------------------------------------------ host lifetime

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Initial snapshot first, so the UI has devices as soon as it opens.
            foreach (var device in await DeviceInformation.FindAllAsync(
                         BluetoothDevice.GetDeviceSelectorFromPairingState(true), PairedProperties))
            {
                lock (sync)
                {
                    pairedById[device.Id] = ToPaired(device);
                }
            }

            foreach (var device in await DeviceInformation.FindAllAsync(
                         AudioPlaybackConnection.GetDeviceSelector(), new[] { EndpointContainerProperty }))
            {
                lock (sync)
                {
                    endpointById[device.Id] = ToEndpoint(device);
                }
            }

            Rebuild();

            // Watchers keep the lists up to date (phone Bluetooth on/off, new pairings).
            pairedWatcher = DeviceInformation.CreateWatcher(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true), PairedProperties);
            pairedWatcher.Added += (_, device) => OnWatcherEvent(() => pairedById[device.Id] = ToPaired(device));
            pairedWatcher.Removed += (_, update) => OnWatcherEvent(() => pairedById.Remove(update.Id));
            pairedWatcher.Start();

            endpointWatcher = DeviceInformation.CreateWatcher(
                AudioPlaybackConnection.GetDeviceSelector(), new[] { EndpointContainerProperty });
            endpointWatcher.Added += (_, device) => OnWatcherEvent(() => endpointById[device.Id] = ToEndpoint(device));
            endpointWatcher.Removed += (_, update) => OnWatcherEvent(() => endpointById.Remove(update.Id));
            endpointWatcher.Start();

            Log("Bluetooth watchers started.");
        }
        catch (Exception ex)
        {
            // No Bluetooth radio, or Bluetooth is turned off. The app still opens with an empty list.
            Log($"Could not start Bluetooth discovery: {ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        stopped = true;
        StopWatcher(ref pairedWatcher);
        StopWatcher(ref endpointWatcher);

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            foreach (var connection in connections.Values)
            {
                connection.Dispose();
            }

            connections.Clear();
        }
        finally
        {
            gate.Release();
        }

        Log("Bluetooth service stopped.");
    }

    public void Dispose()
    {
        StopWatcher(ref pairedWatcher);
        StopWatcher(ref endpointWatcher);
        gate.Dispose();
    }

    // -------------------------------------------------------------- operations

    public async Task<bool> ConnectAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            if (connections.ContainsKey(key))
            {
                return true;
            }

            var device = FindDevice(key);
            if (device == null || !device.SupportsA2dp)
            {
                Raise(key, ConnectionState.Disconnected, AudioState.Off, "This device cannot send audio to this PC (no A2DP).");
                return false;
            }

            Raise(key, ConnectionState.Connecting, AudioState.Off, $"Connecting to {device.Name}...");

            var connection = await CreateAndStartAsync(key, device.A2dpEndpointId);
            if (connection == null)
            {
                Raise(key, ConnectionState.Disconnected, AudioState.Off, $"Could not connect to {device.Name}. Check that its Bluetooth is on.");
                return false;
            }

            lock (sync)
            {
                connections[key] = connection;
            }

            Raise(key, ConnectionState.Ready, AudioState.Off, $"{device.Name} is ready. Waiting for the phone.");
            return true;
        }
        catch (Exception ex)
        {
            Raise(key, ConnectionState.Disconnected, AudioState.Off, $"Connect failed: {ex.Message}");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            if (TakeConnection(key) is { } connection)
            {
                connection.Dispose();
            }

            Raise(key, ConnectionState.Disconnected, AudioState.Off, "Disconnected.");
        }
        catch (Exception ex)
        {
            Raise(key, ConnectionState.Disconnected, AudioState.Off, $"Disconnect failed: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> OpenAudioAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            ActiveConnection connection;
            lock (sync)
            {
                connections.TryGetValue(key, out connection);
            }

            if (connection == null)
            {
                Raise(key, ConnectionState.Disconnected, AudioState.Off, "Connect the device before opening audio.");
                return false;
            }

            Raise(key, ConnectionState.Ready, AudioState.Opening, "Opening the audio stream...");

            var result = await connection.Native.OpenAsync();
            if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                Raise(key, ConnectionState.Ready, AudioState.Streaming, "Audio stream opened.");
                return true;
            }

            var error = result.ExtendedError != null ? $"{result.Status} ({result.ExtendedError.Message})" : result.Status.ToString();
            Raise(key, ConnectionState.Ready, AudioState.Off, $"Could not open the audio stream: {error}");
            return false;
        }
        catch (Exception ex)
        {
            Raise(key, ConnectionState.Ready, AudioState.Off, $"Open audio failed: {ex.Message}");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Windows has no call that closes the stream and keeps the endpoint started. So this disposes the connection and
    /// starts a new one. The phone may reconnect the stream by itself afterwards.
    /// </summary>
    public async Task CloseAudioAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            if (TakeConnection(key) is not { } old)
            {
                return;
            }

            var endpointId = old.EndpointId;
            old.Dispose();

            var fresh = await CreateAndStartAsync(key, endpointId);
            if (fresh == null)
            {
                Raise(key, ConnectionState.Disconnected, AudioState.Off, "Audio closed, but the device could not be made ready again.");
                return;
            }

            lock (sync)
            {
                connections[key] = fresh;
            }

            Raise(key, ConnectionState.Ready, AudioState.Off, "Audio stream closed. The device is still ready.");
        }
        catch (Exception ex)
        {
            Raise(key, ConnectionState.Disconnected, AudioState.Off, $"Close audio failed: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------------------------------------------------------- internals

    /// <summary>Creates and starts a native connection. Must be called with the gate held.</summary>
    private async Task<ActiveConnection> CreateAndStartAsync(string key, string endpointId)
    {
        // Crash guard: the native create call can crash on an endpoint that is asleep or gone. Check the endpoint first.
        // This must be the A2DP endpoint ID. The paired-device record always reports IsEnabled = false.
        var info = await DeviceInformation.CreateFromIdAsync(endpointId);
        if (info == null || !info.IsEnabled)
        {
            return null;
        }

        var native = AudioPlaybackConnection.TryCreateFromId(endpointId);
        if (native == null)
        {
            return null;
        }

        var connection = new ActiveConnection(native, endpointId);
        connection.Handler = (sender, _) => OnNativeStateChanged(key, connection);
        native.StateChanged += connection.Handler;

        try
        {
            native.Start();
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return connection;
    }

    /// <summary>
    /// The phone can open or close the stream by itself. Report it, unless the connection was already replaced.
    /// </summary>
    private void OnNativeStateChanged(string key, ActiveConnection connection)
    {
        try
        {
            lock (sync)
            {
                if (!connections.TryGetValue(key, out var current) || !ReferenceEquals(current, connection))
                {
                    return;
                }
            }

            var opened = connection.Native.State == AudioPlaybackConnectionState.Opened;
            Raise(key, ConnectionState.Ready, opened ? AudioState.Streaming : AudioState.Off,
                opened ? "The phone opened the audio stream." : "The phone closed the audio stream.");
        }
        catch (Exception ex)
        {
            Log($"State change handler failed: {ex.Message}");
        }
    }

    private ActiveConnection TakeConnection(string key)
    {
        lock (sync)
        {
            if (connections.Remove(key, out var connection))
            {
                return connection;
            }
        }

        return null;
    }

    private SmartDeviceInfo FindDevice(string key) => Devices.FirstOrDefault(d => d.Key == key);

    private void OnWatcherEvent(Action update)
    {
        if (stopped)
        {
            return;
        }

        try
        {
            lock (sync)
            {
                update();
            }

            Rebuild();
        }
        catch (Exception ex)
        {
            Log($"Bluetooth watcher event failed: {ex.Message}");
        }
    }

    /// <summary>Merges the two lists, publishes the result, and cleans up connections of devices that went away.</summary>
    private void Rebuild()
    {
        List<SmartDeviceInfo> merged;
        List<string> gone;

        lock (sync)
        {
            merged = DeviceMerger.Merge(pairedById.Values, endpointById.Values);
            devices = merged;

            gone = connections
                .Where(c => !merged.Any(d => d.Key == c.Key && d.A2dpEndpointId == c.Value.EndpointId))
                .Select(c => c.Key)
                .ToList();
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);

        foreach (var key in gone)
        {
            // The device went away (for example the phone turned Bluetooth off). Dispose the native object now,
            // so no stale connection is left behind.
            _ = Task.Run(async () =>
            {
                Log("A device went out of range. Cleaning up its connection.");
                await DisconnectAsync(key);
            });
        }
    }

    private void Raise(string key, ConnectionState connection, AudioState audio, string message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            Log(message);
        }

        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(key, connection, audio, message));
    }

    private void Log(string message) => LogMessage?.Invoke(this, message);

    private static PairedRecord ToPaired(DeviceInformation device) =>
        new(device.Id, device.Name, ContainerOf(device.Properties, PairedContainerProperty),
            ContainerOf(device.Properties, PairedAddressProperty), MajorClassOf(device.Properties));

    /// <summary>Reads the Bluetooth major class. Returns null when Windows does not give one (for example a LE-only device).</summary>
    private static int? MajorClassOf(IReadOnlyDictionary<string, object> properties)
    {
        if (properties.TryGetValue(PairedMajorClassProperty, out var value) && value != null)
        {
            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return null;
            }
        }

        return null;
    }

    private static EndpointRecord ToEndpoint(DeviceInformation device) =>
        new(device.Id, device.Name, ContainerOf(device.Properties, EndpointContainerProperty));

    private static string ContainerOf(IReadOnlyDictionary<string, object> properties, string name) =>
        properties.TryGetValue(name, out var value) && value != null ? value.ToString() : string.Empty;

    private static void StopWatcher(ref DeviceWatcher watcher)
    {
        var current = watcher;
        watcher = null;
        if (current == null)
        {
            return;
        }

        try
        {
            current.Stop();
        }
        catch
        {
            // Stopping a watcher that is already stopped throws. There is nothing to do about it.
        }
    }

    private sealed class ActiveConnection : IDisposable
    {
        public ActiveConnection(AudioPlaybackConnection native, string endpointId)
        {
            Native = native;
            EndpointId = endpointId;
        }

        public AudioPlaybackConnection Native { get; }

        public string EndpointId { get; }

        public TypedEventHandler<AudioPlaybackConnection, object> Handler { get; set; }

        public void Dispose()
        {
            try
            {
                if (Handler != null)
                {
                    Native.StateChanged -= Handler;
                }

                Native.Dispose();
            }
            catch
            {
                // The native object can already be closed. Disposing twice is not an error for us.
            }
        }
    }
}
#endif
