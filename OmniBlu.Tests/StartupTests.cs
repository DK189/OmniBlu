using OmniBlu.Managers;

namespace OmniBlu.Tests;

public class StartupTests
{
    private const string Exe = @"C:\Program Files\OmniBlu\OmniBlu.exe";

    [Fact]
    public void The_command_quotes_the_path_and_adds_the_tray_argument_only_for_the_tray_mode()
    {
        Assert.Equal("\"" + Exe + "\"", StartupCommand.Build(Exe, StartupMode.Window));
        Assert.Equal("\"" + Exe + "\" --tray", StartupCommand.Build(Exe, StartupMode.Tray));
    }

    [Fact]
    public void Off_has_no_command()
    {
        Assert.Null(StartupCommand.Build(Exe, StartupMode.Off));
    }

    [Theory]
    [InlineData(StartupMode.Off)]
    [InlineData(StartupMode.Window)]
    [InlineData(StartupMode.Tray)]
    public void A_saved_command_is_read_back_as_the_same_mode(StartupMode mode)
    {
        Assert.Equal(mode, StartupCommand.Parse(StartupCommand.Build(Exe, mode)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_command_means_off(string command)
    {
        Assert.Equal(StartupMode.Off, StartupCommand.Parse(command));
    }

    [Fact]
    public void A_path_that_only_contains_the_word_tray_is_not_the_tray_mode()
    {
        Assert.Equal(StartupMode.Window, StartupCommand.Parse(@"""C:\tools\--tray\OmniBlu.exe"""));
    }

    [Fact]
    public void The_tray_argument_is_found_in_the_program_arguments()
    {
        Assert.True(StartupCommand.StartsInTray(new[] { "--tray" }));
        Assert.True(StartupCommand.StartsInTray(new[] { "--other", "--TRAY" }));
        Assert.False(StartupCommand.StartsInTray(new[] { "--other" }));
        Assert.False(StartupCommand.StartsInTray(Array.Empty<string>()));
        Assert.False(StartupCommand.StartsInTray(null));
    }
}
