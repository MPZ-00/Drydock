using Drydock.Core;

namespace Drydock.Tests;

public class StackStateTests
{
    static World W => WorldTests.Loaded.Value;

    static (int Inventory, int Stack, ItemStack Stack_) First(string metadataType)
    {
        for (int i = 0; i < W.Inventories.Count; i++)
            for (int s = 0; s < W.Inventories[i].Items.Count; s++)
                if (W.Inventories[i].Items[s] is { HasMetaData: true } st && st.MetaData!.TypeName().EndsWith(metadataType))
                    return (i, s, st);
        throw new InvalidOperationException($"no {metadataType} stack in the test save");
    }

    static ItemStack Rebuilt(int inventory, byte[] stack)
    {
        var w = W;
        var editor = new SaveEditor(w.Archive, w.Content.Items);
        editor.AddStacks(w.Inventories[inventory], new Guid(stack.AsSpan(1, 16)), 1, stack);
        return Inventory.FindAll(editor.Build())[inventory].Items[^1];
    }

    [SaveFact]
    public void Charge_reads_as_a_percent()
    {
        var (_, _, cell) = First(".ResourceContainerItemMetaData");
        var codec = StackStates.For(W.Content, cell)!;
        Assert.Matches(@"^\d{1,3}%$", codec.Describe(cell));
        Assert.Equal(1m, codec.Max(cell));
    }

    [SaveFact]
    public void Charge_roundtrips()
    {
        var (inv, _, cell) = First(".ResourceContainerItemMetaData");
        var codec = StackStates.For(W.Content, cell)!;
        Assert.Equal("75%", codec.Describe(Rebuilt(inv, codec.Encode(W.Archive, cell, 0.75m))));
        Assert.Equal("0%", codec.Describe(Rebuilt(inv, codec.Encode(W.Archive, cell, 0))));
    }

    [SaveFact]
    public void Ammo_max_comes_from_the_ammo_definition()
    {
        var pistol = W.Content.Items.Search("Pistol").First(i => i.Name == "Pistol");
        var (_, _, gun) = First(".GunItemMetaData");
        var stack = W.Inventories.SelectMany(i => i.Items).FirstOrDefault(s => s.Definition == pistol.Guid && s.HasMetaData) ?? gun;
        var codec = StackStates.For(W.Content, stack)!;
        if (stack.Definition == pistol.Guid)
            Assert.Equal(15m, codec.Max(stack));
        Assert.True(codec.Max(stack) >= 1);
        Assert.True(codec.WholeNumbers);
    }

    [SaveFact]
    public void Ammo_roundtrips()
    {
        var (inv, _, gun) = First(".GunItemMetaData");
        var codec = StackStates.For(W.Content, gun)!;
        Assert.Equal($"0/{codec.Max(gun)}", codec.Describe(Rebuilt(inv, codec.Encode(W.Archive, gun, 0))));
        Assert.Throws<ArgumentException>(() => codec.Encode(W.Archive, gun, codec.Max(gun) + 1));
        Assert.Throws<ArgumentException>(() => codec.Encode(W.Archive, gun, 0.5m));
    }

    [SaveFact]
    public void A_value_state_goes_through_the_plan()
    {
        var (inv, stack, cell) = First(".ResourceContainerItemMetaData");
        var plan = new EditPlan(W);
        plan.Apply(new AddStacks(inv, cell.Definition, 2, new ValueState(0.5m)));
        var built = W.WithArchive(plan.Replay().Build());

        Assert.Null(plan.Check(built));
        var added = built.Inventories[inv].Items.TakeLast(2).ToList();
        Assert.All(added, s => Assert.Equal("50%", StackStates.For(W.Content, s)!.Describe(s)));
    }

    [SaveFact]
    public void Snapshot_rows_show_the_decoded_state()
    {
        var (inv, stack, _) = First(".ResourceContainerItemMetaData");
        var row = Snapshot.Of(W).SelectMany(o => o.Inventories).Single(i => i.Index == inv).Rows.Single(r => r.Stack == stack);
        Assert.Matches(@"^\d{1,3}%$", row.State);
    }
}
