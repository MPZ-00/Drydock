using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App.Tests;

public class SaveFlowTests
{
    [Fact]
    public void A_running_game_wins_over_everything() =>
        Assert.IsType<SaveDecision.GameRunning>(MainViewModel.Decide(gameRunning: true, changedOnDisk: true, overrides: 3, writeAnyway: true, allowRunningGame: false));

    [Fact]
    public void A_running_game_asks_unless_allowed()
    {
        Assert.IsType<SaveDecision.GameRunning>(MainViewModel.Decide(true, false, 0, false, allowRunningGame: false));
        Assert.IsType<SaveDecision.Write>(MainViewModel.Decide(true, false, 0, false, allowRunningGame: true));
    }

    [Fact]
    public void The_reason_says_why()
    {
        Assert.Contains("autosave", SaveWriter.GameRunningReason);
        Assert.Contains("main menu", SaveWriter.GameRunningReason);
    }

    [Fact]
    public void A_save_changed_on_disk_asks_to_reload() =>
        Assert.IsType<SaveDecision.Reload>(MainViewModel.Decide(false, changedOnDisk: true, overrides: 0, writeAnyway: true, allowRunningGame: false));

    [Fact]
    public void Overrides_need_write_anyway()
    {
        Assert.IsType<SaveDecision.Confirm>(MainViewModel.Decide(false, false, overrides: 2, writeAnyway: false, allowRunningGame: false));
        Assert.IsType<SaveDecision.Write>(MainViewModel.Decide(false, false, overrides: 2, writeAnyway: true, allowRunningGame: false));
    }

    [Fact]
    public void Clean_edits_are_written() =>
        Assert.IsType<SaveDecision.Write>(MainViewModel.Decide(false, false, 0, false, false));
}
