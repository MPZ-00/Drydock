using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

/// <summary>
/// Edits inventories by splicing bytes into the main chunk at offsets taken from the parsed tree.
/// Nothing outside the touched item lists moves except by the size change, and the result is re-parsed before it is returned.
/// With a <see cref="ItemCatalog"/> it also knows which items stack and which come in whole units.
/// </summary>
public sealed class SaveEditor(SaveArchive archive, ItemCatalog? catalog = null)
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
    /// Add <paramref name="amount"/> of an item. Stackable items top up an existing stack or get a new one.
    /// Tools, guns and power modules get one stack per unit like the game keeps them, copied from an existing stack of the
    /// same item in this save when there is one so they keep its metadata (ammo, fill ratio).
    /// </summary>
    public void AddItem(Inventory inventory, Guid definition, decimal amount)
    {
        CheckAmount(definition, amount);
        var end = inventory.ItemsField.End;
        if (UnitStacks(definition))
        {
            byte[] bytes;
            if (Template(definition) is { } template)
            {
                bytes = Archive.MainChunk[template.Field.Start..template.Field.End];
                ItemCodec.Amount(1).CopyTo(bytes, template.Amount.Start - template.Field.Start);
            }
            else
                bytes = ItemCodec.Stack(definition, 1);
            for (int i = 0; i < (int)amount; i++)
                Add(end, end, bytes);
            ChangeCount(inventory, (int)amount);
            return;
        }
        var existing = inventory.Items.FirstOrDefault(s => s.Definition == definition && !s.HasMetaData);
        if (existing != null)
        {
            SetAmount(existing, CurrentAmount(existing) + amount);
            return;
        }
        Add(end, end, ItemCodec.Stack(definition, amount));
        ChangeCount(inventory, +1);
    }

    /// <summary>Make the inventory hold exactly <paramref name="amount"/> of an item, adding or removing stacks as needed.</summary>
    public void SetTotal(Inventory inventory, Guid definition, decimal amount)
    {
        if (amount > 0)
            CheckAmount(definition, amount);
        var stacks = inventory.Items.Where(s => s.Definition == definition).ToList();
        if (stacks.Count == 0 || UnitStacks(definition))
        {
            decimal current = stacks.Sum(CurrentAmount);
            if (amount > current)
                AddItem(inventory, definition, amount - current);
            // drop whole stacks from the end, newest first, then trim the last one
            for (int i = stacks.Count - 1; i >= 0 && current > amount; i--)
            {
                decimal have = CurrentAmount(stacks[i]);
                if (current - have >= amount)
                    Remove(inventory, stacks[i]);
                else
                    SetAmount(stacks[i], have - (current - amount));
                current -= Math.Min(have, current - amount);
            }
            return;
        }
        if (amount == 0)
            Remove(inventory, stacks[0]);
        else if (CurrentAmount(stacks[0]) != amount)
            SetAmount(stacks[0], amount);
        foreach (var extra in stacks.Skip(1))
            Remove(inventory, extra);
    }

    bool UnitStacks(Guid definition) => catalog?.Find(definition) is { Stackable: false } || Template(definition) != null;

    void CheckAmount(Guid definition, decimal amount)
    {
        bool whole = amount == decimal.Truncate(amount);
        if (UnitStacks(definition) && !(whole && amount <= 10_000))
            throw new ArgumentException("this item comes one per stack, use a whole number up to 10000.");
        if (catalog?.Find(definition) is { Divisible: false } && !whole)
            throw new ArgumentException("only ore and ingots come in fractions, use a whole number.");
    }

    ItemStack? Template(Guid definition) =>
        (_templates ??= Inventory.FindAll(Archive).SelectMany(i => i.Items).Where(s => s.HasMetaData)
            .GroupBy(s => s.Definition).ToDictionary(g => g.Key, g => g.First()))
        .GetValueOrDefault(definition);

    Dictionary<Guid, ItemStack>? _templates;

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
