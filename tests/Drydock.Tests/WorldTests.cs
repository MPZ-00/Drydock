using Drydock.Core;

namespace Drydock.Tests;

public class WorldTests
{
    internal static readonly Lazy<World> Loaded = new(() => World.Load(TestGame.SavePath!, GameContent.Load(GameInstall.RootDir)));

    [SaveFact]
    public void Duplicate_blocks_are_numbered_by_position()
    {
        var w = Loaded.Value;
        var labels = w.Inventories.Where(i => i.Block != null).Select(w.BlockLabel).Distinct().ToList();
        Assert.Contains(labels, l => l.EndsWith(" (1)") && labels.Contains(l[..^4] + " (2)"));
    }

    [SaveFact]
    public void Ref_names_the_player_and_slots()
    {
        var w = Loaded.Value;
        var player = w.Inventories.Where(i => i.Object.IsPlayer).ToList();
        Assert.Contains(player, i => w.Ref(i) == "player");
        Assert.Contains(player, i => w.Ref(i) == "player:consumables");
    }

    [SaveFact]
    public void WithArchive_keeps_labels_for_unchanged_blocks()
    {
        var w = Loaded.Value;
        var rebuilt = w.WithArchive(new SaveEditor(w.Archive).Build());
        Assert.Equal(w.Inventories.Select(w.Ref), rebuilt.Inventories.Select(rebuilt.Ref));
    }
}
