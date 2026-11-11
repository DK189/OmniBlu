using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OmniBlu.Managers;
using OmniBlu.UI;
using OmniBlu.ViewModels;

namespace OmniBlu;

internal partial class App : Avalonia.Application
{
    public App()
        : base()
    {
        Name = "OmniBlu";
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The window decides when the app ends. It is closed by "Quit" or Alt+F4, never by "Hide to tray".
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var mainWindow = Program.HostApp.Services.GetRequiredService<MainWindow>();
            mainWindow.Closed += (_, _) => desktop.Shutdown();

            if (StartupCommand.StartsInTray(desktop.Args))
            {
                // Avalonia shows the main window of the lifetime when it starts, so it is not set. The window is created
                // and is shown later (tray icon or shortcut).
                mainWindow.PrepareHidden();
            }
            else
            {
                desktop.MainWindow = mainWindow;
            }

            AddTrayIcon(desktop, mainWindow);

            // Checks that a release copy makes a few seconds after it starts: its place, and a newer version.
            var services = Program.HostApp.Services;
            var maintenance = new AppMaintenance(
                services.GetRequiredService<SettingsManager>(),
                services.GetRequiredService<IInstallState>(),
                services.GetRequiredService<IAutoStart>(),
                services.GetRequiredService<MainViewModel>(),
                () => mainWindow.IsControlling);
            Dispatcher.UIThread.Post(() => _ = maintenance.RunAsync());

            // A second copy of the app asks this one to show its window.
            Program.Instance?.StartListening(() => Dispatcher.UIThread.Post(mainWindow.ShowFromTray));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Adds the icon in the system tray. It has no menu: a click brings the main window back. The icon is removed when
    /// the app exits, so no empty icon stays in the tray.
    /// </summary>
    private void AddTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, MainWindow mainWindow)
    {
        var icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://OmniBlu/Assets/omniblu.png"))),
            ToolTipText = "OmniBlu",
            IsVisible = true,
        };
        icon.Clicked += (_, _) => mainWindow.ShowFromTray();

        TrayIcon.SetIcons(this, new TrayIcons { icon });
        desktop.Exit += (_, _) =>
        {
            icon.IsVisible = false;
            icon.Dispose();
        };
    }
}
