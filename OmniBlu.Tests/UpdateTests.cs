using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    [InlineData("0.1.0-rc.1+sha.abc", 0, 1, 0, "rc.1")]
    public void A_version_is_read_with_or_without_v_and_build_information(string text, int major, int minor, int patch, string pre)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        Assert.Equal(new AppVersion(major, minor, patch, pre), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-")]
    public void Text_that_is_not_a_version_is_refused(string text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("1.0.0", "1.0.0-beta.1", true)]      // a release is newer than its pre-release
    [InlineData("1.0.0-beta.1", "1.0.0", false)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.9", true)] // numbers are compared as numbers
    [InlineData("1.0.0-rc.1", "1.0.0-beta.9", true)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha", true)]
    [InlineData("v1.0.1", "1.0.0+abc", true)]
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("1.0.1", "garbage", false)]
    public void Versions_are_compared_by_the_semantic_versioning_rules(string latest, string current, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsNewer(latest, current));
    }

    [Fact]
    public void The_running_version_is_a_real_version()
    {
        Assert.True(AppVersion.TryParse(AppVersion.Current, out _));
    }
}

public class ReleaseParserTests
{
    private const string Good = """
        {
          "tag_name": "v1.2.3", "html_url": "https://github.com/DK189/OmniBlu/releases/tag/v1.2.3",
          "draft": false, "prerelease": false,
          "assets": [
            { "name": "OmniBlu-1.2.3-win-x64.zip", "browser_download_url": "https://github.com/DK189/OmniBlu/releases/download/v1.2.3/OmniBlu-1.2.3-win-x64.zip" },
            { "name": "OmniBlu-1.2.3-win-x64.zip.sha256", "browser_download_url": "https://github.com/DK189/OmniBlu/releases/download/v1.2.3/OmniBlu-1.2.3-win-x64.zip.sha256" }
          ]
        }
        """;

    [Fact]
    public void A_good_release_gives_the_version_and_both_files()
    {
        var release = ReleaseParser.Parse(Good);

        Assert.NotNull(release);
        Assert.Equal("v1.2.3", release.Tag);
        Assert.Equal("1.2.3", release.Version);
        Assert.Equal("OmniBlu-1.2.3-win-x64.zip", release.ZipName);
        Assert.EndsWith("OmniBlu-1.2.3-win-x64.zip", release.ZipUrl);
        Assert.EndsWith("OmniBlu-1.2.3-win-x64.zip.sha256", release.HashUrl);
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.0.0","draft":true,"assets":[]}""")]
    [InlineData("""{"tag_name":"v1.0.0","prerelease":true,"assets":[]}""")]
    [InlineData("""{"tag_name":"nightly","assets":[]}""")]
    [InlineData("""{"tag_name":"v1.0.0","assets":[]}""")]
    [InlineData("""{"tag_name":"v1.0.0","assets":[{"name":"OmniBlu-1.0.0-win-x64.zip","browser_download_url":"https://x/y.zip"}]}""")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    public void A_release_that_cannot_be_installed_gives_nothing(string json)
    {
        Assert.Null(ReleaseParser.Parse(json));
    }
}

public class InstallLocationTests
{
    private const string Global = @"C:\ProgramData\OmniBlu";

    [Theory]
    [InlineData("", "Release", true)]
    [InlineData(null, "Release", true)]
    [InlineData("", "Debug", false)]
    [InlineData(@"C:\src\OmniBlu\bin\Debug\OmniBlu.dll", "Release", false)] // not a single file
    public void Only_a_single_file_release_build_is_a_release_run(string location, string configuration, bool expected)
    {
        Assert.Equal(expected, InstallLocation.IsReleaseRun(location, configuration));
    }

    [Theory]
    [InlineData(@"C:\ProgramData\OmniBlu\OmniBlu.exe", true)]
    [InlineData(@"c:\programdata\omniblu\OmniBlu.exe", true)]
    [InlineData(@"C:\ProgramData\OmniBlu\sub\OmniBlu.exe", false)]
    [InlineData(@"C:\Users\King\Downloads\OmniBlu.exe", false)]
    public void The_fixed_folder_is_recognised_without_regard_to_case(string exe, bool expected)
    {
        Assert.Equal(expected, InstallLocation.IsInFolder(exe, Global));
    }

