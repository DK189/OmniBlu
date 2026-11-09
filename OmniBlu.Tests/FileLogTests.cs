using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class FileLogTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "omniblu-tests-" + Guid.NewGuid().ToString("N"), "logs");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(folder);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writes_timestamped_lines_to_a_file_named_after_the_day()
    {
        var log = new FileLog(folder);

        log.Write("hello");

        Assert.Equal($"omniblu-{DateTime.Now:yyyyMMdd}.log", Path.GetFileName(log.FilePath));
        var lines = File.ReadAllLines(log.FilePath);
        Assert.Contains("OmniBlu started", lines[0]);
        Assert.EndsWith("  hello", lines[^1]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}  ", lines[^1]);
    }

    [Fact]
    public void A_second_run_appends_instead_of_replacing()
    {
        new FileLog(folder).Write("first run");
        new FileLog(folder).Write("second run");

        var text = File.ReadAllText(new FileLog(folder).FilePath);
        Assert.Contains("first run", text);
        Assert.Contains("second run", text);
    }

    [Fact]
    public void Writes_the_exception_text()
    {
        var log = new FileLog(folder);

        log.Write("it failed", new InvalidOperationException("boom"));

        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("it failed", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void Deletes_log_files_older_than_two_weeks_and_keeps_recent_ones()
    {
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "omniblu-20200101.log");
        var recent = Path.Combine(folder, "omniblu-20200102.log");
        var unrelated = Path.Combine(folder, "notes.txt");
        File.WriteAllText(old, "x");
        File.WriteAllText(recent, "x");
        File.WriteAllText(unrelated, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        File.SetLastWriteTime(unrelated, DateTime.Now.AddDays(-30));

        _ = new FileLog(folder);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void An_unwritable_folder_does_not_throw()
    {
        // A file where the folder should be makes the folder impossible to create.
        Directory.CreateDirectory(Path.GetDirectoryName(folder));
        File.WriteAllText(folder, "I am a file");

        var log = new FileLog(folder);
        var exception = Record.Exception(() => log.Write("still fine"));

        Assert.Null(exception);
    }
}
