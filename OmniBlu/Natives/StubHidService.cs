#if LINUX
using Microsoft.Extensions.Hosting;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. It never advertises and refuses every operation.</summary>
internal sealed class StubHidService : IHidService, IHostedService
{
    public event EventHandler<HidStateChangedEventArgs> StateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected
    {
        add { }
        remove { }
    }

    public event EventHandler<string> LogMessage
    {
        add { }
        remove { }
    }

    public Task<bool> EnableAsync(string key) => Task.FromResult(false);

    public Task DisableAsync(string key) => Task.CompletedTask;

    public Task<bool> SendClickAsync(string key) => Task.FromResult(false);

    public Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0) => Task.FromResult(false);

    public Task<int> SendTextAsync(string key, string text) => Task.FromResult(0);

    public Task<bool> SendKeyboardReportAsync(string key, byte[] report) => Task.FromResult(false);

    public Task<bool> SendMouseReportAsync(string key, byte[] report) => Task.FromResult(false);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
#endif
