namespace OmniBlu.Managers;

/// <summary>
/// Remembers, for the current user, which program file the user has confirmed to run from (when it is not in the fixed
/// folder for all users). It is kept outside the settings file, in the registry on Windows. It is per user because a
/// machine-wide value would need administrator rights.
/// </summary>
public interface IInstallState
{
    /// <summary>The program file the user confirmed, or null when there is none or it cannot be read.</summary>
    string ApprovedExePath { get; }

    /// <summary>Saves the confirmed program file. Returns false when it could not be saved.</summary>
    bool SetApprovedExePath(string path);
}
