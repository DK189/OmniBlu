#if WINDOWS
using Microsoft.Win32;
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Windows implementation of <see cref="IInstallState"/>: a value in <c>HKEY_CURRENT_USER\Software\OmniBlu</c>.</summary>
internal sealed class WindowsInstallState : IInstallState
{
    private const string KeyPath = @"Software\OmniBlu";
    private const string ValueName = "ApprovedExePath";

    public string ApprovedExePath
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(ValueName) as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }
    }

    public bool SetApprovedExePath(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue(ValueName, path, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
#endif
