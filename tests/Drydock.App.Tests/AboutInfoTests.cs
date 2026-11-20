using Drydock.App.ViewModels;

namespace Drydock.App.Tests;

public class AboutInfoTests
{
    [Fact]
    public void The_tested_version_needs_no_note() =>
        Assert.Null(AboutInfo.GameVersionNote(AboutInfo.TestedGameVersion));

    [Fact]
    public void A_different_game_version_is_pointed_out()
    {
        var note = AboutInfo.GameVersionNote("4.0.0.4000");
        Assert.Contains(AboutInfo.TestedGameVersion, note);
        Assert.Contains("4.0.0.4000", note);
    }

    [Fact]
    public void An_unreadable_game_version_says_so() =>
        Assert.Equal("Drydock couldn't read the game's version.", AboutInfo.GameVersionNote(null));

    [Fact]
    public void Start_game_goes_through_steam() =>
        Assert.Equal("steam://rungameid/1133870", AboutInfo.SteamLaunchUrl);

    [Fact]
    public void The_logo_is_built_into_the_app() =>
        Assert.NotNull(AboutInfo.Logo());

    [Fact]
    public void Links_point_to_the_repo() =>
        Assert.Equal("https://github.com/MPZ-00/Drydock/issues", AboutInfo.IssuesUrl);
}
