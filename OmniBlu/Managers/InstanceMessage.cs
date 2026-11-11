namespace OmniBlu.Managers;

/// <summary>What a second copy tells the copy that is already running: which version it is and where its file is.</summary>
/// <param name="Version">The version the second copy says it has. The receiver does not trust it; it reads the file itself.</param>
/// <param name="Path">The program file of the second copy.</param>
public sealed record OpenRequest(string Version, string Path);

/// <summary>
/// The text that a second copy sends through the pipe, and the reading of it. A copy of an older version sends one byte
/// instead; <see cref="Parse"/> gives null for that, and the receiver then only shows its window.
/// </summary>
public static class InstanceMessage
{
    /// <summary>Longest message that is read. A longer one is cut, then it is not understood.</summary>
    public const int MaxLength = 2048;

    private const string Tag = "open";

    /// <summary>The text for the pipe. Returns null when there is nothing useful to say (no path), so only a "show" signal is sent.</summary>
    public static string Build(string version, string path) =>
        string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(path) || version.Any(char.IsControl) || path.Any(char.IsControl)
            ? null
            : $"{Tag}\t{version}\t{path}";

    /// <summary>
    /// Reads the text from the pipe. Gives null for anything that is not a well-formed request: an old one-byte signal, text
    /// that is too long, a path that is not a full path, or control characters.
    /// </summary>
    public static OpenRequest Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return null;
        }

        var parts = text.Trim().Split('\t');
        if (parts.Length != 3 || parts[0] != Tag || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return null;
        }

        if (parts[1].Any(char.IsControl) || parts[2].Any(char.IsControl) || !Path.IsPathRooted(parts[2]))
        {
            return null;
        }

        return new OpenRequest(parts[1], parts[2]);
    }
}
