using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class SingleInstanceTests
{
    private static string NewName() => "OmniBluTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_second_copy_does_not_get_ownership_until_the_first_one_ends()
    {
        var name = NewName();
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        Assert.Null(SingleInstance.TryAcquire(name));

        first.Dispose();
        using var again = SingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void A_second_copy_can_ask_the_first_one_to_show_its_window()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        using var asked = new ManualResetEventSlim(false);
        first.StartListening(asked.Set);

        Assert.True(SingleInstance.SignalExisting(name, 3000));
        Assert.True(asked.Wait(3000));
    }

    [Fact]
    public void Several_requests_in_a_row_are_all_delivered()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        using var done = new CountdownEvent(3);
        first.StartListening(() => done.Signal());

        for (var i = 0; i < 3; i++)
        {
            Assert.True(SingleInstance.SignalExisting(name, 3000));
        }

        Assert.True(done.Wait(3000));
    }

    [Fact]
    public void Asking_when_no_copy_is_listening_fails_without_an_error()
    {
        Assert.False(SingleInstance.SignalExisting(NewName(), 200));
    }
}
