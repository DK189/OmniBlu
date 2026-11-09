using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OmniBlu.Managers;
using OmniBlu.UI;
using OmniBlu.ViewModels;

namespace OmniBlu;

internal static class Extensions
{
    /// <summary>Registers the Avalonia application and its <see cref="AppBuilder"/>.</summary>
    public static IServiceCollection AddAvaloniaApp(this IServiceCollection services)
    {
        services.AddSingleton<App>();
        services.AddSingleton(
            s => AppBuilder.Configure(() => s.GetRequiredService<App>())
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
        );

        return services;
    }

    public static IServiceCollection AddManagers(this IServiceCollection services)
    {
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<FileLog>();

        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();

        return services;
    }

    public static IServiceCollection AddWindows(this IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();

        return services;
    }
}
