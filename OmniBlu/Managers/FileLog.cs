using System.Text;

namespace OmniBlu.Managers;

/// <summary>
/// Writes the event log to a text file, one file per day, so a problem can be read after the app is closed.
/// Writing never throws: a broken log must not break the app.
/// </summary>
public sealed class FileLog
{
    private const int KeepDays = 14;

    private readonly object writeLock = new();

    public FileLog()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniBlu", "logs"))
    {
    }

    public FileLog(string folder)
    {
        Folder = folder;
        FilePath = Path.Combine(folder, $"omniblu-{DateTime.Now:yyyyMMdd}.log");

        DeleteOldFiles();
        Write($"===== OmniBlu started (process {Environment.ProcessId}) =====");
    }

    public string Folder { get; }

    /// <summary>The file of today. Every run of the app appends to it.</summary>
    public string FilePath { get; }

    public void Write(string message)
    {
        try
        {
            lock (writeLock)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // Nothing sensible to do: the disk may be full or the folder locked.
        }
    }

    public void Write(string message, Exception exception) => Write($"{message}{Environment.NewLine}{exception}");

    private void DeleteOldFiles()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            var limit = DateTime.Now.AddDays(-KeepDays);
            foreach (var file in Directory.EnumerateFiles(Folder, "omniblu-*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Old logs staying around is harmless.
        }
    }
}
