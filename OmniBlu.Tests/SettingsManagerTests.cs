using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class SettingsManagerTests : IDisposable
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
    public void Saved_device_customization_is_loaded_by_a_new_manager()
    {
        new SettingsManager(FilePath).SetDevice("key-1", "My phone", "🎧");

        var saved = new SettingsManager(FilePath).GetDevice("key-1");

        Assert.NotNull(saved);
        Assert.Equal("My phone", saved.Name);
        Assert.Equal("🎧", saved.Icon);
    }

    [Fact]
    public void Raw_mouse_choice_and_mouse_speed_are_saved()
    {
        var first = new SettingsManager(FilePath);
        Assert.True(first.UseRawMouse);

        first.SetUseRawMouse(false);
        first.SetMouseSpeed(1.7);

        var second = new SettingsManager(FilePath);
        Assert.False(second.UseRawMouse);
        Assert.Equal(1.7, second.MouseSpeed);
    }

    [Fact]
    public void The_shortcut_is_saved_and_a_broken_value_gives_the_default()
    {
        var first = new SettingsManager(FilePath);
        Assert.Equal(HotkeyCombo.Default, first.Hotkey);

        Assert.True(HotkeyCombo.TryParse("Ctrl+Shift+F9", out var chosen));
        first.SetHotkey(chosen);
        Assert.Equal(chosen, new SettingsManager(FilePath).Hotkey);

        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, "{ \"Hotkey\": \"just text\" }");
        Assert.Equal(HotkeyCombo.Default, new SettingsManager(FilePath).Hotkey);
    }

    [Fact]
    public void All_devices_are_hidden_by_default_and_the_choice_is_saved()
    {
        var first = new SettingsManager(FilePath);
        Assert.False(first.ShowAllDevices);

        first.SetShowAllDevices(true);

        Assert.True(new SettingsManager(FilePath).ShowAllDevices);
    }

    [Fact]
    public void Setting_both_values_to_null_removes_the_device()
    {
        var manager = new SettingsManager(FilePath);
        manager.SetDevice("key-1", "My phone", null);

        manager.SetDevice("key-1", null, null);

        Assert.Null(new SettingsManager(FilePath).GetDevice("key-1"));
    }

    [Fact]
    public void A_broken_file_gives_default_settings()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, "{ this is not json");

        var manager = new SettingsManager(FilePath);

        Assert.Empty(manager.Settings.Devices);
    }
}
