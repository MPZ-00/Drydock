using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

/// <summary>
/// Edits inventories by splicing bytes into the main chunk at offsets taken from the parsed tree.
/// Nothing outside the touched item lists moves except by the size change, and the result is re-parsed before it is returned.
/// </summary>
public sealed class SaveEditor(SaveArchive archive)
{
    readonly record struct Splice(int Start, int End, byte[] Bytes);

    readonly List<Splice> _splices = [];
    readonly Dictionary<SerializedField, int> _counts = [];

    public SaveArchive Archive { get; } = archive;

    public bool HasChanges => _splices.Count > 0;

    public void SetAmount(ItemStack stack, decimal amount)
    {
        var field = stack.Amount;
        Add(field.Start, field.End, ItemCodec.Amount(amount));
    }

    public void Remove(Inventory inventory, ItemStack stack)
    {
        Add(stack.Field.Start, stack.Field.End, []);
        ChangeCount(inventory, -1);
    }

    /// <summary>
    /// Add <paramref name="amount"/> of an item. Tops up an existing stack without metadata, otherwise appends a new stack.
    /// A new stack copies the metadata of an existing stack of the same item in this save if there is one (gun ammo, fill ratio).
    /// </summary>
    public void AddItem(Inventory inventory, Guid definition, decimal amount)
    {
        var existing = inventory.Items.FirstOrDefault(s => s.Definition == definition && !s.HasMetaData);
        if (existing != null)
        {
            SetAmount(existing, CurrentAmount(existing) + amount);
            return;
        }
        var end = inventory.ItemsField.End;
        Add(end, end, NewStack(definition, amount));
        ChangeCount(inventory, +1);
    }

    byte[] NewStack(Guid definition, decimal amount)
    {
        var template = Inventory.FindAll(Archive).SelectMany(i => i.Items).FirstOrDefault(s => s.Definition == definition && s.HasMetaData);
        if (template == null)
            return ItemCodec.Stack(definition, amount);
        var bytes = Archive.MainChunk[template.Field.Start..template.Field.End];
        ItemCodec.Amount(amount).CopyTo(bytes, template.Amount.Start - template.Field.Start);
        return bytes;
    }

    public decimal CurrentAmount(ItemStack stack) => ItemCodec.ReadAmount(Archive.MainChunk.AsSpan(stack.Amount.Start, 16));

    void ChangeCount(Inventory inventory, int delta)
    {
        var items = inventory.ItemsField;
        int current = _counts.TryGetValue(items, out var c) ? c : ItemCodec.ReadCount(Archive.MainChunk.AsSpan(items.Start, 4));
        _counts[items] = current + delta;
    }

    void Add(int start, int end, byte[] bytes)
    {
        foreach (var s in _splices)
        {
            bool overlaps = start < s.End && s.Start < end;
            bool sameInsertPoint = start == end && s.Start == s.End && start == s.Start;
            if (overlaps && !sameInsertPoint)
                throw new InvalidOperationException("Two edits touch the same bytes.");
        }
        _splices.Add(new Splice(start, end, bytes));
    }

    /// <summary>Apply all edits and return the re-parsed result. The editor's own archive is left as it was.</summary>
    public SaveArchive Build()
    {
        var splices = new List<Splice>(_splices);
        foreach (var (items, count) in _counts)
            splices.Add(new Splice(items.Start, items.Start + 4, ItemCodec.Count(count)));
        // stable sort keeps several inserts at one point in the order they were made
        splices = [.. splices.OrderBy(s => s.Start)];

        var source = Archive.MainChunk;
        var output = new MemoryStream(source.Length + splices.Sum(s => s.Bytes.Length - (s.End - s.Start)));
        int pos = 0;
        foreach (var s in splices)
        {
            output.Write(source, pos, s.Start - pos);
            output.Write(s.Bytes);
            pos = s.End;
        }
        output.Write(source, pos, source.Length - pos);
        return Archive.WithMainChunk(output.ToArray());
    }
}
