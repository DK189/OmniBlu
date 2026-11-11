using System.Text.Json;

namespace OmniBlu.Managers;

/// <summary>A published release that can be installed: its version and the files to download.</summary>
/// <param name="Tag">Tag name, for example "v1.2.3".</param>
/// <param name="Version">Version text without the "v", for example "1.2.3".</param>
/// <param name="PageUrl">Web page of the release.</param>
/// <param name="ZipName">File name of the zip file.</param>
/// <param name="ZipUrl">Download address of the zip file.</param>
/// <param name="HashUrl">Download address of the SHA-256 file of the zip file.</param>
public sealed record ReleaseInfo(string Tag, string Version, string PageUrl, string ZipName, string ZipUrl, string HashUrl);

/// <summary>Reads the answer of the GitHub "latest release" API.</summary>
public static class ReleaseParser
{
    /// <summary>Ending of the file name of the release zip file.</summary>
    public const string ZipSuffix = "-win-x64.zip";

    /// <summary>
    /// Returns the release, or null when the text is not a usable release: broken JSON, a draft or pre-release, a tag that is
    /// not a version, or a release that has no zip file and no SHA-256 file.
    /// </summary>
    public static ReleaseInfo Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Flag(root, "draft") || Flag(root, "prerelease"))
            {
                return null;
            }

            var tag = Text(root, "tag_name");
            if (!AppVersion.TryParse(tag, out var version) || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string zipName = null, zipUrl = null;
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                if (name == null || url == null)
                {
                    continue;
                }

                files[name] = url;
                if (zipName == null && name.EndsWith(ZipSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    zipName = name;
                    zipUrl = url;
                }
            }

            if (zipName == null || !files.TryGetValue(zipName + ".sha256", out var hashUrl))
            {
                return null;
            }

            return new ReleaseInfo(tag, version.ToString(), Text(root, "html_url"), zipName, zipUrl, hashUrl);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
