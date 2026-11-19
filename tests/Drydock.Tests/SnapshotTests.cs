using Drydock.Core;

namespace Drydock.Tests;

public class SnapshotTests
{
    static World W => WorldTests.Loaded.Value;

    [SaveFact]
    public void Player_comes_first_and_is_yours()
    {
        var first = Snapshot.Of(W)[0];
        Assert.True(first.IsPlayer);
        Assert.True(first.Yours);
        Assert.Contains(first.Inventories, i => i.Ref == "player:consumables" && i.MaxMass == 25);
    }

    [SaveFact]
    public void Stateful_items_get_a_row_per_stack()
    {
        var rows = Snapshot.Of(W).SelectMany(o => o.Inventories).SelectMany(i => i.Rows).Where(r => W.HasState(r.Item)).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.NotNull(r.Stack));
        Assert.All(rows, r => Assert.Equal(1m, r.Amount));
    }

    [SaveFact]
    public void Plain_items_get_one_row_per_item()
    {
        foreach (var inv in Snapshot.Of(W).SelectMany(o => o.Inventories))
        {
            var plain = inv.Rows.Where(r => r.Stack == null).ToList();
            Assert.Equal(plain.Count, plain.Select(r => r.Item).Distinct().Count());
            Assert.All(plain, r => Assert.Equal(
                W.Inventories[inv.Index].Items.Where(s => s.Definition == r.Item).Sum(s => (decimal)s.Amount.Value!), r.Amount));
        }
    }

    [SaveFact]
    public void Snapshot_survives_a_world_without_owner()
    {
        var objects = Snapshot.Of(W.WithoutOwner());
        Assert.All(objects.Where(o => o.Yours), o => Assert.True(o.IsPlayer));
        Assert.Contains(objects, o => !o.Yours);
    }
}
