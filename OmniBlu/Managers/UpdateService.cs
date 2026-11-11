using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace OmniBlu.Managers;

/// <summary>What the check for a new version found.</summary>
public enum UpdateStatus
{
    /// <summary>The running version is the latest one.</summary>
    UpToDate,

    /// <summary>A newer release can be installed.</summary>
    Available,

    /// <summary>Nothing can be said now: no release is published yet, GitHub refused the request, or there is no network.</summary>
    Unavailable,
}

/// <summary>The answer of <see cref="UpdateService.CheckAsync"/>.</summary>
/// <param name="Status">What was found.</param>
/// <param name="Release">The newer release when <paramref name="Status"/> is <see cref="UpdateStatus.Available"/>, otherwise null.</param>
/// <param name="Message">A short sentence for the log.</param>
public sealed record UpdateCheckResult(UpdateStatus Status, ReleaseInfo Release, string Message);

/// <summary>
/// Looks for a newer release on GitHub and installs it. The files must come from the project address
/// (<see cref="DefaultAllowedPrefix"/>) and the zip file must match its SHA-256 file. The SHA-256 file comes from the same
/// release, so it proves the download is complete, but it does not prove who made the file (that would need code signing).
/// </summary>
public sealed class UpdateService
{
    /// <summary>The GitHub address that gives the latest release. It never returns a draft or a pre-release.</summary>
    public const string DefaultApiUrl = "https://api.github.com/repos/DK189/OmniBlu/releases/latest";

    /// <summary>Downloads are accepted only from addresses that start with this text.</summary>
    public const string DefaultAllowedPrefix = "https://github.com/DK189/OmniBlu/";

    /// <summary>Name of the program file inside the release zip file.</summary>
    public const string ExeEntryName = "OmniBlu.exe";

    private readonly HttpClient http;
    private readonly string apiUrl;
    private readonly string allowedPrefix;

    /// <summary>Creates the service. The address and the allowed prefix can be changed for tests only.</summary>
    public UpdateService(HttpClient http, string apiUrl = DefaultApiUrl, string allowedPrefix = DefaultAllowedPrefix)
    {
        this.http = http;
        this.apiUrl = apiUrl;
        this.allowedPrefix = allowedPrefix;
    }

    /// <summary>Asks GitHub for the latest release and compares it with <paramref name="currentVersion"/>. It never throws for network problems.</summary>
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            Prepare(request, currentVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateStatus.Unavailable, null, "No release is published yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateStatus.Unavailable, null, $"GitHub did not answer the check (HTTP {(int)response.StatusCode}). It is tried again at the next start.");
            }

            var release = ReleaseParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (release == null)
            {
                return new UpdateCheckResult(UpdateStatus.Unavailable, null, "The latest release has no installable file, so it is skipped.");
            }

            return AppVersion.IsNewer(release.Version, currentVersion)
                ? new UpdateCheckResult(UpdateStatus.Available, release, $"Version {release.Version} is available.")
                : new UpdateCheckResult(UpdateStatus.UpToDate, null, $"OmniBlu {currentVersion} is the latest version.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new UpdateCheckResult(UpdateStatus.Unavailable, null, $"The check for a new version failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the release, checks it, and replaces <paramref name="exePath"/> with it (see <see cref="ExeSwapper"/>). The
    /// program must be started again afterwards. Throws with a clear message when something goes wrong; the old file is
    /// then still in place.
    /// </summary>
    public async Task InstallAsync(ReleaseInfo release, string exePath, string currentVersion, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        if (!release.ZipUrl.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase)
            || !release.HashUrl.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The download address is not from the OmniBlu project, so it was not used.");
        }

        var folder = Path.GetDirectoryName(exePath);
        if (!ExeSwapper.CanWrite(folder))
        {
            throw new IOException($"OmniBlu cannot write to the folder {folder}. Move it to a folder that you can write to.");
        }

        progress?.Report("Reading the checksum...");
        var expected = ParseHash(await GetStringAsync(release.HashUrl, currentVersion, cancellationToken));

        var zipPath = Path.Combine(Path.GetTempPath(), $"omniblu-update-{Guid.NewGuid():N}.zip");
        var stagedPath = ExeSwapper.NewPath(exePath);
        try
        {
            var actual = await DownloadAsync(release.ZipUrl, zipPath, currentVersion, progress, cancellationToken);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded file does not match its checksum. It was not installed.");
            }

            progress?.Report("Installing...");
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, ExeEntryName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"The zip file has no {ExeEntryName}.");
                await using var source = entry.Open();
                await using var target = File.Create(stagedPath);
                await source.CopyToAsync(target, cancellationToken);
            }

            ExeSwapper.Swap(exePath, stagedPath);
        }
        finally
        {
            TryDelete(zipPath);
            TryDelete(stagedPath); // still there only when something failed before the swap
        }
    }

    /// <summary>Takes the SHA-256 value from a ".sha256" file ("hash  file name"). Throws when it is not a SHA-256 value.</summary>
    public static string ParseHash(string text)
    {
        var token = (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The checksum file is not valid.");
        }

        return token.ToLowerInvariant();
    }

    private void Prepare(HttpRequestMessage request, string currentVersion)
    {
        // GitHub refuses requests that have no User-Agent. It is a generic name: no personal data.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OmniBlu-Updater", currentVersion));
    }

    private async Task<string> GetStringAsync(string url, string currentVersion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Prepare(request, currentVersion);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string> DownloadAsync(string url, string path, string currentVersion, IProgress<string> progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Prepare(request, currentVersion);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        using var hash = SHA256.Create();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(path);

        var buffer = new byte[81920];
        long done = 0;
        long lastReport = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.TransformBlock(buffer, 0, read, null, 0);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            done += read;

            var megabytes = done / (1024 * 1024);
            if (megabytes != lastReport)
            {
                lastReport = megabytes;
                progress?.Report(total.HasValue
                    ? $"Downloading... {megabytes} of {total.Value / (1024 * 1024)} MB"
                    : $"Downloading... {megabytes} MB");
            }
        }

        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(hash.Hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only cleaning.
        }
    }
}
