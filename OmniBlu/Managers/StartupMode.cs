namespace OmniBlu.Managers;

/// <summary>What OmniBlu does when Windows starts and the user signs in.</summary>
public enum StartupMode
{
    /// <summary>OmniBlu does not start with Windows.</summary>
    Off = 0,

    /// <summary>OmniBlu starts and shows its window.</summary>
    Window = 1,

    /// <summary>OmniBlu starts with no window. Only the tray icon and the control shortcut are there.</summary>
    Tray = 2,
}

/// <summary>Builds and reads the command line that Windows runs at sign-in.</summary>
public static class StartupCommand
{
    /// <summary>The argument that makes OmniBlu start in the tray, with no window.</summary>
    public const string TrayArgument = "--tray";

    /// <summary>The command line for a mode, or null for <see cref="StartupMode.Off"/> (nothing to run).</summary>
    public static string Build(string exePath, StartupMode mode)
    {
        var quoted = $"\"{exePath}\"";
        return mode switch
        {
            StartupMode.Window => quoted,
            StartupMode.Tray => $"{quoted} {TrayArgument}",
            _ => null,
        };
    }

    /// <summary>Tells the mode from a saved command line. An empty command means <see cref="StartupMode.Off"/>.</summary>
    public static StartupMode Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return StartupMode.Off;
        }

        return command.TrimEnd().EndsWith(" " + TrayArgument, StringComparison.OrdinalIgnoreCase)
            ? StartupMode.Tray
            : StartupMode.Window;
    }

    /// <summary>True when the program arguments ask for a start in the tray.</summary>
    public static bool StartsInTray(IEnumerable<string> args) =>
        args != null && args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
}
