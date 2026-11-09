#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Hosting;
using OmniBlu.Managers;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace OmniBlu.Natives;

/// <summary>
/// Windows implementation. This PC publishes a HID over GATT service (keyboard + mouse) as a Bluetooth LE peripheral.
/// The phone is the host: it connects by itself, and this PC then sends input reports as notifications.
/// <para>
/// Advertising makes this PC visible to nearby devices, so it is started only while at least one device is enabled
/// (never at app start) and stopped as soon as none is.
/// </para>
/// </summary>
internal sealed class WindowsHidService : IHidService, IHostedService
{
    private readonly IBluetoothService bluetooth;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();

    private readonly HashSet<string> enabled = new();
    private readonly Dictionary<string, HidState> lastStates = new();
    private readonly HashSet<string> announcedUnknown = new();
    private readonly Dictionary<string, (string Address, string Name)> clientCache = new();

    /// <summary>LE records of the enabled devices, kept only to log when their Bluetooth link goes up or down.</summary>
    private readonly Dictionary<string, BluetoothLEDevice> links = new();

    private GattServiceProvider provider;
    private GattLocalCharacteristic keyboard;
    private GattLocalCharacteristic mouse;
    private Dictionary<string, GattSubscribedClient> keyboardClients = new();
    private Dictionary<string, GattSubscribedClient> mouseClients = new();
    private bool advertising;

    public WindowsHidService(IBluetoothService bluetooth)
    {
        this.bluetooth = bluetooth;
    }

    public event EventHandler<HidStateChangedEventArgs> StateChanged;

    public event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected;

    public event EventHandler<string> LogMessage;

    // ------------------------------------------------------------ host lifetime

