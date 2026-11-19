using Drydock.Core;

namespace Drydock.Tests;

public class RoutingTests
{
    static World W => WorldTests.Loaded.Value;

    static (int Input, int Output)? Smelter()
    {
        var w = W;
        for (int i = 0; i < w.Inventories.Count; i++)
        {
            if (!w.Ref(w.Inventories[i]).EndsWith(":input"))
                continue;
            var block = w.Ref(w.Inventories[i])[..^":input".Length];
            int output = Enumerable.Range(0, w.Inventories.Count).FirstOrDefault(j => w.Ref(w.Inventories[j]) == block + ":output", -1);
            if (output >= 0 && w.Content.Rules(w.Inventories[i].Composite, w.Inventories[i].Slot)?.Types.Contains("Ore") == true)
                return (i, output);
        }
        return null;
    }

    static ItemInfo Item(string name) => W.Content.Items.Search(name).Single();

    [SaveFact]
    public void Ore_for_a_smelter_routes_to_its_input()
    {
        if (Smelter() is not var (input, _))
            return; // this save has no smelter
        var block = W.Ref(W.Inventories[input])[..^":input".Length];
        Assert.Equal([input], W.Route(W.Match(block), Item("Iron Ore"), slotNamed: false));
    }

    [SaveFact]
    public void Medikit_for_player_routes_to_consumables()
    {
        var routed = W.Route(W.Match("player"), Item("Medikit"), slotNamed: false);
        Assert.Equal("player:consumables", W.Ref(W.Inventories[Assert.Single(routed)]));
    }

    [SaveFact]
    public void Rejection_names_the_right_slot()
    {
        if (Smelter() is not var (input, output))
            return;
        Assert.Equal(
            $"{W.Ref(W.Inventories[output])} doesn't take {Item("Iron Ore").Name}. It goes in \"{W.Ref(W.Inventories[input])}\".",
            W.Rejection(output, Item("Iron Ore")));
    }

    [SaveFact]
    public void Over_capacity_only_when_growing()
    {
        var w = W;
        int n = w.Match("player:consumables").Single();
        var inv = w.Inventories[n];
        Assert.Null(w.OverCapacity(inv, inv));

        var editor = new SaveEditor(w.Archive, w.Content.Items);
        editor.SetTotal(inv, Item("Medikit").Guid, 6);
        var after = w.WithArchive(editor.Build());
        var message = after.OverCapacity(inv, after.Inventories[n]);
        Assert.True(message is "player:consumables holds 25kg max." or "player:consumables holds 5 stacks max.", message);
    }

    [SaveFact]
    public void Over_capacity_names_a_block_from_the_loaded_world()
    {
        var w = W;
        var iron = Item("Iron Ore");
        int n = Enumerable.Range(0, w.Inventories.Count)
            .First(i => w.Inventories[i].Block != null && w.Accepts(i, iron) == true && w.Rules(w.Inventories[i])!.MaxMass != null);
        var inv = w.Inventories[n];
        var editor = new SaveEditor(w.Archive, w.Content.Items);
        editor.SetTotal(inv, iron.Guid, (decimal)Math.Ceiling(w.Rules(inv)!.MaxMass!.Value / iron.MassPerUnit) + 10);
        var after = w.WithArchive(editor.Build());

        Assert.StartsWith(w.Ref(inv) + " holds ", w.OverCapacity(inv, after.Inventories[n]));
    }
}