    [Theory]
    [InlineData(true, @"C:\Users\King\Downloads\OmniBlu.exe", null, true)]
    [InlineData(true, @"C:\Users\King\Downloads\OmniBlu.exe", @"C:\Users\King\Downloads\OmniBlu.exe", false)]
    [InlineData(true, @"C:\Users\King\Downloads\OmniBlu.exe", @"C:\Other\OmniBlu.exe", true)]
    [InlineData(true, @"C:\ProgramData\OmniBlu\OmniBlu.exe", null, false)]
    [InlineData(false, @"C:\Users\King\Downloads\OmniBlu.exe", null, false)]
    public void The_user_is_asked_only_for_an_unconfirmed_release_copy_outside_the_fixed_folder(bool release, string exe, string approved, bool expected)
    {
        Assert.Equal(expected, InstallLocation.NeedsPrompt(release, exe, approved, Global));
    }

    [Fact]
    public void Copying_creates_the_folder_and_replaces_an_older_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "omniblu-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "source", "OmniBlu.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(source));
            File.WriteAllText(source, "new");
            var folder = Path.Combine(root, "global");

            var target = InstallLocation.CopyToFolder(source, folder);
            Assert.Equal(Path.Combine(folder, "OmniBlu.exe"), target);
            Assert.Equal("new", File.ReadAllText(target));

            File.WriteAllText(target, "older");
            InstallLocation.CopyToFolder(source, folder);
            Assert.Equal("new", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class ExeSwapperTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omniblu-test-" + Guid.NewGuid().ToString("N"));

    public ExeSwapperTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Exe => Path.Combine(root, "OmniBlu.exe");

    [Fact]
    public void The_names_are_next_to_the_program()
    {
        Assert.Equal(Path.Combine(root, "OmniBlu.old.exe"), ExeSwapper.OldPath(Exe));
        Assert.Equal(Path.Combine(root, "OmniBlu.new.exe"), ExeSwapper.NewPath(Exe));
    }

    [Fact]
    public void Swapping_keeps_the_old_file_under_the_old_name_and_puts_the_new_file_in_its_place()
    {
        File.WriteAllText(Exe, "old");
        File.WriteAllText(ExeSwapper.NewPath(Exe), "new");

        ExeSwapper.Swap(Exe, ExeSwapper.NewPath(Exe));

        Assert.Equal("new", File.ReadAllText(Exe));
        Assert.Equal("old", File.ReadAllText(ExeSwapper.OldPath(Exe)));
        Assert.False(File.Exists(ExeSwapper.NewPath(Exe)));
    }

    [Fact]
    public void A_leftover_old_file_from_an_earlier_update_does_not_block_the_swap()
    {
        File.WriteAllText(Exe, "v2");
        File.WriteAllText(ExeSwapper.OldPath(Exe), "v1");
        File.WriteAllText(ExeSwapper.NewPath(Exe), "v3");

        ExeSwapper.Swap(Exe, ExeSwapper.NewPath(Exe));

        Assert.Equal("v3", File.ReadAllText(Exe));
        Assert.Equal("v2", File.ReadAllText(ExeSwapper.OldPath(Exe)));
    }

    [Fact]
    public void When_the_new_file_is_missing_the_program_is_put_back()
    {
        File.WriteAllText(Exe, "old");

        Assert.ThrowsAny<IOException>(() => ExeSwapper.Swap(Exe, ExeSwapper.NewPath(Exe)));

        Assert.Equal("old", File.ReadAllText(Exe));
    }

    [Fact]
    public void Clean_up_removes_the_leftovers_and_ignores_missing_files()
    {
        ExeSwapper.CleanUp(Exe);
        File.WriteAllText(ExeSwapper.OldPath(Exe), "x");
        File.WriteAllText(ExeSwapper.NewPath(Exe), "x");

        ExeSwapper.CleanUp(Exe);

        Assert.False(File.Exists(ExeSwapper.OldPath(Exe)));
        Assert.False(File.Exists(ExeSwapper.NewPath(Exe)));
    }

    [Fact]
    public void Writing_is_possible_in_a_normal_folder_and_not_in_a_missing_one()
    {
        Assert.True(ExeSwapper.CanWrite(root));
        Assert.False(ExeSwapper.CanWrite(Path.Combine(root, "missing")));
        Assert.Empty(Directory.GetFiles(root)); // the test file is removed again
    }
}

