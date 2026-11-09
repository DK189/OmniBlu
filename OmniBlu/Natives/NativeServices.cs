using Microsoft.Extensions.DependencyInjection;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>The only place that picks a platform implementation. Keep every <c>#if</c> inside <c>Natives/</c>.</summary>
internal static class NativeServices
{
    public static IServiceCollection AddNativeServices(this IServiceCollection services)
    {
#if WINDOWS
        services.AddSingleton<WindowsBluetoothService>();
        services.AddSingleton<IBluetoothService>(s => s.GetRequiredService<WindowsBluetoothService>());
        services.AddHostedService(s => s.GetRequiredService<WindowsBluetoothService>());

        services.AddSingleton<WindowsHidService>();
        services.AddSingleton<IHidService>(s => s.GetRequiredService<WindowsHidService>());
        services.AddHostedService(s => s.GetRequiredService<WindowsHidService>());

        services.AddSingleton<ICursorLock, WindowsCursorLock>();
        services.AddSingleton<IRawMouse, WindowsRawMouse>();
        services.AddSingleton<IGlobalHotkey, WindowsGlobalHotkey>();
        services.AddSingleton<IKeyboardBlocker, WindowsKeyboardBlocker>();
        services.AddSingleton<IAutoStart, WindowsAutoStart>();
#else
        services.AddSingleton<StubBluetoothService>();
        services.AddSingleton<IBluetoothService>(s => s.GetRequiredService<StubBluetoothService>());
        services.AddHostedService(s => s.GetRequiredService<StubBluetoothService>());

        services.AddSingleton<StubHidService>();
        services.AddSingleton<IHidService>(s => s.GetRequiredService<StubHidService>());
        services.AddHostedService(s => s.GetRequiredService<StubHidService>());

        services.AddSingleton<ICursorLock, StubCursorLock>();
        services.AddSingleton<IRawMouse, StubRawMouse>();
        services.AddSingleton<IGlobalHotkey, StubGlobalHotkey>();
        services.AddSingleton<IKeyboardBlocker, StubKeyboardBlocker>();
        services.AddSingleton<IAutoStart, StubAutoStart>();
#endif

        return services;
    }
}
