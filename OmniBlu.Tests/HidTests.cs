using OmniBlu.Managers;
using OmniBlu.Models;

namespace OmniBlu.Tests;

public class BluetoothAddressTests
{
    [Theory]
    [InlineData("b0:8c:75:1d:06:52", "b08c751d0652")]
    [InlineData("B0-8C-75-1D-06-52", "b08c751d0652")]
    [InlineData("B08C751D0652", "b08c751d0652")]
    public void Normalizes_common_formats(string input, string expected) =>
        Assert.Equal(expected, BluetoothAddress.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("b0:8c:75:1d:06")]
    [InlineData("zz:8c:75:1d:06:52")]
    public void Returns_null_for_text_that_is_not_an_address(string input) =>
        Assert.Null(BluetoothAddress.Normalize(input));

    [Fact]
    public void Converts_the_number_used_by_Windows()
    {
        // Real address from the tests machine: b0:8c:75:1d:06:52.
        Assert.Equal("b08c751d0652", BluetoothAddress.FromUInt64(0xB08C751D0652));
        Assert.Equal("00000000abcd", BluetoothAddress.FromUInt64(0xABCD));
    }
}

public class HidClientMatcherTests
{
    private static readonly SmartDeviceInfo Henry = new("77f7a4f2", "Henry", "endpoint", "b08c751d0652");
    private static readonly SmartDeviceInfo Controller = new("3cc8f77b", "Wireless Controller", null, "dcaf68096cf8");

    [Fact]
    public void Matches_a_client_to_the_device_with_the_same_address()
    {
        var match = HidClientMatcher.Match(new[] { Controller, Henry }, "b08c751d0652", "anything");

        Assert.True(match.IsKnown);
        Assert.False(match.ByName);
        Assert.Equal("77f7a4f2", match.Key);
    }

    [Fact]
    public void Accepts_an_address_with_separators_and_uppercase()
    {
        var match = HidClientMatcher.Match(new[] { Henry }, "B0:8C:75:1D:06:52", null);

        Assert.True(match.IsKnown);
        Assert.Equal("77f7a4f2", match.Key);
    }

    [Fact]
    public void A_phone_with_a_random_address_is_matched_by_name_when_the_name_is_unique()
    {
        // Real case: an iPhone not yet paired over LE connects with a random address, but reports its own name.
        var match = HidClientMatcher.Match(new[] { Controller, Henry }, "4b97a1f2c3d4", "henry");

        Assert.True(match.IsKnown);
        Assert.True(match.ByName);
        Assert.Equal("77f7a4f2", match.Key);
    }

    [Fact]
    public void A_name_shared_by_two_devices_is_not_used_to_match()
    {
        var twin = new SmartDeviceInfo("other", "Henry", null, "112233445566");

        var match = HidClientMatcher.Match(new[] { Henry, twin }, "4b97a1f2c3d4", "Henry");

        Assert.False(match.IsKnown);
        Assert.Equal("ble:4b97a1f2c3d4", match.Key);
    }

    [Fact]
    public void An_address_match_wins_over_a_name_match()
    {
        var sameNameOtherAddress = new SmartDeviceInfo("other", "Henry", null, "112233445566");

        var match = HidClientMatcher.Match(new[] { sameNameOtherAddress, Henry }, "b08c751d0652", "Henry");

        Assert.Equal("77f7a4f2", match.Key);
        Assert.False(match.ByName);
    }

    [Fact]
    public void A_client_with_no_match_gets_its_own_key_instead_of_being_dropped()
    {
        var match = HidClientMatcher.Match(new[] { Henry }, "112233445566", "Someone else");

        Assert.False(match.IsKnown);
        Assert.Equal("ble:112233445566", match.Key);
    }

    [Fact]
    public void Devices_without_an_address_are_not_matched_by_address()
    {
        var noAddress = new SmartDeviceInfo("k", "Phone", "endpoint");

        var match = HidClientMatcher.Match(new[] { noAddress }, "b08c751d0652", null);

        Assert.False(match.IsKnown);
    }

    [Fact]
    public void Reads_the_address_back_from_the_key_of_an_unknown_phone()
    {
        Assert.Equal("112233445566", HidClientMatcher.AddressOfUnknownKey("ble:112233445566"));
        Assert.Null(HidClientMatcher.AddressOfUnknownKey("77f7a4f2"));
        Assert.Null(HidClientMatcher.AddressOfUnknownKey(null));
    }
}

