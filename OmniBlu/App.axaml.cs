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

            var startInTray = StartupCommand.StartsInTray(desktop.Args);
            var services = Program.HostApp.Services;
            var maintenance = new AppMaintenance(
                services.GetRequiredService<SettingsManager>(),
                services.GetRequiredService<IInstallState>(),
                services.GetRequiredService<IAutoStart>(),
                services.GetRequiredService<MainViewModel>(),
                () => mainWindow.IsControlling,
                startInTray);

            // Nothing is shown at first, because a release copy may have to ask about its place (or hand over to the copy that is
            // installed). The main window of the lifetime is therefore not set here: Avalonia would show it at once.
            var ready = false;

            // A second copy of the app asks this one to show its window. It also says which version and file it has: if it is
            // newer, this copy offers to replace itself with it (the user opened it by hand, so a question is the right answer).
            Program.Instance?.StartListening(text => Dispatcher.UIThread.Post(() =>
            {
                if (!ready)
                {
                    return;
                }

                mainWindow.ShowFromTray();
                var request = InstanceMessage.Parse(text);
                if (request != null)
                {
                    _ = maintenance.OfferNewerCopyAsync(request);
                }
            }));

            Dispatcher.UIThread.Post(async () =>
            {
                // 1. Before any window: the place of the program. This may end this copy and start another file.
                if (await maintenance.RunLocationStepAsync())
                {
                    return;
                }

                // 2. Now the app shows itself, or stays in the tray.
                ready = true;
                if (startInTray)
                {
                    mainWindow.PrepareHidden();
                }
                else
                {
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                }

                AddTrayIcon(desktop, mainWindow);

                // 3. A little later: a newer version on GitHub.
                await maintenance.RunUpdateStepAsync();
            });
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
