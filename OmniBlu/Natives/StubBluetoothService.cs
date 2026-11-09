#if LINUX
using Microsoft.Extensions.Hosting;
using OmniBlu.Managers;
using OmniBlu.Models;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. It finds no devices and refuses every operation.</summary>
internal sealed class StubBluetoothService : IBluetoothService, IHostedService
{
    public IReadOnlyList<SmartDeviceInfo> Devices => Array.Empty<SmartDeviceInfo>();

    public event EventHandler DevicesChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<DeviceStateChangedEventArgs> StateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<string> LogMessage
    {
        add { }
        remove { }
    }

    public Task<bool> ConnectAsync(string key) => Task.FromResult(false);

    public Task DisconnectAsync(string key) => Task.CompletedTask;

    public Task<bool> OpenAudioAsync(string key) => Task.FromResult(false);

    public Task CloseAudioAsync(string key) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
#endif
