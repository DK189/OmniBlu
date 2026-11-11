namespace OmniBlu.Managers;

/// <summary>
/// Replaces the program file with a new one. Windows does not allow deleting a running program, but it does allow renaming
/// it, so the running file becomes "name.old.exe" and the new file takes its name. The old file is deleted at the next start.
/// </summary>
public static class ExeSwapper
{
    /// <summary>Where the running program is kept while the new one takes its name, for example "OmniBlu.old.exe".</summary>
    public static string OldPath(string exePath) => Path.ChangeExtension(exePath, ".old.exe");

    /// <summary>Where the new program is written before it is swapped in, for example "OmniBlu.new.exe". It is in the same folder.</summary>
    public static string NewPath(string exePath) => Path.ChangeExtension(exePath, ".new.exe");

    /// <summary>
    /// Renames <paramref name="exePath"/> to the old name and moves <paramref name="stagedPath"/> to <paramref name="exePath"/>.
    /// Both files must be in the same folder. If the second step fails, the first one is undone and the error is thrown again.
    /// </summary>
    public static void Swap(string exePath, string stagedPath)
    {
        var old = OldPath(exePath);
        if (File.Exists(old))
        {
            File.Delete(old);
        }

        File.Move(exePath, old);
        try
        {
            File.Move(stagedPath, exePath);
        }
        catch
        {
            File.Move(old, exePath);
            throw;
        }
    }

    /// <summary>Deletes the files that an earlier update left next to the program. Errors are ignored: it is only cleaning.</summary>
    public static void CleanUp(string exePath)
    {
        foreach (var path in new[] { OldPath(exePath), NewPath(exePath) })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file may still be in use. The next start tries again.
            }
        }
    }

    /// <summary>True when a file can be created in the folder. An update needs this, so it is checked before the download.</summary>
    public static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