    /// <summary>Nothing to do: the service stays silent (no advertising) until a device is enabled.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            StopAdvertising();
            lock (sync)
            {
                enabled.Clear();
                keyboardClients = new Dictionary<string, GattSubscribedClient>();
                mouseClients = new Dictionary<string, GattSubscribedClient>();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    // -------------------------------------------------------------- operations

    public async Task<bool> EnableAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            if (provider == null)
            {
                var error = await CreateProviderAsync();
                if (error != null)
                {
                    Log($"HID is not available on this PC: {error}");
                    return false;
                }
            }

            lock (sync)
            {
                enabled.Add(key);
            }

            if (!advertising)
            {
                provider.StartAdvertising(new GattServiceProviderAdvertisingParameters
                {
                    IsDiscoverable = true,
                    IsConnectable = true,
                });
                advertising = true;
                Log($"Advertising as a Bluetooth keyboard and mouse. On the phone, look for \"{Environment.MachineName}\" (the computer name) and pair.");
            }

            RaiseState(key);
            _ = WatchLinkAsync(key);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Could not enable HID: {ex.Message}");
            lock (sync)
            {
                enabled.Remove(key);
            }

            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisableAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            bool none;
            lock (sync)
            {
                enabled.Remove(key);
                lastStates.Remove(key);
                none = enabled.Count == 0;
            }

            StateChanged?.Invoke(this, new HidStateChangedEventArgs(key, HidState.Off));
            StopWatchingLink(key);

            // Windows cannot disconnect a phone from the server side. The device just stops receiving input.
            if (none)
            {
                StopAdvertising();
            }
        }
        catch (Exception ex)
        {
            Log($"Could not disable HID: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> SendClickAsync(string key)
    {
        var client = ClientFor(key, mouseClients);
        if (client == null || mouse == null)
        {
            Log("The phone is not connected over HID yet.");
            return false;
        }

        try
        {
            var down = await NotifyAsync(mouse, HidReports.Mouse(HidReports.MouseLeftButton, 0, 0, 0), client);
            await Task.Delay(40);
            var up = await NotifyAsync(mouse, HidReports.Mouse(0, 0, 0, 0), client);
            return down && up;
        }
        catch (Exception ex)
        {
            Log($"Could not send the click: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0)
    {
        var client = ClientFor(key, keyboardClients);
        if (client == null || keyboard == null)
        {
            Log("The phone is not connected over HID yet.");
            return false;
        }

        try
        {
            return await PressAsync(client, usage, modifiers);
        }
        catch (Exception ex)
        {
            Log($"Could not send the key: {ex.Message}");
            return false;
        }
    }

    public async Task<int> SendTextAsync(string key, string text)
    {
        var client = ClientFor(key, keyboardClients);
        if (client == null || keyboard == null)
        {
            Log("The phone is not connected over HID yet.");
            return 0;
        }

        var sent = 0;
        try
        {
            foreach (var c in text ?? string.Empty)
            {
                if (!HidKeyboard.TryMap(c, out var hidKey))
                {
                    continue;
                }

                if (!await PressAsync(client, hidKey.Usage, hidKey.Modifiers))
                {
                    break;
                }

                sent++;
            }
        }
        catch (Exception ex)
        {
            Log($"Could not type the text: {ex.Message}");
        }

        return sent;
    }

    public Task<bool> SendKeyboardReportAsync(string key, byte[] report) => SendRawAsync(key, keyboardClients, () => keyboard, report);

    public Task<bool> SendMouseReportAsync(string key, byte[] report) => SendRawAsync(key, mouseClients, () => mouse, report);

    /// <summary>Sends one report with no delay. Used by the control session, which paces itself.</summary>
    private async Task<bool> SendRawAsync(string key, Dictionary<string, GattSubscribedClient> clients, Func<GattLocalCharacteristic> characteristic, byte[] report)
    {
        var client = ClientFor(key, clients);
        var target = characteristic();
        if (client == null || target == null)
        {
            return false;
        }

        try
        {
            return await NotifyAsync(target, report, client);
        }
        catch (Exception ex)
        {
            Log($"Could not send the report: {ex.Message}");
            return false;
        }
    }

    /// <summary>Presses a key and releases it. The release between keys is what makes repeated letters ("ll") work.</summary>
    private async Task<bool> PressAsync(GattSubscribedClient client, byte usage, byte modifiers)
    {
        var down = await NotifyAsync(keyboard, HidReports.KeyDown(usage, modifiers), client);
        await Task.Delay(30);
        var up = await NotifyAsync(keyboard, HidReports.KeysUp(), client);
        await Task.Delay(30);
        return down && up;
    }

    // ---------------------------------------------------------------- internals

    /// <summary>Builds the HID service. Returns an error message, or null on success. Must be called with the gate held.</summary>
    private async Task<string> CreateProviderAsync()
    {
        var created = await GattServiceProvider.CreateAsync(Uuid(0x1812));
        if (created.Error != BluetoothError.Success)
        {
            return $"cannot create the HID service ({created.Error})";
        }

        var service = created.ServiceProvider.Service;

        async Task<GattLocalCharacteristic> Add(ushort id, GattLocalCharacteristicParameters parameters)
        {
            var result = await service.CreateCharacteristicAsync(Uuid(id), parameters);
            if (result.Error != BluetoothError.Success)
            {
                throw new InvalidOperationException($"characteristic 0x{id:X4}: {result.Error}");
            }

            return result.Characteristic;
        }

        GattLocalCharacteristicParameters Static(byte[] value) => new()
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            StaticValue = value.AsBuffer(),
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
        };

        async Task<GattLocalCharacteristic> InputReport(byte reportId, int size)
        {
            var characteristic = await Add(0x2A4D, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });

            characteristic.ReadRequested += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    Log($"The phone read the {(reportId == HidReports.KeyboardReportId ? "keyboard" : "mouse")} report.");
                    var request = await args.GetRequestAsync();
                    request.RespondWithValue(new byte[size].AsBuffer());
                }
                catch (Exception ex)
                {
                    Log($"Read request failed: {ex.Message}");
                }
                finally
                {
                    deferral.Complete();
                }
            };

            characteristic.SubscribedClientsChanged += (c, _) => _ = RefreshClientsAsync(c);

            var descriptor = await characteristic.CreateDescriptorAsync(Uuid(0x2908), new GattLocalDescriptorParameters
            {
                StaticValue = new byte[] { reportId, 0x01 }.AsBuffer(), // Report Reference: report ID, input report
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            if (descriptor.Error != BluetoothError.Success)
            {
                throw new InvalidOperationException($"descriptor of report {reportId}: {descriptor.Error}");
            }

            return characteristic;
        }

        try
        {
            await Add(0x2A4B, Static(HidReports.ReportMap));
            await Add(0x2A4A, Static(HidReports.HidInformation));

            var control = await Add(0x2A4C, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            control.WriteRequested += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    var request = await args.GetRequestAsync();
                    if (request.Option == GattWriteOption.WriteWithResponse)
                    {
                        request.Respond();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Write request failed: {ex.Message}");
                }
                finally
                {
                    deferral.Complete();
                }
            };

            keyboard = await InputReport(HidReports.KeyboardReportId, HidReports.KeyboardReportSize);
            mouse = await InputReport(HidReports.MouseReportId, HidReports.MouseReportSize);
        }
        catch (Exception ex)
        {
            keyboard = null;
            mouse = null;
            return ex.Message;
        }

        created.ServiceProvider.AdvertisementStatusChanged += (_, e) =>
            Log($"Advertising status: {e.Status}{(e.Error == BluetoothError.Success ? string.Empty : $" ({e.Error})")}");

        provider = created.ServiceProvider;
        return null;
    }

    private void StopAdvertising()
    {
        if (!advertising || provider == null)
        {
            return;
        }

        try
        {
            provider.StopAdvertising();
        }
        catch (Exception ex)
        {
            Log($"Could not stop advertising: {ex.Message}");
        }

        advertising = false;
        Log("Stopped advertising.");
    }

    /// <summary>Works out which device each subscribed phone is, then updates the device states.</summary>
    private async Task RefreshClientsAsync(GattLocalCharacteristic characteristic)
    {
        try
        {
            var clients = new Dictionary<string, GattSubscribedClient>();
            foreach (var client in characteristic.SubscribedClients.ToList())
            {
                var (address, name) = await ResolveAsync(client.Session.DeviceId.Id);
                var match = HidClientMatcher.Match(bluetooth.Devices, address, name);
                var key = match.Key;
                clients[key] = client;

                if (match.ByName)
                {
                    Log($"\"{name}\" connected with a random address, so it was matched to the device with the same name.");
                }

                if (!match.IsKnown)
                {
                    bool firstTime;
                    lock (sync)
                    {
                        firstTime = announcedUnknown.Add(key);
                    }

                    if (firstTime)
                    {
                        UnknownClientConnected?.Invoke(this, new UnknownHidClientEventArgs(key, name, address));
                    }
                }
            }

            Log($"{(ReferenceEquals(characteristic, keyboard) ? "Keyboard" : "Mouse")} report: {clients.Count} phone(s) subscribed.");

            lock (sync)
            {
                if (ReferenceEquals(characteristic, keyboard))
                {
                    keyboardClients = clients;
                }
                else if (ReferenceEquals(characteristic, mouse))
                {
                    mouseClients = clients;
                }
            }

            string[] keys;
            lock (sync)
            {
                keys = enabled.ToArray();
            }

            foreach (var key in keys)
            {
                RaiseState(key);
            }
        }
        catch (Exception ex)
        {
            Log($"Could not read the connected phones: {ex.Message}");
        }
    }

    /// <summary>
    /// Diagnostics only: logs when the Bluetooth LE link to the phone goes up or down. Shows whether the phone connects
    /// at all, which tells "the phone never connects" apart from "it connects but does not subscribe".
    /// </summary>
    private async Task WatchLinkAsync(string key)
    {
        try
        {
            var address = bluetooth.Devices.FirstOrDefault(d => d.Key == key)?.Address
                ?? HidClientMatcher.AddressOfUnknownKey(key);
            if (address == null)
            {
                Log("The Bluetooth address of this device is unknown, so its link cannot be watched.");
                return;
            }

            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(address, 16));
            if (device == null)
            {
                Log("Windows has no Bluetooth LE record for this device yet.");
                return;
            }

            device.ConnectionStatusChanged += (d, _) => Log($"{d.Name}: Bluetooth LE link {d.ConnectionStatus}.");

            lock (sync)
            {
                if (!enabled.Contains(key))
                {
                    device.Dispose();
                    return;
                }

                links[key] = device;
            }

            Log($"{device.Name}: Bluetooth LE link is {device.ConnectionStatus} right now.");
        }
        catch (Exception ex)
        {
            Log($"Could not watch the Bluetooth link: {ex.Message}");
        }
    }

    private void StopWatchingLink(string key)
    {
        BluetoothLEDevice device;
        lock (sync)
        {
            links.Remove(key, out device);
        }

        device?.Dispose();
    }

    /// <summary>Looks up the Bluetooth address and name of a connected LE client.</summary>
    private async Task<(string Address, string Name)> ResolveAsync(string deviceId)
    {
        lock (sync)
        {
            if (clientCache.TryGetValue(deviceId, out var cached))
            {
                return cached;
            }
        }

        var result = (Address: deviceId, Name: "Unknown phone");
        using var device = await BluetoothLEDevice.FromIdAsync(deviceId);
        if (device != null)
        {
            result = (BluetoothAddress.FromUInt64(device.BluetoothAddress), device.Name);
        }

        lock (sync)
        {
            clientCache[deviceId] = result;
        }

        return result;
    }

    /// <summary>Raises the state of a device if it changed since the last time.</summary>
    private void RaiseState(string key)
    {
        HidState state;
        lock (sync)
        {
            if (!enabled.Contains(key))
            {
                return;
            }

            state = keyboardClients.ContainsKey(key) ? HidState.Active : HidState.Waiting;
            if (lastStates.TryGetValue(key, out var last) && last == state)
            {
                return;
            }

            lastStates[key] = state;
        }

        Log(state == HidState.Active ? "A phone connected over HID." : "Waiting for the phone to connect over HID.");
        StateChanged?.Invoke(this, new HidStateChangedEventArgs(key, state));
    }

    private GattSubscribedClient ClientFor(string key, Dictionary<string, GattSubscribedClient> clients)
    {
        lock (sync)
        {
            return enabled.Contains(key) && clients.TryGetValue(key, out var client) ? client : null;
        }
    }

    private async Task<bool> NotifyAsync(GattLocalCharacteristic characteristic, byte[] report, GattSubscribedClient client)
    {
        var result = await characteristic.NotifyValueAsync(report.AsBuffer(), client);
        if (result.Status != GattCommunicationStatus.Success)
        {
            Log($"Sending failed: {result.Status}");
            return false;
        }

        return true;
    }

    private static Guid Uuid(ushort id) => Guid.Parse($"0000{id:x4}-0000-1000-8000-00805f9b34fb");

    private void Log(string message) => LogMessage?.Invoke(this, message);
}
#endif
