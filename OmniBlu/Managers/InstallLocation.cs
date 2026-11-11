namespace OmniBlu.Managers;

/// <summary>
/// Where the program is installed. A release copy is a single file; it works best from one fixed folder for all users
/// (for example C:\ProgramData\OmniBlu). This class decides whether to ask the user about it, and copies the file there.
/// </summary>
public static class InstallLocation
{
    /// <summary>The fixed folder for all users, for example C:\ProgramData\OmniBlu.</summary>
    public static string DefaultGlobalFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OmniBlu");

    /// <summary>
    /// True for a release copy: one single file made by "dotnet publish" (the assembly has no file location then) that is not a
    /// Debug build. Development runs (built from source) are never moved and never updated.
    /// </summary>
    public static bool IsReleaseRun(string assemblyLocation, string configuration) =>
        string.IsNullOrEmpty(assemblyLocation) && !string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the program file is directly inside the folder (the comparison ignores case).</summary>
    public static bool IsInFolder(string exePath, string folder) =>
        SamePath(Path.GetDirectoryName(Path.GetFullPath(exePath)), folder);

    /// <summary>True when both paths name the same place (the comparison ignores case and a closing slash).</summary>
    public static bool SamePath(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        static string Clean(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Clean(first), Clean(second), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the user must be asked: a release copy that is not in the fixed folder and that the user has not confirmed yet.
    /// <paramref name="approvedExePath"/> is the file the user confirmed earlier (or null).
    /// </summary>
    public static bool NeedsPrompt(bool isReleaseRun, string exePath, string approvedExePath, string globalFolder) =>
        isReleaseRun && !IsInFolder(exePath, globalFolder) && !SamePath(exePath, approvedExePath);

    /// <summary>
    /// Copies the program into the fixed folder (it is created when it is missing) and returns the new path. The copy is not
    /// started here. An existing file with the same name is replaced; that fails when it is running.
    /// </summary>
    public static string CopyToFolder(string exePath, string globalFolder)
    {
        Directory.CreateDirectory(globalFolder);
        var target = Path.Combine(globalFolder, Path.GetFileName(exePath));
        File.Copy(exePath, target, overwrite: true);
        return target;
    }
}
