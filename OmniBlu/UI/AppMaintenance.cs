using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using OmniBlu.Managers;
using OmniBlu.ViewModels;

namespace OmniBlu.UI;

/// <summary>
/// The checks of a release copy. Development runs (built from source) do none of them. There are two steps:
/// <list type="number">
/// <item><see cref="RunLocationStepAsync"/> runs before any window of the app is shown. It looks at the fixed folder for all
/// users (for example C:\ProgramData\OmniBlu): when a copy is installed there, the newer one wins without a question; when
/// there is none, the user must choose to move there or to keep this place.</item>
/// <item><see cref="RunUpdateStepAsync"/> runs a little after the window is shown. It looks for a newer version on GitHub.</item>
/// </list>
/// A question is never shown while control mode runs, because the window would take the keyboard focus and end it.
/// </summary>
internal sealed class AppMaintenance
{
    private const int UpdateDelayMs = 2000;

    private readonly SettingsManager settings;
    private readonly IInstallState installState;
    private readonly IAutoStart autoStart;
    private readonly MainViewModel viewModel;
    private readonly Func<bool> isControlling;
    private readonly bool startInTray;
    private readonly string exe = Environment.ProcessPath;
    private readonly bool isReleaseRun;

    /// <param name="startInTray">True when this copy was started in the tray. A silent restart then starts in the tray too.</param>
    public AppMaintenance(
        SettingsManager settings, IInstallState installState, IAutoStart autoStart, MainViewModel viewModel, Func<bool> isControlling, bool startInTray)
    {
        this.settings = settings;
        this.installState = installState;
        this.autoStart = autoStart;
        this.viewModel = viewModel;
        this.isControlling = isControlling;
        this.startInTray = startInTray;

        var assembly = typeof(Program).Assembly;
        var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        isReleaseRun = !string.IsNullOrEmpty(exe) && OperatingSystem.IsWindows() && InstallLocation.IsReleaseRun(assembly.Location, configuration);
    }

    // ------------------------------------------------------------------ step 1: place of the program