public class ProcessRestartTests
{
    [Theory]
    [InlineData(new[] { "--wait-pid", "1234" }, 1234)]
    [InlineData(new[] { "--tray", "--wait-pid", "42" }, 42)]
    [InlineData(new[] { "--wait-pid=77" }, 77)]
    [InlineData(new[] { "--WAIT-PID", "5" }, 5)]
    [InlineData(new[] { "--wait-pid" }, 0)]
    [InlineData(new[] { "--wait-pid", "abc" }, 0)]
    [InlineData(new[] { "--wait-pid", "-4" }, 0)]
    [InlineData(new[] { "--tray" }, 0)]
    public void The_process_to_wait_for_is_read_from_the_arguments(string[] args, int expected)
    {
        Assert.Equal(expected, ProcessRestart.ParseWaitPid(args));
    }

    [Fact]
    public void Waiting_for_a_process_that_does_not_exist_returns_at_once()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ProcessRestart.WaitForExit(int.MaxValue - 1, 5000);
        Assert.True(watch.ElapsedMilliseconds < 2000);
    }
}

public class UpdateServiceTests : IDisposable
{
    private const string Prefix = "https://github.com/DK189/OmniBlu/";
    private const string ApiUrl = "https://api.test/latest";
    private const string ZipUrl = Prefix + "releases/download/v2.0.0/OmniBlu-2.0.0-win-x64.zip";
    private const string HashUrl = ZipUrl + ".sha256";

    private readonly string root = Path.Combine(Path.GetTempPath(), "omniblu-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<HttpRequestMessage> requests = new();

    public UpdateServiceTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> answer;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) => this.answer = answer;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private UpdateService Service(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new HttpClient(new FakeHandler(r => { requests.Add(r); return answer(r); })), ApiUrl, Prefix);

    private static HttpResponseMessage Text(string text, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(text, Encoding.UTF8) };

    private static string ReleaseJson(string version) => $$"""
        {"tag_name":"v{{version}}","draft":false,"prerelease":false,"html_url":"{{Prefix}}releases/tag/v{{version}}","assets":[
          {"name":"OmniBlu-{{version}}-win-x64.zip","browser_download_url":"{{ZipUrl}}"},
          {"name":"OmniBlu-{{version}}-win-x64.zip.sha256","browser_download_url":"{{HashUrl}}"}]}
        """;

    private static byte[] ZipWith(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
            writer.Write(content);
        }

        return stream.ToArray();
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // ---------------------------------------------------------------- check

