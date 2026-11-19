using Drydock.Core;

namespace Drydock.Tests;

public class SaveEditorTests
{
    static World W => WorldTests.Loaded.Value;

    static byte[] Bytes(SaveArchive a, Keen.VRage.Library.Serialization.Binary.Reflection.SerializedField f) => a.MainChunk[f.Start..f.End];

    [SaveFact]
    public void Preview_matches_build()
    {
        var w = W;
        var player = w.Inventories.First(i => i.Object.IsPlayer && i.Items.Count > 0);
        var first = player.Items[0];
        var editor = new SaveEditor(w.Archive, w.Content.Items);
        editor.SetTotal(player, first.Definition, player.Items.Where(s => s.Definition == first.Definition).Sum(editor.CurrentAmount) + 1);

        var preview = w.Archive.Preview(editor.Splice());
        var built = editor.Build();

        Assert.True(preview.IsPreview);
        Assert.False(built.IsPreview);
        Assert.Equal(built.MainChunk, preview.MainChunk);
        Assert.Equal(
            Inventory.FindAll(built).Select(i => i.Items.Count),
            Inventory.FindAll(preview).Select(i => i.Items.Count));
    }

    [SaveFact]
    public void Added_copies_keep_the_source_state()
    {
        var w = W;
        var (inv, source) = w.Inventories.SelectMany(i => i.Items.Select(s => (i, s))).First(x => x.s.HasMetaData);
        int index = w.Inventories.ToList().IndexOf(inv);
        var editor = new SaveEditor(w.Archive, w.Content.Items);
        editor.AddStacks(inv, source.Definition, 2, editor.StackBytes(source));

        var built = editor.Build();
        var added = Inventory.FindAll(built)[index].Items.TakeLast(2).ToList();

        Assert.All(added, s => Assert.Equal(Bytes(w.Archive, source.MetaData!), Bytes(built, s.MetaData!)));
        Assert.All(added, s => Assert.Equal(1m, (decimal)s.Amount.Value!));
    }

    [SaveFact]
    public void New_stacks_carry_no_metadata()
    {
        var w = W;
        var cell = w.Content.Items.Search("Power Cell").First(i => i.Name == "Power Cell");
        var player = w.Inventories.First(i => i.Object.IsPlayer && w.Content.SlotName(i.Slot) == "main");
        int index = w.Inventories.ToList().IndexOf(player);
        var editor = new SaveEditor(w.Archive, w.Content.Items);
        editor.AddStacks(player, cell.Guid, 1, ItemCodec.Stack(cell.Guid, 1));

        var last = Inventory.FindAll(editor.Build())[index].Items[^1];
        Assert.Equal(cell.Guid, last.Definition);
        Assert.False(last.HasMetaData);
    }

    [SaveFact]
    public void Previews_are_never_written()
    {
        var w = W;
        var preview = w.Archive.Preview(new SaveEditor(w.Archive).Splice());
        var target = Path.Combine(Directory.CreateTempSubdirectory("drydock-preview-").FullName, "savegame.vrb");
        File.Copy(w.Path, target);

        var e = Assert.Throws<InvalidOperationException>(() => SaveWriter.Write(preview, target));
        Assert.Equal("A preview can't be written.", e.Message);
    }
}
