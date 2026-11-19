using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

/// <summary>
/// Edits inventories by splicing bytes into the main chunk at offsets taken from the parsed tree.
/// The result is re-parsed before it is returned.
/// </summary>
public sealed class SaveEditor(SaveArchive archive, ItemCatalog? catalog = null)
{
    readonly record struct Cut(int Start, int End, byte[] Bytes);

    readonly List<Cut> _splices = [];
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
    /// Add <paramref name="amount"/> of an item, topping up a stack where the item stacks.
    /// Unstackable items get one stack per unit, cloned from one in the save to keep its metadata.
    /// </summary>
    public void AddItem(Inventory inventory, Guid definition, decimal amount)
    {
        CheckAmount(definition, amount);
        var end = inventory.ItemsField.End;
        if (UnitStacks(definition))
        {
            AddStacks(inventory, definition, (int)amount,
                Template(definition) is { } template ? StackBytes(template) : ItemCodec.Stack(definition, 1));
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

    /// <summary>Insert <paramref name="count"/> copies of a complete stack encoding at the end of the item list.</summary>
    public void AddStacks(Inventory inventory, Guid definition, int count, byte[] stack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (stack.Length < 17 || new Guid(stack.AsSpan(1, 16)) != definition)
            throw new ArgumentException("The stack encoding is not for this item.", nameof(stack));
        var end = inventory.ItemsField.End;
        for (int i = 0; i < count; i++)
            Add(end, end, stack);
        ChangeCount(inventory, count);
    }

    /// <summary>A stack's bytes with the amount set to 1, to add copies of it.</summary>
    public byte[] StackBytes(ItemStack source)
    {
        var bytes = Archive.MainChunk[source.Field.Start..source.Field.End];
        ItemCodec.Amount(1).CopyTo(bytes, source.Amount.Start - source.Field.Start);
        return bytes;
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
        _splices.Add(new Cut(start, end, bytes));
    }

    /// <summary>Apply all edits and return the re-parsed result. The editor's own archive is left as it was.</summary>
    public SaveArchive Build() => Archive.WithMainChunk(Splice());

    /// <summary>The main chunk with all edits applied.</summary>
    public byte[] Splice()
    {
        var splices = new List<Cut>(_splices);
        foreach (var (items, count) in _counts)
            splices.Add(new Cut(items.Start, items.Start + 4, ItemCodec.Count(count)));
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
        return output.ToArray();
    }
}
