using Drydock.Core;

namespace Drydock.Tests;

public class SaveRoundTripTests
{
    static readonly Lazy<SaveArchive> Save = new(() => SaveArchive.Load(TestGame.SavePath!));

    [SaveFact]
    public void Every_amount_reencodes_to_the_same_bytes()
    {
        var archive = Save.Value;
        var stacks = Inventory.FindAll(archive).SelectMany(i => i.Items).ToList();
        Assert.NotEmpty(stacks);
        foreach (var stack in stacks)
        {
            var original = archive.MainChunk.AsSpan(stack.Amount.Start, stack.Amount.End - stack.Amount.Start).ToArray();
            Assert.Equal(original, ItemCodec.Amount(ItemCodec.ReadAmount(original)));
        }
    }

    [SaveFact]
    public void Every_item_list_count_matches_its_elements()
    {
        var archive = Save.Value;
        foreach (var inventory in Inventory.FindAll(archive))
        {
            var items = inventory.ItemsField;
            Assert.Equal(items.Members.Length, ItemCodec.ReadCount(archive.MainChunk.AsSpan(items.Start, 4)));
            if (items.Members.Length > 0)
                Assert.Equal(items.Start + 4, items.Members[0].Start);
        }
    }

    [SaveFact]
    public void Rebuild_without_edits_keeps_the_main_chunk()
    {
        var archive = Save.Value;
        var rebuilt = new SaveEditor(archive).Build();
        Assert.Equal(archive.MainChunk, rebuilt.MainChunk);
        Assert.True(rebuilt.File.Verify(out var problem), problem);
    }

    [SaveFact]
    public void Edits_land_where_intended()
    {
        var archive = Save.Value;
        var inventory = Inventory.FindAll(archive).First(i => i.Items.Count >= 2);
        var before = inventory.Items;
        var newItem = Guid.Parse("cb651320-76fb-413d-bdde-c46f35b7180d"); // iron ore
        var editor = new SaveEditor(archive);
        editor.SetAmount(before[0], 123.5m);
        editor.Remove(inventory, before[1]);
        editor.AddItem(inventory, newItem, 42);

        var rebuilt = editor.Build();
        var after = Inventory.FindAll(rebuilt).Single(i => i.Owner == inventory.Owner && i.Slot == inventory.Slot && i.Field.Start == inventory.Field.Start);
        var items = after.Items;
        bool hadIron = before.Any(s => s.Definition == newItem && !s.HasMetaData);

        Assert.Equal(before.Count - 1 + (hadIron ? 0 : 1), items.Count);
        Assert.Equal(123.5m, (decimal)items[0].Amount.Value!);
        Assert.Equal(before.Skip(2).Select(s => s.Definition),items.Skip(1).Take(before.Count - 2).Select(s => s.Definition));
        Assert.Contains(items, s => s.Definition == newItem);
        Assert.Equal(Inventory.FindAll(archive).Count, Inventory.FindAll(rebuilt).Count);
    }

    static readonly Lazy<GameContent> Content = new(() => GameContent.Load(GameInstall.RootDir));

    [SaveFact]
    public void Unstackable_items_get_one_stack_per_unit()
    {
        var archive = Save.Value;
        var catalog = Content.Value.Items;
        var module = catalog.Search("Power Module").Single();
        Assert.False(module.Stackable);
        var inventory = Inventory.FindAll(archive).First(i => i.Object.IsPlayer);
        int before = inventory.Items.Count(s => s.Definition == module.Guid);

        var editor = new SaveEditor(archive, catalog);
        editor.SetTotal(inventory, module.Guid, before + 3);
        var after = Inventory.FindAll(editor.Build()).First(i => i.Object.IsPlayer && i.Slot == inventory.Slot);

        var modules = after.Items.Where(s => s.Definition == module.Guid).ToList();
        Assert.Equal(before + 3, modules.Count);
        Assert.All(modules, s => Assert.Equal(1m, (decimal)s.Amount.Value!));
        Assert.Throws<ArgumentException>(() => editor.AddItem(inventory, module.Guid, 1.5m));
        Assert.Throws<ArgumentException>(() => editor.AddItem(inventory, catalog.Search("Steel Plate").Single().Guid, 0.5m));
    }

    [SaveFact]
    public void Player_and_named_grids_are_found()
    {
        var inventories = Inventory.FindAll(Save.Value);
        Assert.Contains(inventories, i => i.Object.IsPlayer && Content.Value.SlotName(i.Slot) == "main");
        Assert.NotNull(Inventory.PlayerOwnerId(inventories));
        Assert.Contains(inventories, i => i.Object.Kind is ObjectKind.Ship or ObjectKind.Station && i.Block != null);
    }
}
