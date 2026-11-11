using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using OmniBlu.Managers;
using OmniBlu.ViewModels;

namespace OmniBlu.UI;

/// <summary>
/// The checks that run a few seconds after a release copy starts: is the program in the fixed folder for all users (and if
/// not, move it there or remember the choice), and is there a newer version on GitHub. Development runs (built from source)
/// do neither. A question is never shown while control mode runs, because the window would take the keyboard focus and end it.
/// </summary>
internal sealed class AppMaintenance
{
    private const int StartDelayMs = 3000;

    private readonly SettingsManager settings;
    private readonly IInstallState installState;
    private readonly IAutoStart autoStart;
    private readonly MainViewModel viewModel;
    private readonly Func<bool> isControlling;

    public AppMaintenance(SettingsManager settings, IInstallState installState, IAutoStart autoStart, MainViewModel viewModel, Func<bool> isControlling)
    {
        this.settings = settings;
        this.installState = installState;
        this.autoStart = autoStart;
        this.viewModel = viewModel;
        this.isControlling = isControlling;
    }

    /// <summary>Runs the checks. Call it on the UI thread. It never throws.</summary>
    public async Task RunAsync()
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
            var exe = Environment.ProcessPath;
            if (!InstallLocation.IsReleaseRun(assembly.Location, configuration) || string.IsNullOrEmpty(exe) || !OperatingSystem.IsWindows())
            {
                return;
            }

            // A copy that an update or a move started removes the files that were left next to it.
            ExeSwapper.CleanUp(exe);

            await Task.Delay(StartDelayMs);
            if (await CheckLocationAsync(exe))
            {
                return; // restarting from the new place
            }

            await CheckUpdateAsync(exe);
        }
        catch (Exception ex)
        {
            viewModel.AddLog($"The start-up checks failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ place of the program

    /// <summary>Asks about the fixed folder when needed. Returns true when the program was moved and is restarting.</summary>
    private async Task<bool> CheckLocationAsync(string exe)
    {
        var folder = InstallLocation.DefaultGlobalFolder;
        if (!InstallLocation.NeedsPrompt(true, exe, installState.ApprovedExePath, folder))
        {
            return false;
        }

        await WaitWhileControllingAsync();

        // The user must choose: there is no "later" and the window cannot be closed. If moving fails, the question is asked again.
        while (true)
        {
            var choice = await ChoiceDialog.AskAsync(
                "Move OmniBlu to a fixed folder?",
                $"OmniBlu is running from:\n{Path.GetDirectoryName(exe)}\n\n"
                + "It works best from one fixed folder for all users of this PC, because updates and \"Start with Windows\" "
                + $"always find the program there:\n{folder}\n\n"
                + "\"Move\" copies the program there and starts it from there. The file here is not deleted. "
                + "\"Keep here\" runs it from this place, and you are not asked again for this file.",
                new[] { "Move", "Keep here" },
                closeIndex: -1,
                mustChoose: true);

            if (choice < 0)
            {
                return true; // the app is ending and closed the window: nothing was chosen, and nothing is saved
            }

            if (choice == 0)
            {
                if (await MoveAsync(exe, folder))
                {
                    return true;
                }

                continue; // moving failed: ask again
            }

            viewModel.AddLog(installState.SetApprovedExePath(exe)
                ? $"OmniBlu will keep running from {exe}. You are not asked about it again for this file."
                : "Your choice could not be saved, so you are asked again at the next start.");
            return false;
        }
    }

    private async Task<bool> MoveAsync(string exe, string folder)
    {
        try
        {
            var target = InstallLocation.CopyToFolder(exe, folder);
            installState.SetApprovedExePath(target);

            // "Start with Windows" must point to the new file, or it would start the old one.
            if (autoStart.IsSupported)
            {
                var mode = autoStart.Read();
                if (mode != StartupMode.Off && !autoStart.Apply(mode, target))
                {
                    viewModel.AddLog($"The start-up entry could not be moved: {autoStart.LastError}");
                }
            }

            viewModel.AddLog($"OmniBlu was copied to {target}. Starting it from there.");
            ProcessRestart.StartAndWait(target);
            Quit();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            viewModel.AddLog($"Moving OmniBlu failed: {ex.Message}");
            await ChoiceDialog.AskAsync("OmniBlu was not moved", $"{ex.Message}\n\nYou can try again or keep running from this place.", new[] { "OK" }, closeIndex: 0);
            return false;
        }
    }

    // ------------------------------------------------------------------ updates

    private async Task CheckUpdateAsync(string exe)
    {
        if (!settings.CheckForUpdates)
        {
            viewModel.AddLog("Checking for updates is off.");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var service = new UpdateService(http);
        var current = AppVersion.Current;
        var result = await service.CheckAsync(current);
        viewModel.AddLog(result.Message);
        if (result.Status != UpdateStatus.Available)
        {
            return;
        }

        await WaitWhileControllingAsync();
        var release = result.Release;
        var choice = await ChoiceDialog.AskAsync(
            "A new version is available",
            $"OmniBlu {release.Version} is available. You are using {current}.\n\n"
            + "\"Update now\" downloads it and restarts OmniBlu. \"Later\" asks again the next time OmniBlu starts. "
            + "You can also turn off the check here, and turn it on again in Settings.",
            new[] { "Update now", "Later", "Turn off updates" },
            closeIndex: 1);

        if (choice == 2)
        {
            viewModel.CheckForUpdates = false;
            return;
        }

        if (choice != 0)
        {
            viewModel.AddLog("Update postponed. It is offered again at the next start.");
            return;
        }

        await InstallAsync(service, release, exe, current);
    }

    private async Task InstallAsync(UpdateService service, ReleaseInfo release, string exe, string current)
    {
        var busy = ChoiceDialog.ShowBusy($"Updating to {release.Version}", "Starting...");
        try
        {
            await service.InstallAsync(release, exe, current, new Progress<string>(busy.SetMessage));
            busy.FinishBusy();
            viewModel.AddLog($"OmniBlu {release.Version} was installed. Restarting.");
            ProcessRestart.StartAndWait(exe);
            Quit();
        }
        catch (Exception ex)
        {
            busy.FinishBusy();
            viewModel.AddLog($"The update failed: {ex.Message}");
            await ChoiceDialog.AskAsync("The update failed", $"{ex.Message}\n\nOmniBlu was not changed and keeps running.", new[] { "OK" }, closeIndex: 0);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task WaitWhileControllingAsync()
    {
        while (isControlling())
        {
            await Task.Delay(1000);
        }
    }

    /// <summary>Ends the app the normal way, so the tray icon, the shortcut and the Bluetooth parts are cleaned up.</summary>
    private static void Quit() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
