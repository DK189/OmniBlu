namespace OmniBlu.Managers;

/// <summary>What a starting copy does about the fixed folder for all users.</summary>
public enum StartupPlan
{
    /// <summary>Nothing to do: it is a development run, it already runs from the fixed folder, or the user chose to keep it here.</summary>
    Continue,

    /// <summary>There is no copy in the fixed folder: ask the user to move or to keep it here.</summary>
    Ask,

    /// <summary>A copy is installed and it is the same or newer: do not copy, just start that copy and end this one.</summary>
    RunFixed,

    /// <summary>A copy is installed and this one is newer: copy this one over it without asking, start it, and end this one.</summary>
    ReplaceThenRunFixed,
}

/// <summary>
/// Where the program is installed. A release copy is a single file; it works best from one fixed folder for all users
/// (for example C:\ProgramData\OmniBlu). This class decides whether to ask the user about it, and copies the file there.
/// </summary>
public static class InstallLocation
{
    /// <summary>The name of the program file in the fixed folder. It does not depend on the name of the file that was copied.</summary>
    public const string GlobalExeName = "OmniBlu.exe";

    /// <summary>The fixed folder for all users, for example C:\ProgramData\OmniBlu.</summary>
    public static string DefaultGlobalFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OmniBlu");

    /// <summary>The program file in the fixed folder, for example C:\ProgramData\OmniBlu\OmniBlu.exe.</summary>
    public static string GlobalExePath(string globalFolder) => Path.Combine(globalFolder, GlobalExeName);

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
    /// Copies the program into the fixed folder (it is created when it is missing) and returns the new path. The copy is not
    /// started here. An existing file with the same name is replaced; that fails when it is running.
    /// </summary>
    public static string CopyToFolder(string exePath, string globalFolder)
    {
        Directory.CreateDirectory(globalFolder);
        var target = GlobalExePath(globalFolder);
        File.Copy(exePath, target, overwrite: true);
        return target;
    }

    /// <summary>The version stored in a program file ("1.2.3", maybe with build information), or null when it cannot be read.</summary>
    public static string ReadFileVersion(string exePath)
    {
        try
        {
            return File.Exists(exePath) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath).ProductVersion : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The product name stored in a program file, or null when it cannot be read.</summary>
    public static string ReadProductName(string exePath)
    {
        try
        {
            return File.Exists(exePath) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath).ProductName : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The product name that every OmniBlu program file carries.</summary>
    public const string ProductName = "OmniBlu";

    /// <summary>
    /// Decides if a running copy should offer to replace itself with a program file that another copy reported. The offer is made
    /// only when the file is a different file, is an OmniBlu program, and its version, read from the file itself, is newer than
    /// the running version. What the other copy said about its version is never used here.
    /// </summary>
    /// <param name="isReleaseRun">See <see cref="IsReleaseRun"/>. A development run never replaces itself.</param>
    /// <param name="currentExe">The program file of the running copy.</param>
    /// <param name="requestedExe">The file that the other copy reported.</param>
    /// <param name="currentVersion">The running version.</param>
    /// <param name="fileVersion">The version read from <paramref name="requestedExe"/>, or null when it could not be read.</param>
    /// <param name="fileProductName">The product name read from <paramref name="requestedExe"/>, or null.</param>
    public static bool ShouldOfferReplacement(
        bool isReleaseRun, string currentExe, string requestedExe, string currentVersion, string fileVersion, string fileProductName) =>
        isReleaseRun
        && !SamePath(currentExe, requestedExe)
        && string.Equals(fileProductName, ProductName, StringComparison.Ordinal)
        && AppVersion.IsNewer(fileVersion, currentVersion);

    /// <summary>
    /// Decides what a starting copy does about the fixed folder (see <see cref="StartupPlan"/>). It does not ask or change anything.
    /// </summary>
    /// <param name="isReleaseRun">See <see cref="IsReleaseRun"/>. A development run never moves.</param>
    /// <param name="exePath">The program file that is running now.</param>
    /// <param name="approvedExePath">The program file the user chose to keep running from, or null.</param>
    /// <param name="globalFolder">The fixed folder.</param>
    /// <param name="globalVersion">The version of the program file in the fixed folder, or null when there is none or it is unreadable.</param>
    /// <param name="globalExists">True when the program file exists in the fixed folder.</param>
    /// <param name="currentVersion">The version that is running now.</param>
    public static StartupPlan PlanStartup(
        bool isReleaseRun, string exePath, string approvedExePath, string globalFolder, bool globalExists, string globalVersion, string currentVersion)
    {
        if (!isReleaseRun || IsInFolder(exePath, globalFolder) || SamePath(exePath, approvedExePath))
        {
            return StartupPlan.Continue;
        }

        if (!globalExists)
        {
            return StartupPlan.Ask;
        }

        // The program was installed before, so nothing is asked. Only the newer version wins. An unreadable version is not
        // guessed: the user decides.
        if (!AppVersion.TryParse(globalVersion, out _) || !AppVersion.TryParse(currentVersion, out _))
        {
            return StartupPlan.Ask;
        }

        return AppVersion.IsNewer(currentVersion, globalVersion) ? StartupPlan.ReplaceThenRunFixed : StartupPlan.RunFixed;
    }
}