    [Fact]
    public async Task A_newer_release_is_reported_as_available_and_the_request_has_a_user_agent()
    {
        var result = await Service(_ => Text(ReleaseJson("2.0.0"))).CheckAsync("1.0.0");

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("2.0.0", result.Release.Version);
        var request = Assert.Single(requests);
        Assert.Contains("OmniBlu-Updater", request.Headers.UserAgent.ToString()); // GitHub refuses requests without one
        Assert.DoesNotContain("@", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task The_same_or_an_older_release_is_up_to_date()
    {
        Assert.Equal(UpdateStatus.UpToDate, (await Service(_ => Text(ReleaseJson("2.0.0"))).CheckAsync("2.0.0")).Status);
        Assert.Equal(UpdateStatus.UpToDate, (await Service(_ => Text(ReleaseJson("2.0.0"))).CheckAsync("3.0.0")).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]      // no release yet
    [InlineData(HttpStatusCode.Forbidden)]     // rate limit
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refused_check_is_quiet_and_not_an_update(HttpStatusCode code)
    {
        var result = await Service(_ => Text("{}", code)).CheckAsync("1.0.0");

        Assert.Equal(UpdateStatus.Unavailable, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task A_network_failure_is_quiet()
    {
        var service = new UpdateService(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("no network"))), ApiUrl, Prefix);

        Assert.Equal(UpdateStatus.Unavailable, (await service.CheckAsync("1.0.0")).Status);
    }

    [Fact]
    public async Task A_release_without_an_installable_file_is_skipped()
    {
        var result = await Service(_ => Text("""{"tag_name":"v9.0.0","assets":[]}""")).CheckAsync("1.0.0");

        Assert.Equal(UpdateStatus.Unavailable, result.Status);
    }

    // ---------------------------------------------------------------- install

    private ReleaseInfo Release => new("v2.0.0", "2.0.0", Prefix, "OmniBlu-2.0.0-win-x64.zip", ZipUrl, HashUrl);

    private string PutOldProgram()
    {
        var exe = Path.Combine(root, "OmniBlu.exe");
        File.WriteAllText(exe, "old program");
        return exe;
    }

    [Fact]
    public async Task A_good_download_replaces_the_program_and_keeps_the_old_one_under_the_old_name()
    {
        var exe = PutOldProgram();
        var zip = ZipWith("OmniBlu.exe", "new program");
        var service = Service(r => r.RequestUri.AbsoluteUri == HashUrl
            ? Text($"{Sha256(zip)}  OmniBlu-2.0.0-win-x64.zip\n")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        var messages = new List<string>();

        await service.InstallAsync(Release, exe, "1.0.0", new Progress<string>(messages.Add));

        Assert.Equal("new program", File.ReadAllText(exe));
        Assert.Equal("old program", File.ReadAllText(ExeSwapper.OldPath(exe)));
        Assert.False(File.Exists(ExeSwapper.NewPath(exe)));
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_not_installed()
    {
        var exe = PutOldProgram();
        var zip = ZipWith("OmniBlu.exe", "tampered program");
        var service = Service(r => r.RequestUri.AbsoluteUri == HashUrl
            ? Text(new string('a', 64) + "  file.zip")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(Release, exe, "1.0.0", null));

        Assert.Equal("old program", File.ReadAllText(exe));
        Assert.False(File.Exists(ExeSwapper.OldPath(exe)));
        Assert.False(File.Exists(ExeSwapper.NewPath(exe)));
    }

    [Fact]
    public async Task A_zip_file_without_the_program_is_not_installed()
    {
        var exe = PutOldProgram();
        var zip = ZipWith("readme.txt", "hello");
        var service = Service(r => r.RequestUri.AbsoluteUri == HashUrl
            ? Text(Sha256(zip))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(Release, exe, "1.0.0", null));

        Assert.Equal("old program", File.ReadAllText(exe));
        Assert.False(File.Exists(ExeSwapper.NewPath(exe)));
    }

    [Fact]
    public async Task A_download_address_outside_the_project_is_refused_before_any_request()
    {
        var exe = PutOldProgram();
        var other = Release with { ZipUrl = "https://evil.example/OmniBlu-2.0.0-win-x64.zip" };
        var service = Service(_ => Text("unexpected"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(other, exe, "1.0.0", null));

        Assert.Empty(requests);
        Assert.Equal("old program", File.ReadAllText(exe));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_is_reported_before_the_download()
    {
        var missingFolder = Path.Combine(root, "missing", "OmniBlu.exe");
        var service = Service(_ => Text("unexpected"));

        var error = await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(Release, missingFolder, "1.0.0", null));

        Assert.Contains("cannot write", error.Message);
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789  file.zip", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    [InlineData("  abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\r\n", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    public void The_checksum_is_read_from_the_first_word(string text, string expected)
    {
        Assert.Equal(expected, UpdateService.ParseHash(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("zzzzzz0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    public void A_broken_checksum_file_is_refused(string text)
    {
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseHash(text));
    }
}
