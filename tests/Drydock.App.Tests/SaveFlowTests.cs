using Drydock.App.ViewModels;

namespace Drydock.App.Tests;

public class SaveFlowTests
{
    [Fact]
    public void A_running_game_wins_over_everything() =>
        Assert.IsType<SaveDecision.Refuse>(MainViewModel.Decide(gameRunning: true, changedOnDisk: true, overrides: 3, writeAnyway: true));

    [Fact]
    public void A_save_changed_on_disk_asks_to_reload() =>
        Assert.IsType<SaveDecision.Reload>(MainViewModel.Decide(false, changedOnDisk: true, overrides: 0, writeAnyway: true));

    [Fact]
    public void Overrides_need_write_anyway()
    {
        Assert.IsType<SaveDecision.Confirm>(MainViewModel.Decide(false, false, overrides: 2, writeAnyway: false));
        Assert.IsType<SaveDecision.Write>(MainViewModel.Decide(false, false, overrides: 2, writeAnyway: true));
    }

    [Fact]
    public void Clean_edits_are_written() =>
        Assert.IsType<SaveDecision.Write>(MainViewModel.Decide(false, false, 0, false));
}