public class HidReportsTests
{
    [Fact]
    public void Keyboard_report_has_the_size_declared_for_it()
    {
        Assert.Equal(HidReports.KeyboardReportSize, HidReports.KeyDown(HidReports.KeyEnter).Length);
        Assert.Equal(HidReports.KeyboardReportSize, HidReports.KeysUp().Length);
    }

    [Fact]
    public void Key_down_puts_the_usage_in_the_first_key_slot_and_key_up_is_all_zero()
    {
        var down = HidReports.KeyDown(0x28);

        Assert.Equal(0x28, down[2]);
        Assert.All(HidReports.KeysUp(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Mouse_report_has_the_declared_size_and_keeps_negative_movement()
    {
        var report = HidReports.Mouse(HidReports.MouseLeftButton, -5, 10, 0);

        Assert.Equal(HidReports.MouseReportSize, report.Length);
        Assert.Equal(1, report[0]);
        Assert.Equal(unchecked((byte)-5), report[1]);
        Assert.Equal(10, report[2]);
    }

    [Fact]
    public void Report_map_declares_both_reports()
    {
        var map = HidReports.ReportMap;

        // "Report ID" items are 0x85 followed by the ID.
        Assert.Contains(HidReports.KeyboardReportId, Pairs(map, 0x85));
        Assert.Contains(HidReports.MouseReportId, Pairs(map, 0x85));
    }

    private static IEnumerable<byte> Pairs(byte[] map, byte tag)
    {
        for (var i = 0; i < map.Length - 1; i++)
        {
            if (map[i] == tag)
            {
                yield return map[i + 1];
            }
        }
    }
}

public class HidSettingsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "omniblu-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Hid_verified_is_remembered_by_a_new_manager()
    {
        new SettingsManager(FilePath).SetHidVerified("key-1", true);

        Assert.True(new SettingsManager(FilePath).GetDevice("key-1").HidVerified);
    }

    [Fact]
    public void Changing_the_name_keeps_the_hid_verified_flag()
    {
        var manager = new SettingsManager(FilePath);
        manager.SetHidVerified("key-1", true);

        manager.SetDevice("key-1", "My phone", null);

        var saved = new SettingsManager(FilePath).GetDevice("key-1");
        Assert.True(saved.HidVerified);
        Assert.Equal("My phone", saved.Name);
    }

    [Fact]
    public void Clearing_the_name_does_not_remove_a_verified_device()
    {
        var manager = new SettingsManager(FilePath);
        manager.SetHidVerified("key-1", true);

        manager.SetDevice("key-1", null, null);

        Assert.NotNull(new SettingsManager(FilePath).GetDevice("key-1"));
    }

    [Fact]
    public void A_record_with_nothing_in_it_is_removed()
    {
        var manager = new SettingsManager(FilePath);
        manager.SetHidVerified("key-1", true);

        manager.SetHidVerified("key-1", false);

        Assert.Null(new SettingsManager(FilePath).GetDevice("key-1"));
    }
}

public class MouseSpeedSettingTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "omniblu-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Defaults_to_one() => Assert.Equal(1.0, new SettingsManager(FilePath).MouseSpeed);

    [Fact]
    public void A_saved_speed_is_loaded_by_a_new_manager()
    {
        new SettingsManager(FilePath).SetMouseSpeed(0.6);

        Assert.Equal(0.6, new SettingsManager(FilePath).MouseSpeed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    public void A_broken_speed_falls_back_to_one(double broken)
    {
        new SettingsManager(FilePath).SetMouseSpeed(broken);

        Assert.Equal(1.0, new SettingsManager(FilePath).MouseSpeed);
    }

    [Fact]
    public void A_huge_speed_is_limited()
    {
        new SettingsManager(FilePath).SetMouseSpeed(500);

        Assert.Equal(5.0, new SettingsManager(FilePath).MouseSpeed);
    }
}

public class RawMouseSettingTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "omniblu-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Raw_input_is_on_by_default() => Assert.True(new SettingsManager(FilePath).UseRawMouse);

    [Fact]
    public void Raw_input_can_be_turned_off_in_the_settings_file()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, "{ \"UseRawMouse\": false }");

        Assert.False(new SettingsManager(FilePath).UseRawMouse);
    }
}
