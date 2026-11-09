using Avalonia.Input;
using OmniBlu.Managers;
using OmniBlu.UI;

namespace OmniBlu.Tests;

public class HidKeyboardTests
{
    [Theory]
    [InlineData('a', 0x04, 0)]
    [InlineData('z', 0x1D, 0)]
    [InlineData('A', 0x04, HidKeyboard.ShiftModifier)]
    [InlineData('1', 0x1E, 0)]
    [InlineData('0', 0x27, 0)]
    [InlineData('!', 0x1E, HidKeyboard.ShiftModifier)]
    [InlineData(')', 0x27, HidKeyboard.ShiftModifier)]
    [InlineData(' ', 0x2C, 0)]
    [InlineData('\n', 0x28, 0)]
    [InlineData('-', 0x2D, 0)]
    [InlineData('_', 0x2D, HidKeyboard.ShiftModifier)]
    [InlineData('/', 0x38, 0)]
    [InlineData('?', 0x38, HidKeyboard.ShiftModifier)]
    [InlineData('"', 0x34, HidKeyboard.ShiftModifier)]
    public void Maps_characters_to_the_us_layout(char c, int usage, int modifiers)
    {
        Assert.True(HidKeyboard.TryMap(c, out var key));
        Assert.Equal((byte)usage, key.Usage);
        Assert.Equal((byte)modifiers, key.Modifiers);
    }

    [Theory]
    [InlineData('é')]
    [InlineData('ế')]
    [InlineData('日')]
    public void Characters_outside_the_us_layout_cannot_be_typed(char c) =>
        Assert.False(HidKeyboard.TryMap(c, out _));

    [Fact]
    public void Every_printable_ascii_character_has_a_key()
    {
        for (var c = ' '; c <= '~'; c++)
        {
            Assert.True(HidKeyboard.TryMap(c, out _), $"'{c}' has no key");
        }
    }

    [Theory]
    [InlineData("Enter", 0x28)]
    [InlineData("escape", 0x29)]
    [InlineData("Up", 0x52)]
    [InlineData("PageDown", 0x4E)]
    public void Finds_named_keys_ignoring_case(string name, int usage)
    {
        Assert.True(HidKeyboard.TryGetNamed(name, out var found));
        Assert.Equal((byte)usage, found);
    }

    [Fact]
    public void Unknown_key_names_are_not_found()
    {
        Assert.False(HidKeyboard.TryGetNamed("Banana", out _));
        Assert.False(HidKeyboard.TryGetNamed(null, out _));
    }
}

