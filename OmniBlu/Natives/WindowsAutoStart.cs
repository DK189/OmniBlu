#if WINDOWS
using Microsoft.Win32;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>
/// Windows implementation of <see cref="IAutoStart"/>. It uses a value in the "Run" key of the current user, so no
/// administrator rights are needed. The value holds the full path of the program that is running now.
/// </summary>
internal sealed class WindowsAutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string valueName;

    public WindowsAutoStart()
        : this("OmniBlu")
    {
    }

    /// <summary>For tests: a different <paramref name="valueName"/> keeps the real entry untouched.</summary>
    public WindowsAutoStart(string valueName)
    {
        this.valueName = valueName;
    }

    public bool IsSupported => true;

    public string LastError { get; private set; }

    public StartupMode Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return StartupCommand.Parse(key?.GetValue(valueName) as string);
        }
        catch (Exception ex)
        {
            LastError = $"Reading the start-up setting failed: {ex.Message}";
            return StartupMode.Off;
        }
    }

    public bool Apply(StartupMode mode, string exePath = null)
    {
        try
        {
            var path = exePath ?? Environment.ProcessPath;
            if (mode != StartupMode.Off && string.IsNullOrEmpty(path))
            {
                LastError = "The path of the program is not known.";
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            var command = StartupCommand.Build(path, mode);
            if (command == null)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(valueName, command, RegistryValueKind.String);
            }

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Saving the start-up setting failed: {ex.Message}";
            return false;
        }
    }
}
#endif