    /// <summary>
    /// Runs before any window is shown. Returns true when this copy is handing over to another file and is ending: the caller
    /// must not show a window then. It never throws.
    /// </summary>
    public async Task<bool> RunLocationStepAsync()
    {
        try
        {
            if (!isReleaseRun)
            {
                return false;
            }

            // A copy that an update or a move started removes the files that were left next to it.
            ExeSwapper.CleanUp(exe);

            var folder = InstallLocation.DefaultGlobalFolder;
            var fixedExe = InstallLocation.GlobalExePath(folder);
            var installed = File.Exists(fixedExe);
            var plan = InstallLocation.PlanStartup(
                true, exe, installState.ApprovedExePath, folder, installed, installed ? InstallLocation.ReadFileVersion(fixedExe) : null, AppVersion.Current);

            switch (plan)
            {
                case StartupPlan.RunFixed:
                    viewModel.AddLog($"OmniBlu is already installed in {folder} (the same or a newer version). Starting that copy.");
                    if (HandOver(fixedExe))
                    {
                        return true;
                    }

                    break;

                case StartupPlan.ReplaceThenRunFixed:
                    if (ReplaceInstalledCopy(fixedExe, folder) && HandOver(fixedExe))
                    {
                        return true;
                    }

                    break;
            }

            // Nothing is installed (or the silent way did not work): the user chooses, unless there is nothing to ask.
            return plan is StartupPlan.Ask or StartupPlan.RunFixed or StartupPlan.ReplaceThenRunFixed
                && await AskAsync(folder);
        }
        catch (Exception ex)
        {
            viewModel.AddLog($"The check of the program folder failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>This copy is newer than the installed one: copy it over the installed one. Nothing is asked, because the user installed it before.</summary>
    private bool ReplaceInstalledCopy(string fixedExe, string folder)
    {
        try
        {
            var from = InstallLocation.ReadFileVersion(fixedExe);
            InstallLocation.CopyToFolder(exe, folder);
            viewModel.AddLog($"This copy ({AppVersion.Current}) is newer than the installed one ({from}) in {folder}. It replaced the installed copy.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.AddLog($"The installed copy in {folder} could not be replaced (it may be in use): {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts the installed copy and ends this one. Returns false when it could not be started.</summary>
    private bool HandOver(string fixedExe)
    {
        try
        {
            ProcessRestart.StartAndWait(fixedExe, startInTray);
            Quit();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            viewModel.AddLog($"The installed copy could not be started: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Nothing is installed in the fixed folder. The user must choose: there is no "later" and the window cannot be closed.
    /// If moving fails, the question comes again. Returns true when the program was moved and is restarting.
    /// </summary>
    private async Task<bool> AskAsync(string folder)
    {
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
                if (await MoveAsync(folder))
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

    private async Task<bool> MoveAsync(string folder)
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
            viewModel.AddLog($"Moving OmniBlu failed: {ex.GetType().Name}: {ex.Message}");
            await ChoiceDialog.AskAsync("OmniBlu was not moved", $"{ex.Message}\n\nYou can try again or keep running from this place.", new[] { "OK" }, closeIndex: 0);
            return false;
        }
    }

    // ------------------------------------------------------------------ step 2: updates

    /// <summary>Looks for a newer version on GitHub, a little after the window is shown. It never throws.</summary>
    public async Task RunUpdateStepAsync()
    {
        try
        {
            if (!isReleaseRun)
            {
                return;
            }

            await Task.Delay(UpdateDelayMs);
            await CheckUpdateAsync();
        }
        catch (Exception ex)
        {
            viewModel.AddLog($"The check for updates failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task CheckUpdateAsync()
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

        await InstallAsync(service, release, current);
    }

    private async Task InstallAsync(UpdateService service, ReleaseInfo release, string current)
    {
        var busy = ChoiceDialog.ShowBusy($"Updating to {release.Version}", "Starting...");
        try
        {
            // Everything the restart needs is loaded before the program file is replaced (see ProcessRestart.Prepare).
            ProcessRestart.Prepare();
            await service.InstallAsync(release, exe, current, new Progress<string>(busy.SetMessage));
        }
        catch (Exception ex)
        {
            busy.FinishBusy();
            viewModel.AddLog($"The update failed: {ex.GetType().Name}: {ex.Message}");
            await ChoiceDialog.AskAsync("The update failed", $"{ex.Message}\n\nOmniBlu was not changed and keeps running.", new[] { "OK" }, closeIndex: 0);
            return;
        }

        busy.FinishBusy();
        viewModel.AddLog($"OmniBlu {release.Version} was installed. Restarting.");
        try
        {
            ProcessRestart.StartAndWait(exe);
            Quit();
        }
        catch (Exception ex)
        {
            // The new file is already in place, so this is not a failed update: only the restart did not work.
            viewModel.AddLog($"The new version could not be started: {ex.GetType().Name}: {ex.Message}");
            await ChoiceDialog.AskAsync("Please start OmniBlu again", $"OmniBlu {release.Version} was installed, but OmniBlu could not start it: {ex.Message}\n\nClose OmniBlu and start it again.", new[] { "OK" }, closeIndex: 0);
        }
    }

    // ------------------------------------------------------------------ a newer copy was opened

    private bool offering;

    /// <summary>
    /// Another copy of the app was started by hand while this one runs, and it told us where its file is. If that file is an
    /// OmniBlu program with a newer version (read from the file, not taken from the message), ask the user to replace this
    /// program with it: the file is copied over this program, and OmniBlu restarts. Nothing is downloaded. Call it on the UI
    /// thread. It never throws.
    /// </summary>
    public async Task OfferNewerCopyAsync(OpenRequest request)
    {
        if (offering)
        {
            return;
        }

        try
        {
            var fileVersion = InstallLocation.ReadFileVersion(request.Path);
            var current = AppVersion.Current;
            if (!InstallLocation.ShouldOfferReplacement(
                    isReleaseRun, exe, request.Path, current, fileVersion, InstallLocation.ReadProductName(request.Path)))
            {
                return;
            }

            offering = true;
            await WaitWhileControllingAsync();
            var shown = AppVersion.TryParse(fileVersion, out var version) ? version.ToString() : fileVersion;
            var choice = await ChoiceDialog.AskAsync(
                "A newer OmniBlu was opened",
                $"You opened OmniBlu {shown} from:\n{request.Path}\n\nThis OmniBlu is {current}, and it is already running.\n\n"
                + "\"Replace and restart\" copies that file over this program (nothing is downloaded) and starts it. "
                + "\"Not now\" keeps this version running.",
                new[] { "Replace and restart", "Not now" },
                closeIndex: 1);

            if (choice == 0)
            {
                await ReplaceFromFileAsync(request.Path, shown);
            }
        }
        catch (Exception ex)
        {
            viewModel.AddLog($"Offering the newer copy failed: {ex}");
        }
        finally
        {
            offering = false;
        }
    }

    private async Task ReplaceFromFileAsync(string source, string shown)
    {
        var folder = Path.GetDirectoryName(exe);
        var staged = ExeSwapper.NewPath(exe);
        try
        {
            if (!ExeSwapper.CanWrite(folder))
            {
                throw new IOException($"OmniBlu cannot write to the folder {folder}. Close OmniBlu and copy the file by hand.");
            }

            // Same steps as an update, with a local file instead of a download: stage next to the program, then swap. Everything
            // the restart needs is loaded first, because the program file is replaced under this running copy.
            ProcessRestart.Prepare();
            File.Copy(source, staged, overwrite: true);
            ExeSwapper.Swap(exe, staged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(staged);
            }
            catch (Exception cleanUp) when (cleanUp is IOException or UnauthorizedAccessException)
            {
                // Only cleaning.
            }

            viewModel.AddLog($"Replacing OmniBlu with {source} failed: {ex.GetType().Name}: {ex.Message}");
            await ChoiceDialog.AskAsync("OmniBlu was not replaced", $"{ex.Message}\n\nThis version keeps running.", new[] { "OK" }, closeIndex: 0);
            return;
        }

        viewModel.AddLog($"OmniBlu was replaced with version {shown} from {source}. Restarting.");
        try
        {
            ProcessRestart.StartAndWait(exe);
            Quit();
        }
        catch (Exception ex)
        {
            viewModel.AddLog($"The new version could not be started: {ex.GetType().Name}: {ex.Message}");
            await ChoiceDialog.AskAsync("Please start OmniBlu again", $"The file was replaced, but OmniBlu could not start it: {ex.Message}\n\nClose OmniBlu and start it again.", new[] { "OK" }, closeIndex: 0);
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