public class HidKeyStateTests
{
    [Fact]
    public void Press_then_release_gives_a_report_each_time()
    {
        var state = new HidKeyState();

        var down = state.Press(0x04);
        var up = state.Release(0x04);

        Assert.Equal(0x04, down[2]);
        Assert.All(up, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Key_repeat_gives_no_new_report()
    {
        var state = new HidKeyState();
        state.Press(0x04);

        Assert.Null(state.Press(0x04));
    }

    [Fact]
    public void Releasing_a_key_that_is_not_held_gives_no_report() =>
        Assert.Null(new HidKeyState().Release(0x04));

    [Fact]
    public void Holds_several_keys_in_the_order_they_were_pressed()
    {
        var state = new HidKeyState();
        state.Press(0x04);
        var report = state.Press(0x05);

        Assert.Equal(0x04, report[2]);
        Assert.Equal(0x05, report[3]);
    }

    [Fact]
    public void A_seventh_key_is_ignored()
    {
        var state = new HidKeyState();
        for (byte key = 0x04; key < 0x04 + HidKeyState.MaxKeys; key++)
        {
            state.Press(key);
        }

        Assert.Null(state.Press(0x20));
    }

    [Fact]
    public void Modifiers_are_bits_and_combine_with_keys()
    {
        var state = new HidKeyState();
        state.SetModifier(HidKeyboard.ShiftModifier, true);
        var report = state.Press(0x04);

        Assert.Equal(HidKeyboard.ShiftModifier, report[0]);
        Assert.Equal(0x04, report[2]);
        Assert.Null(state.SetModifier(HidKeyboard.ShiftModifier, true));
        Assert.Equal(0, state.SetModifier(HidKeyboard.ShiftModifier, false)[0]);
    }

    [Fact]
    public void Clear_always_reports_all_keys_released()
    {
        var state = new HidKeyState();
        state.Press(0x04);
        state.SetModifier(0x01, true);

        Assert.All(state.Clear(), b => Assert.Equal(0, b));
        Assert.All(state.Clear(), b => Assert.Equal(0, b));
    }
}

public class MouseAccumulatorTests
{
    [Fact]
    public void Nothing_to_send_gives_no_report() =>
        Assert.False(new MouseAccumulator().TryTake(out _));

    [Fact]
    public void Movements_are_added_up_into_one_report()
    {
        var mouse = new MouseAccumulator();
        mouse.Move(3, -2);
        mouse.Move(4, -1);

        Assert.True(mouse.TryTake(out var report));
        Assert.Equal(7, (sbyte)report[1]);
        Assert.Equal(-3, (sbyte)report[2]);
        Assert.False(mouse.TryTake(out _));
    }

    [Fact]
    public void A_big_move_is_split_so_no_distance_is_lost()
    {
        var mouse = new MouseAccumulator();
        mouse.Move(300, 0);

        var total = 0;
        var reports = 0;
        while (mouse.TryTake(out var report))
        {
            total += (sbyte)report[1];
            reports++;
        }

        Assert.Equal(300, total);
        Assert.Equal(3, reports);
    }

    [Fact]
    public void A_button_change_is_reported_once_and_then_held()
    {
        var mouse = new MouseAccumulator();
        mouse.SetButton(MouseAccumulator.LeftButton, true);

        Assert.True(mouse.TryTake(out var pressed));
        Assert.Equal(MouseAccumulator.LeftButton, pressed[0]);
        Assert.False(mouse.TryTake(out _));

        mouse.Move(1, 0);
        Assert.True(mouse.TryTake(out var moved));
        Assert.Equal(MouseAccumulator.LeftButton, moved[0]);

        mouse.SetButton(MouseAccumulator.LeftButton, false);
        Assert.True(mouse.TryTake(out var released));
        Assert.Equal(0, released[0]);
    }

    [Fact]
    public void Setting_a_button_to_the_state_it_already_has_gives_no_report()
    {
        var mouse = new MouseAccumulator();
        mouse.SetButton(MouseAccumulator.RightButton, false);

        Assert.False(mouse.TryTake(out _));
    }

    [Fact]
    public void The_wheel_is_reported()
    {
        var mouse = new MouseAccumulator();
        mouse.Scroll(-2);

        Assert.True(mouse.TryTake(out var report));
        Assert.Equal(-2, (sbyte)report[3]);
    }
}

public class KeyMapperTests
{
    [Theory]
    [InlineData(Key.A, 0x04)]
    [InlineData(Key.Z, 0x1D)]
    [InlineData(Key.D1, 0x1E)]
    [InlineData(Key.D0, 0x27)]
    [InlineData(Key.F1, 0x3A)]
    [InlineData(Key.F12, 0x45)]
    [InlineData(Key.Enter, 0x28)]
    [InlineData(Key.Back, 0x2A)]
    [InlineData(Key.Up, 0x52)]
    [InlineData(Key.OemComma, 0x36)]
    public void Maps_pc_keys_to_hid_codes(Key key, int usage)
    {
        Assert.True(KeyMapper.TryGetUsage(key, out var found));
        Assert.Equal((byte)usage, found);
    }

    [Theory]
    [InlineData(Key.LeftCtrl, 0x01)]
    [InlineData(Key.LeftShift, 0x02)]
    [InlineData(Key.LeftAlt, 0x04)]
    [InlineData(Key.RightShift, 0x20)]
    public void Modifier_keys_are_bits(Key key, int bit)
    {
        Assert.True(KeyMapper.TryGetModifier(key, out var found));
        Assert.Equal((byte)bit, found);
        Assert.False(KeyMapper.TryGetUsage(key, out _));
    }

    [Fact]
    public void The_exit_key_is_never_sent_to_the_phone()
    {
        Assert.False(KeyMapper.TryGetModifier(KeyMapper.ExitKey, out _));
        Assert.False(KeyMapper.TryGetUsage(KeyMapper.ExitKey, out _));
    }
}

public class HidControlSessionTests
{
    private sealed class FakeHid : IHidService
    {
        public List<byte[]> Keyboard { get; } = new();

        public List<byte[]> Mouse { get; } = new();

        public bool Fail { get; set; }

        public event EventHandler<HidStateChangedEventArgs> StateChanged { add { } remove { } }

        public event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected { add { } remove { } }

        public event EventHandler<string> LogMessage { add { } remove { } }

        public Task<bool> SendKeyboardReportAsync(string key, byte[] report)
        {
            lock (Keyboard)
            {
                Keyboard.Add(report);
            }

            return Task.FromResult(!Fail);
        }

        public Task<bool> SendMouseReportAsync(string key, byte[] report)
        {
            lock (Mouse)
            {
                Mouse.Add(report);
            }

            return Task.FromResult(!Fail);
        }

        public Task<bool> EnableAsync(string key) => Task.FromResult(true);

        public Task DisableAsync(string key) => Task.CompletedTask;

        public Task<bool> SendClickAsync(string key) => Task.FromResult(true);

        public Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0) => Task.FromResult(true);

        public Task<int> SendTextAsync(string key, string text) => Task.FromResult(text.Length);
    }

    [Fact]
    public async Task Keys_are_sent_in_order_and_stopping_releases_everything()
    {
        var hid = new FakeHid();
        var session = new HidControlSession(hid, "phone");

        session.KeyDown(0x04);
        session.KeyUp(0x04);
        session.KeyDown(0x05);
        await session.StopAsync();

        // down a, up a, down b, then the release-all sent on stop.
        Assert.Equal(4, hid.Keyboard.Count);
        Assert.Equal(0x04, hid.Keyboard[0][2]);
        Assert.Equal(0, hid.Keyboard[1][2]);
        Assert.Equal(0x05, hid.Keyboard[2][2]);
        Assert.All(hid.Keyboard[3], b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Stopping_releases_a_held_mouse_button()
    {
        var hid = new FakeHid();
        var session = new HidControlSession(hid, "phone");

        session.MouseButton(MouseAccumulator.LeftButton, true);
        await Task.Delay(100);
        await session.StopAsync();

        Assert.Equal(MouseAccumulator.LeftButton, hid.Mouse.First()[0]);
        Assert.Equal(0, hid.Mouse.Last()[0]);
    }

    [Fact]
    public async Task Mouse_movement_is_sent()
    {
        var hid = new FakeHid();
        var session = new HidControlSession(hid, "phone");

        session.MouseMove(10, 5);

        // Movement that has not been sent when the session stops is dropped on purpose, so wait for it first.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !HasSent(hid))
        {
            await Task.Delay(10);
        }

        await session.StopAsync();

        Assert.True(HasSent(hid));
    }

    [Fact]
    public async Task Movement_not_sent_yet_is_dropped_when_the_session_stops()
    {
        var hid = new FakeHid();
        var session = new HidControlSession(hid, "phone");

        session.MouseMove(10, 5);
        await session.StopAsync();

        // The release is still sent, but not the old movement.
        Assert.DoesNotContain(hid.Mouse, r => (sbyte)r[1] == 10 && (sbyte)r[2] == 5);
    }

    private static bool HasSent(FakeHid hid)
    {
        lock (hid.Mouse)
        {
            return hid.Mouse.Any(r => (sbyte)r[1] == 10 && (sbyte)r[2] == 5);
        }
    }

    [Fact]
    public async Task Repeated_send_failures_raise_Faulted_once()
    {
        var hid = new FakeHid { Fail = true };
        var session = new HidControlSession(hid, "phone");
        var faulted = new TaskCompletionSource();
        var count = 0;
        session.Faulted += (_, _) =>
        {
            Interlocked.Increment(ref count);
            faulted.TrySetResult();
        };

        for (var i = 0; i < 10; i++)
        {
            session.KeyDown((byte)(0x04 + i));
        }

        Assert.Same(faulted.Task, await Task.WhenAny(faulted.Task, Task.Delay(3000)));
        await session.StopAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Stopping_twice_is_safe()
    {
        var session = new HidControlSession(new FakeHid(), "phone");

        await session.StopAsync();
        var exception = await Record.ExceptionAsync(() => session.StopAsync());

        Assert.Null(exception);
    }
}

public class RelativeCursorTests
{
    private sealed class FakeCursor : ICursorLock
    {
        public int X { get; set; }

        public int Y { get; set; }

        public bool IsSupported => true;

        public void Clip(int left, int top, int right, int bottom)
        {
        }

        public void Unclip()
        {
        }

        public void SetPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool TryGetPosition(out int x, out int y)
        {
            x = X;
            y = Y;
            return true;
        }
    }

    [Fact]
    public void Reads_the_distance_from_the_centre_and_puts_the_cursor_back()
    {
        var cursor = new FakeCursor { X = 105, Y = 197 };
        var relative = new RelativeCursor(cursor, 100, 200);

        Assert.True(relative.TryRead(out var dx, out var dy));

        Assert.Equal(5, dx);
        Assert.Equal(-3, dy);
        Assert.Equal((100, 200), (cursor.X, cursor.Y));
    }

    [Fact]
    public void An_old_event_after_centring_adds_nothing_so_movement_is_not_counted_twice()
    {
        // The bug this guards against: two queued mouse events both carried "5 pixels right". Counting both gave 10.
        var cursor = new FakeCursor { X = 105, Y = 200 };
        var relative = new RelativeCursor(cursor, 100, 200);

        var first = relative.TryRead(out var dx1, out _);
        var second = relative.TryRead(out var dx2, out _);

        Assert.True(first);
        Assert.Equal(5, dx1);
        Assert.False(second);
        Assert.Equal(0, dx2);
    }

    [Fact]
    public void A_cursor_already_in_the_centre_gives_no_movement_and_no_phantom_drift()
    {
        var cursor = new FakeCursor { X = 100, Y = 200 };
        var relative = new RelativeCursor(cursor, 100, 200);

        for (var i = 0; i < 100; i++)
        {
            Assert.False(relative.TryRead(out _, out _));
        }
    }

    [Fact]
    public void Movement_between_two_reads_is_summed_into_the_next_read()
    {
        var cursor = new FakeCursor { X = 100, Y = 200 };
        var relative = new RelativeCursor(cursor, 100, 200);

        cursor.X += 3;
        cursor.X += 4;

        Assert.True(relative.TryRead(out var dx, out _));
        Assert.Equal(7, dx);
    }

    [Fact]
    public void A_cursor_that_cannot_be_read_gives_no_movement()
    {
        var relative = new RelativeCursor(new UnreadableCursor(), 100, 200);

        Assert.False(relative.TryRead(out _, out _));
    }

    private sealed class UnreadableCursor : ICursorLock
    {
        public bool IsSupported => false;

        public void Clip(int left, int top, int right, int bottom)
        {
        }

        public void Unclip()
        {
        }

        public void SetPosition(int x, int y)
        {
        }

        public bool TryGetPosition(out int x, out int y)
        {
            x = 0;
            y = 0;
            return false;
        }
    }
}

public class MousePacingTests
{
    [Fact]
    public void A_big_move_is_limited_to_the_step_size_per_report()
    {
        var mouse = new MouseAccumulator(maxStep: 40);
        mouse.Move(100, -90);

        var reports = new List<byte[]>();
        while (mouse.TryTake(out var report))
        {
            reports.Add(report);
        }

        Assert.All(reports, r =>
        {
            Assert.InRange((sbyte)r[1], -40, 40);
            Assert.InRange((sbyte)r[2], -40, 40);
        });
        Assert.Equal(100, reports.Sum(r => (sbyte)r[1]));
        Assert.Equal(-90, reports.Sum(r => (sbyte)r[2]));
        Assert.Equal(3, reports.Count);
    }

    [Fact]
    public void Speed_scales_movement_and_keeps_the_fraction_so_slow_movement_is_not_lost()
    {
        var mouse = new MouseAccumulator { Speed = 0.5 };

        // One pixel at a time, 10 times: at half speed that must add up to 5, not to 0.
        for (var i = 0; i < 10; i++)
        {
            mouse.Move(1, 0);
        }

        Assert.True(mouse.TryTake(out var report));
        Assert.Equal(5, (sbyte)report[1]);
    }

    [Fact]
    public void Speed_above_one_makes_movement_bigger()
    {
        var mouse = new MouseAccumulator { Speed = 2 };
        mouse.Move(10, -4);

        Assert.True(mouse.TryTake(out var report));
        Assert.Equal(20, (sbyte)report[1]);
        Assert.Equal(-8, (sbyte)report[2]);
    }

    [Fact]
    public void Discarding_movement_keeps_a_held_button()
    {
        var mouse = new MouseAccumulator();
        mouse.SetButton(MouseAccumulator.LeftButton, true);
        mouse.TryTake(out _);
        mouse.Move(50, 50);
        mouse.Scroll(3);

        mouse.DiscardMovement();

        Assert.False(mouse.TryTake(out _));
        mouse.SetButton(MouseAccumulator.LeftButton, false);
        Assert.True(mouse.TryTake(out var report));
        Assert.Equal(0, report[0]);
    }

    [Fact]
    public void Pending_magnitude_is_the_biggest_axis_left_to_send()
    {
        var mouse = new MouseAccumulator(maxStep: 40);
        mouse.Move(-100, 30);

        Assert.Equal(100, mouse.PendingMagnitude);
        mouse.TryTake(out _);
        Assert.Equal(60, mouse.PendingMagnitude);
    }

    private sealed class TimedHid : IHidService
    {
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public List<(long At, byte[] Report)> Mouse { get; } = new();

        public event EventHandler<HidStateChangedEventArgs> StateChanged { add { } remove { } }

        public event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected { add { } remove { } }

        public event EventHandler<string> LogMessage { add { } remove { } }

        public Task<bool> SendKeyboardReportAsync(string key, byte[] report) => Task.FromResult(true);

        public Task<bool> SendMouseReportAsync(string key, byte[] report)
        {
            lock (Mouse)
            {
                Mouse.Add((clock.ElapsedMilliseconds, report));
            }

            return Task.FromResult(true);
        }

        public Task<bool> EnableAsync(string key) => Task.FromResult(true);

        public Task DisableAsync(string key) => Task.CompletedTask;

        public Task<bool> SendClickAsync(string key) => Task.FromResult(true);

        public Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0) => Task.FromResult(true);

        public Task<int> SendTextAsync(string key, string text) => Task.FromResult(text.Length);
    }

    [Fact]
    public async Task The_pieces_of_a_big_move_are_spread_over_time_not_sent_in_a_burst()
    {
        var hid = new TimedHid();
        var session = new HidControlSession(hid, "phone");

        session.MouseMove(200, 0);

        // Wait until all 200 units have been sent (5 reports of at most 40).
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            lock (hid.Mouse)
            {
                if (hid.Mouse.Sum(r => (sbyte)r.Report[1]) >= 200)
                {
                    break;
                }
            }

            await Task.Delay(10);
        }

        await session.StopAsync();

        List<(long At, byte[] Report)> sent;
        lock (hid.Mouse)
        {
            sent = hid.Mouse.Where(r => (sbyte)r.Report[1] != 0).ToList();
        }

        Assert.Equal(200, sent.Sum(r => (sbyte)r.Report[1]));
        Assert.All(sent, r => Assert.InRange((sbyte)r.Report[1], -HidControlSession.MaxMouseStep, HidControlSession.MaxMouseStep));

        // Neighbouring reports are at least about one interval apart (8 ms, allow a little for timer rounding).
        for (var i = 1; i < sent.Count; i++)
        {
            Assert.True(sent[i].At - sent[i - 1].At >= 6, $"reports {i - 1} and {i} were only {sent[i].At - sent[i - 1].At} ms apart");
        }
    }

    [Fact]
    public async Task The_summary_reports_what_happened()
    {
        var hid = new TimedHid();
        var session = new HidControlSession(hid, "phone") { MouseSpeed = 1 };

        session.MouseMove(30, 0);
        await Task.Delay(100);
        await session.StopAsync();

        var summary = session.GetSummary();
        Assert.Contains("1 movement(s), largest 30px", summary);
        Assert.Contains("report(s)", summary);
        Assert.Contains("slowest send", summary);
    }
}

public class RawMouseTests
{
    [Fact]
    public void A_relative_event_gives_its_movement()
    {
        Assert.True(RawMouse.TryGetRelative(0, 30, -12, out var dx, out var dy));
        Assert.Equal(30, dx);
        Assert.Equal(-12, dy);
    }

    [Fact]
    public void An_absolute_position_is_not_movement()
    {
        // Touch screens, pens and remote desktop send absolute positions. As movement they would be a huge jump.
        Assert.False(RawMouse.TryGetRelative(RawMouse.MoveAbsolute, 30000, 20000, out var dx, out var dy));
        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void An_event_with_no_movement_is_ignored()
    {
        // Button and wheel events also arrive as raw mouse events, with zero movement.
        Assert.False(RawMouse.TryGetRelative(0, 0, 0, out _, out _));
    }

    [Fact]
    public void Other_flag_bits_do_not_hide_a_relative_movement()
    {
        Assert.True(RawMouse.TryGetRelative(0x08, 5, 5, out var dx, out _));
        Assert.Equal(5, dx);
    }

    [Fact]
    public async Task A_session_can_use_the_full_report_range_for_raw_input()
    {
        var hid = new RawModeHid();
        var session = new HidControlSession(hid, "phone", maxMouseStep: 127);

        session.MouseMove(120, 0);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && hid.LargestStep < 120)
        {
            await Task.Delay(10);
        }

        await session.StopAsync();

        // One report carries the whole move: with the default limit of 40 it would have taken three.
        Assert.Equal(120, hid.LargestStep);
    }

    private sealed class RawModeHid : IHidService
    {
        private int largestStep;

        public int LargestStep => Volatile.Read(ref largestStep);

        public event EventHandler<HidStateChangedEventArgs> StateChanged { add { } remove { } }

        public event EventHandler<UnknownHidClientEventArgs> UnknownClientConnected { add { } remove { } }

        public event EventHandler<string> LogMessage { add { } remove { } }

        public Task<bool> SendKeyboardReportAsync(string key, byte[] report) => Task.FromResult(true);

        public Task<bool> SendMouseReportAsync(string key, byte[] report)
        {
            var step = Math.Abs((sbyte)report[1]);
            int current;
            while (step > (current = Volatile.Read(ref largestStep)))
            {
                if (Interlocked.CompareExchange(ref largestStep, step, current) == current)
                {
                    break;
                }
            }

            return Task.FromResult(true);
        }

        public Task<bool> EnableAsync(string key) => Task.FromResult(true);

        public Task DisableAsync(string key) => Task.CompletedTask;

        public Task<bool> SendClickAsync(string key) => Task.FromResult(true);

        public Task<bool> SendKeyAsync(string key, byte usage, byte modifiers = 0) => Task.FromResult(true);

        public Task<int> SendTextAsync(string key, string text) => Task.FromResult(text.Length);
    }
}
