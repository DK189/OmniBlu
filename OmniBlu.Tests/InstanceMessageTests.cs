using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class InstanceMessageTests
{
    private const string Exe = @"C:\Users\King\Downloads\OmniBlu.exe";

    [Fact]
    public void A_message_is_read_back_as_the_same_version_and_path()
    {
        var text = InstanceMessage.Build("1.0.1", Exe);

        Assert.Equal(new OpenRequest("1.0.1", Exe), InstanceMessage.Parse(text));
    }

    [Fact]
    public void A_path_with_spaces_and_non_ascii_letters_survives()
    {
        const string path = @"C:\Users\Trần Văn\Tải xuống\OmniBlu (1).exe";

        Assert.Equal(path, InstanceMessage.Parse(InstanceMessage.Build("1.0.1", path)).Path);
    }

    [Theory]
    [InlineData(null, Exe)]
    [InlineData("1.0.1", null)]
    [InlineData("", Exe)]
    [InlineData("1.0.1", "  ")]
    [InlineData("1.0\t1", Exe)]
    [InlineData("1.0.1", "C:\\a\tb.exe")]
    [InlineData("1.0.1", "C:\\a\nb.exe")]
    public void Nothing_is_built_when_there_is_no_usable_version_or_path(string version, string path)
    {
        Assert.Null(InstanceMessage.Build(version, path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001")]                                  // the one-byte signal of an older copy
    [InlineData("open")]
    [InlineData("open\t1.0.1")]
    [InlineData("open\t1.0.1\tC:\\a.exe\textra")]
    [InlineData("show\t1.0.1\tC:\\a.exe")]
    [InlineData("open\t\tC:\\a.exe")]
    [InlineData("open\t1.0.1\t")]
    [InlineData("open\t1.0.1\trelative\\OmniBlu.exe")]       // not a full path
    [InlineData("open\t1.0.1\tC:\\a\u0007.exe")]             // control character
    public void Text_that_is_not_a_request_gives_nothing(string text)
    {
        Assert.Null(InstanceMessage.Parse(text));
    }

    [Fact]
    public void A_message_that_is_too_long_gives_nothing()
    {
        var text = InstanceMessage.Build("1.0.1", @"C:\" + new string('a', InstanceMessage.MaxLength));

        Assert.Null(InstanceMessage.Parse(text));
    }
}

public class SingleInstanceMessageTests
{
    private static string NewName() => "OmniBluTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_text_that_a_second_copy_sends_reaches_the_running_copy()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        string received = null;
        using var done = new ManualResetEventSlim(false);
        first.StartListening(text => { received = text; done.Set(); });
        var message = InstanceMessage.Build("1.0.1", @"C:\Users\King\Downloads\OmniBlu.exe");

        Assert.True(SingleInstance.SignalExisting(name, 3000, message));

        Assert.True(done.Wait(3000));
        Assert.Equal(message, received);
        Assert.Equal(new OpenRequest("1.0.1", @"C:\Users\King\Downloads\OmniBlu.exe"), InstanceMessage.Parse(received));
    }

    [Fact]
    public void The_old_one_byte_signal_arrives_as_an_empty_message_that_means_only_show_the_window()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        string received = null;
        using var done = new ManualResetEventSlim(false);
        first.StartListening(text => { received = text; done.Set(); });

        Assert.True(SingleInstance.SignalExisting(name, 3000));

        Assert.True(done.Wait(3000));
        Assert.Equal(string.Empty, received);
        Assert.Null(InstanceMessage.Parse(received));
    }

    [Fact]
    public void A_handler_that_fails_does_not_stop_the_listening()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        var calls = 0;
        using var second = new ManualResetEventSlim(false);
        first.StartListening(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("the first request fails");
            }

            second.Set();
        });

        Assert.True(SingleInstance.SignalExisting(name, 3000, InstanceMessage.Build("1.0.1", @"C:\a.exe")));
        Assert.True(SingleInstance.SignalExisting(name, 3000, InstanceMessage.Build("1.0.2", @"C:\b.exe")));

        Assert.True(second.Wait(3000));
    }

    [Fact]
    public void A_very_long_message_is_cut_and_the_listening_goes_on()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        var lengths = new List<int>();
        using var both = new CountdownEvent(2);
        first.StartListening(text => { lock (lengths) { lengths.Add(text.Length); } both.Signal(); });

        SingleInstance.SignalExisting(name, 3000, new string('x', 100_000)); // may fail for the sender: it is cut off
        Assert.True(SingleInstance.SignalExisting(name, 3000, "open\t1.0.1\tC:\\a.exe"));

        Assert.True(both.Wait(3000));
        Assert.All(lengths, length => Assert.True(length <= InstanceMessage.MaxLength * 3 + 4));
    }
}

public class SingleInstanceSlowClientTests
{
    [Fact]
    public async Task A_client_that_connects_and_says_nothing_does_not_block_the_next_one()
    {
        var name = "OmniBluTest." + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        using var arrived = new ManualResetEventSlim(false);
        first.StartListening(text => { if (text.StartsWith("open")) { arrived.Set(); } });

        // A silent client: it connects and keeps the pipe open without sending anything.
        await using var silent = new System.IO.Pipes.NamedPipeClientStream(".", $"{name}.{Environment.UserName}", System.IO.Pipes.PipeDirection.Out);
        await silent.ConnectAsync(3000);

        // The listener gives up on it after its time limit (2 s), then serves this request.
        var served = await Task.Run(() => SingleInstance.SignalExisting(name, 8000, InstanceMessage.Build("1.0.1", @"C:\a.exe")));

        Assert.True(served);
        Assert.True(arrived.Wait(8000));
    }
}

public class ReplacementOfferTests
{
    private const string Current = @"C:\ProgramData\OmniBlu\OmniBlu.exe";
    private const string Other = @"C:\Users\King\Downloads\OmniBlu.exe";

    private static bool Offer(
        bool release = true, string exe = Current, string requested = Other, string currentVersion = "1.0.0", string fileVersion = "1.0.1", string product = "OmniBlu") =>
        InstallLocation.ShouldOfferReplacement(release, exe, requested, currentVersion, fileVersion, product);

    [Fact]
    public void A_newer_omniblu_file_is_offered()
    {
        Assert.True(Offer());
        Assert.True(Offer(fileVersion: "1.0.1+abc123"));
        Assert.True(Offer(currentVersion: "1.0.0-beta.1", fileVersion: "1.0.0"));
    }

    [Theory]
    [InlineData("1.0.0")]       // the same
    [InlineData("0.9.0")]       // older
    [InlineData("1.0.0-beta.2")] // a pre-release of the same version
    [InlineData(null)]          // unreadable
    [InlineData("garbage")]
    public void The_same_an_older_or_unreadable_version_is_not_offered(string fileVersion)
    {
        Assert.False(Offer(fileVersion: fileVersion));
    }

    [Fact]
    public void A_development_run_the_same_file_and_other_programs_are_never_offered()
    {
        Assert.False(Offer(release: false));
        Assert.False(Offer(requested: Current));
        Assert.False(Offer(requested: @"c:\programdata\omniblu\OMNIBLU.EXE"));
        Assert.False(Offer(product: "SomethingElse"));
        Assert.False(Offer(product: "omniblu"));
        Assert.False(Offer(product: null));
    }
}
