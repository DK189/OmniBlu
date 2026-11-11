using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmniBlu.Managers;
using OmniBlu.Natives;

namespace OmniBlu;

sealed class Program
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(6);

    /// <summary>The name that makes the app run only once for each user.</summary>
    private const string InstanceName = "OmniBlu.SingleInstance";

    public static IHost HostApp { get; private set; }

    /// <summary>Ownership of the single running copy. Used to hear a second copy that asks for the window.</summary>
    public static SingleInstance Instance { get; private set; }

    // Entry point. Do not use Avalonia, third-party APIs or anything that depends on
    // SynchronizationContext before the host is built: they are not initialized yet.
    [STAThread]
    public static void Main(string[] args)
    {
        // Only one copy runs. A second copy asks the first one to show its window and exits. A copy that Windows started
        // in the tray (--tray) must stay quiet, so it does not ask.
        Instance = SingleInstance.TryAcquire(InstanceName);
        if (Instance == null)
        {
            if (!StartupCommand.StartsInTray(args))
            {
                SingleInstance.SignalExisting(InstanceName, 2000);
            }

            Console.WriteLine("OmniBlu is already running. Its window was asked to show.");
            return;
        }

        try
        {
            Console.WriteLine("OmniBlu is starting...");

            Build(args);

            var fileLog = HostApp.Services.GetRequiredService<FileLog>();
            Console.WriteLine($"Log file: {fileLog.FilePath}");

            // Write crashes to the log file too, so they can be read after the app is gone.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                fileLog.Write("Unhandled exception (the app is closing).", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, e) =>
                fileLog.Write("Unobserved task exception.", e.Exception);

            var lifetime = HostApp.Services.GetRequiredService<IHostApplicationLifetime>();
            var desktopStopped = false;

            // If the host stops first (for example Ctrl+C), close the desktop UI too.
            lifetime.ApplicationStopping.Register(() =>
            {
                if (desktopStopped)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                });
            });

            HostApp.Start();
            HostApp.Services.GetRequiredService<AppBuilder>().StartWithClassicDesktopLifetime(args);
            desktopStopped = true;

            // Stop the host off the UI thread: the UI thread still has its SynchronizationContext,
            // so waiting for the host on it could deadlock.
            lifetime.StopApplication();
            var stop = Task.Run(() => HostApp.StopAsync(ShutdownTimeout));
            if (!stop.Wait(ShutdownTimeout + TimeSpan.FromSeconds(2)))
            {
                Console.Error.WriteLine("OmniBlu did not stop in time. Exiting anyway.");
                fileLog.Write("OmniBlu did not stop in time. Exiting anyway.");
            }

            fileLog.Write("OmniBlu stopped.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            HostApp?.Services.GetService<FileLog>()?.Write("Fatal error in Main.", ex);
        }
        finally
        {
            Instance.Dispose();
        }
    }

    // Avalonia configuration. Do not remove: the visual designer also uses it.
    public static AppBuilder BuildAvaloniaApp() => Build().Services.GetRequiredService<AppBuilder>();

    private static IHost Build(params string[] args)
    {
        if (HostApp != null)
        {
            return HostApp;
        }

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddAvaloniaApp();
        builder.Services.AddNativeServices();
        builder.Services.AddManagers();
        builder.Services.AddViewModels();
        builder.Services.AddWindows();

        return HostApp = builder.Build();
    }
}
