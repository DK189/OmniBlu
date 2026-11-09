using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class HotkeyTests
{
    [Fact]
    public void A_shortcut_is_read_and_written_in_a_fixed_order()
    {
        Assert.True(HotkeyCombo.TryParse("alt + ctrl + o", out var combo));

        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, combo.Modifiers);
        Assert.Equal("O", combo.Key);
        Assert.Equal("Ctrl+Alt+O", combo.ToString());
    }

    [Theory]
    [InlineData("O")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+O+P")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+Alt+Ä")]
    [InlineData("")]
    [InlineData(null)]
    public void A_shortcut_without_a_modifier_or_with_an_unsupported_key_is_refused(string text)
    {
        Assert.False(HotkeyCombo.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+Alt+A", 0x41)]
    [InlineData("Ctrl+Alt+z", 0x5A)]
    [InlineData("Ctrl+Alt+0", 0x30)]
    [InlineData("Ctrl+Alt+F1", 0x70)]
    [InlineData("Ctrl+Alt+f12", 0x7B)]
    [InlineData("Ctrl+Alt+Space", 0x20)]
    [InlineData("Win+Shift+pageup", 0x21)]
    [InlineData("Ctrl+Alt+Pause", 0x13)]
    public void Keys_have_the_windows_virtual_key_code(string text, int expected)
    {
        Assert.True(HotkeyCombo.TryParse(text, out var combo));
        Assert.Equal(expected, combo.VirtualKey);
    }

    [Fact]
    public void The_key_above_tab_can_be_used_and_the_default_shortcut_is_win_and_that_key()
    {
        Assert.True(HotkeyCombo.TryParse("win + `", out var combo));
        Assert.Equal("Win+`", combo.ToString());
        Assert.Equal(0xC0, combo.VirtualKey);
        Assert.Equal("Win+`", HotkeyCombo.DefaultText);
    }

    [Theory]
    [InlineData("Win+`", 0xC0, false, false, false, true, true)]
    [InlineData("Win+`", 0xC0, false, false, false, false, false)]   // Windows key not held
    [InlineData("Win+`", 0xC0, true, false, false, true, false)]     // an extra modifier is held
    [InlineData("Win+`", 0x41, false, false, false, true, false)]    // another key
    [InlineData("Ctrl+Win+`", 0xC0, true, false, false, true, true)]
    [InlineData("Ctrl+Win+`", 0xC0, false, false, false, true, false)]
    [InlineData("Ctrl+Alt+O", 0x4F, true, true, false, false, false)] // no Windows key: the system handles it
    public void The_hook_finds_only_shortcuts_with_the_windows_key(string text, int key, bool ctrl, bool alt, bool shift, bool win, bool expected)
    {
        Assert.True(HotkeyCombo.TryParse(text, out var combo));
        Assert.Equal(expected, KeyBlockRules.IsWinHotkey(combo, key, ctrl, alt, shift, win));
    }

    [Fact]
    public void Modifier_values_are_the_ones_windows_uses()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Ctrl);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Win);
    }

    [Fact]
    public void The_default_shortcut_is_valid()
    {
        Assert.NotNull(HotkeyCombo.Default);
        Assert.Equal(HotkeyCombo.DefaultText, HotkeyCombo.Default.ToString());
    }

    [Theory]
    [InlineData(KeyBlockRules.VkLeftWin, false, false, true)]
    [InlineData(KeyBlockRules.VkRightWin, false, false, true)]
    [InlineData(KeyBlockRules.VkTab, true, false, true)]
    [InlineData(KeyBlockRules.VkTab, false, false, false)]
    [InlineData(KeyBlockRules.VkTab, false, true, false)]
    [InlineData(KeyBlockRules.VkEscape, true, false, true)]
    [InlineData(KeyBlockRules.VkEscape, false, true, true)]
    [InlineData(KeyBlockRules.VkEscape, false, false, false)]
    [InlineData(0x41, true, true, false)]
    public void Only_the_keys_that_would_take_the_focus_are_blocked(int key, bool alt, bool ctrl, bool expected)
    {
        Assert.Equal(expected, KeyBlockRules.ShouldBlock(key, alt, ctrl));
    }

    private static readonly string[] Two ={ "a", "b" };
    private static readonly string[] Three = { "a", "b", "c" };

    [Fact]
    public void Nothing_happens_when_no_device_is_connected()
    {
        Assert.Null(ControlCycler.Next(Array.Empty<string>(), null, "a"));
        Assert.Null(ControlCycler.Next(Array.Empty<string>(), "a", "a"));
    }

    [Fact]
    public void Without_control_the_last_device_is_used_again()
    {
        Assert.Equal("b", ControlCycler.Next(Three, null, "b"));
    }

    [Fact]
    public void Without_control_and_without_a_usable_last_device_the_first_is_used()
    {
        Assert.Equal("a", ControlCycler.Next(Three, null, null));
        Assert.Equal("a", ControlCycler.Next(Three, null, "gone"));
    }

    [Fact]
    public void While_controlling_the_next_device_is_chosen_and_the_list_wraps_around()
    {
        Assert.Equal("b", ControlCycler.Next(Three, "a", "a"));
        Assert.Equal("c", ControlCycler.Next(Three, "b", "b"));
        Assert.Equal("a", ControlCycler.Next(Three, "c", "c"));
    }

    [Fact]
    public void While_controlling_the_only_device_there_is_nothing_to_switch_to()
    {
        Assert.Null(ControlCycler.Next(new[] { "a" }, "a", "a"));
    }

    [Fact]
    public void A_controlled_device_that_is_no_longer_in_the_list_leads_to_the_first_device()
    {
        Assert.Equal("a", ControlCycler.Next(Two, "gone", "gone"));
    }

    [Fact]
    public void After_control_ends_the_shortcut_returns_to_the_device_that_was_controlled()
    {
        // Control the first device, switch to the second, stop. The next press goes to the second, not the first.
        var afterSwitch = ControlCycler.Next(Two, "a", "a");
        Assert.Equal("b", afterSwitch);

        Assert.Equal("b", ControlCycler.Next(Two, null, afterSwitch));
    }
}
