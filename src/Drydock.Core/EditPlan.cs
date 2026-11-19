namespace Drydock.Core;

/// <summary>A pending change to one inventory, by its index in <see cref="World.Inventories"/>.</summary>
public abstract record Edit(int Inventory);

/// <summary>Make a plain item's total in the inventory exactly <paramref name="Amount"/>; 0 removes it.</summary>
public sealed record SetTotal(int Inventory, Guid Item, decimal Amount) : Edit(Inventory);

/// <summary>Remove one stack of an item with state, by its index in the loaded inventory.</summary>
public sealed record RemoveStack(int Inventory, int Stack) : Edit(Inventory);

public sealed record AddStacks(int Inventory, Guid Item, int Count, StackState State) : Edit(Inventory);

public abstract record StackState;

/// <summary>No metadata, the game fills in its default.</summary>
public sealed record NewState : StackState;

/// <summary>An exact copy of a stack in the loaded save.</summary>
public sealed record CopyState(int Inventory, int Stack) : StackState;

/// <summary>A value such as charge or ammo; 0 is empty.</summary>
public sealed record ValueState(decimal Value) : StackState;

/// <summary>An edit that breaks a game rule and needs "write anyway".</summary>
public sealed record Override(int Inventory, string Message);

/// <summary>
/// Pending edits as intents, replayed onto a fresh <see cref="SaveEditor"/> whenever a preview or the save is built,
/// since one editor refuses two changes to the same bytes.
/// </summary>
public sealed class EditPlan(World world)
{
    readonly List<Edit> _edits = [];

    public World World { get; } = world;
    public IReadOnlyList<Edit> Edits => _edits;

    public void Apply(Edit edit)
    {
        switch (edit)
        {
            case SetTotal set:
                ApplySet(set);
                break;
            case RemoveStack remove:
                var stack = Stack(remove.Inventory, remove.Stack);
                RequireState(stack.Definition);
                if (!_edits.Contains(remove))
                    _edits.Add(remove);
                break;
            case AddStacks add:
                ApplyAdd(add);
                break;
            default:
                throw new ArgumentException($"Unknown edit {edit.GetType().Name}.");
        }
    }

    void ApplySet(SetTotal set)
    {
        if (World.HasState(set.Item))
            throw new ArgumentException("this item has its own state, remove or add single stacks instead.");
        var inventory = World.Inventories[set.Inventory];
        // validates the amount with the editor's own rules
        new SaveEditor(World.Archive, World.Content.Items).SetTotal(inventory, set.Item, set.Amount);
        _edits.RemoveAll(e => e is SetTotal s && s.Inventory == set.Inventory && s.Item == set.Item);
        if (set.Amount != LoadedTotal(set.Inventory, set.Item))
            _edits.Add(set);
    }

    void ApplyAdd(AddStacks add)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(add.Count);
        if (add.Count > 10_000)
            throw new ArgumentException("this item comes one per stack, use a whole number up to 10000.");
        RequireState(add.Item);
        switch (add.State)
        {
            case CopyState copy when Stack(copy.Inventory, copy.Stack).Definition != add.Item:
                throw new ArgumentException("the stack to copy is a different item.");
            case ValueState value:
                var (codec, template) = ValueCodec(add.Item);
                codec.Encode(World.Archive, template, value.Value);
                break;
        }
        int i = _edits.FindIndex(e => e is AddStacks a && a.Inventory == add.Inventory && a.Item == add.Item && a.State == add.State);
        if (i >= 0)
            _edits[i] = add with { Count = ((AddStacks)_edits[i]).Count + add.Count };
        else
            _edits.Add(add);
    }

    /// <summary>Drops the edit, or for added stacks every pending add of that item and state.</summary>
    public void Revert(Edit edit) => _edits.RemoveAll(e => e == edit
        || (edit is AddStacks a && e is AddStacks b && a with { Count = 0 } == b with { Count = 0 })
        || (edit is SetTotal s && e is SetTotal t && s.Inventory == t.Inventory && s.Item == t.Item));

    public void Clear() => _edits.Clear();

    /// <summary>The codec for an item's state and the stack in the save that new stacks are made from.</summary>
    public (IStackState Codec, ItemStack Template) ValueCodec(Guid item)
    {
        var template = World.Inventories.SelectMany(i => i.Items).FirstOrDefault(s => s.Definition == item && s.HasMetaData);
        if (template != null && StackStates.For(World.Content, template) is { } codec)
            return (codec, template);
        throw new NotSupportedException("this item's state can't be set, use New or Copy.");
    }

    void RequireState(Guid item)
    {
        if (!World.HasState(item))
            throw new ArgumentException("this item has no state of its own, set its total instead.");
    }

    ItemStack Stack(int inventory, int stack)
    {
        var items = World.Inventories[inventory].Items;
        if (stack < 0 || stack >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(stack));
        return items[stack];
    }

    decimal LoadedTotal(int inventory, Guid item) =>
        World.Inventories[inventory].Items.Where(s => s.Definition == item).Sum(s => (decimal)s.Amount.Value!);

    public SaveEditor Replay()
    {
        var editor = new SaveEditor(World.Archive, World.Content.Items);
        foreach (var edit in _edits)
        {
            var inventory = World.Inventories[edit.Inventory];
            switch (edit)
            {
                case SetTotal set:
                    editor.SetTotal(inventory, set.Item, set.Amount);
                    break;
                case RemoveStack remove:
                    editor.Remove(inventory, inventory.Items[remove.Stack]);
                    break;
                case AddStacks add:
                    var bytes = add.State switch
                    {
                        CopyState copy => editor.StackBytes(Stack(copy.Inventory, copy.Stack)),
                        ValueState value when ValueCodec(add.Item) is var (codec, template) => codec.Encode(World.Archive, template, value.Value),
                        _ => ItemCodec.Stack(add.Item, 1),
                    };
                    editor.AddStacks(inventory, add.Item, add.Count, bytes);
                    break;
            }
        }
        return editor;
    }

    public World Preview() => _edits.Count == 0 ? World : World.WithArchive(World.Archive.Preview(Replay().Splice()));

    public IReadOnlyList<Override> Overrides(World preview)
    {
        var result = new List<Override>();
        foreach (var edit in _edits)
        {
            Guid? added = edit switch
            {
                SetTotal s when s.Amount > LoadedTotal(s.Inventory, s.Item) => s.Item,
                AddStacks a => a.Item,
                _ => null,
            };
            if (added is { } guid && World.Content.Items.Find(guid) is { } item && World.Accepts(edit.Inventory, item) == false)
                result.Add(new Override(edit.Inventory, World.Rejection(edit.Inventory, item)));
        }
        foreach (int i in _edits.Select(e => e.Inventory).Distinct())
            if (World.OverCapacity(World.Inventories[i], preview.Inventories[i]) is { } message)
                result.Add(new Override(i, message));
        return result.Distinct().ToList();
    }

    /// <summary>
    /// Checks a built save against the intents without trusting the editor: plain totals, stack counts of items
    /// with state, and untouched inventories byte for byte. Null when all match.
    /// </summary>
    public string? Check(World built)
    {
        if (built.Inventories.Count != World.Inventories.Count)
            return "The built save has a different number of inventories.";
        var touched = _edits.Select(e => e.Inventory).ToHashSet();
        for (int i = 0; i < World.Inventories.Count; i++)
        {
            if (touched.Contains(i))
                continue;
            var before = World.Inventories[i].ItemsField;
            var after = built.Inventories[i].ItemsField;
            if (!World.Archive.MainChunk.AsSpan(before.Start, before.End - before.Start)
                    .SequenceEqual(built.Archive.MainChunk.AsSpan(after.Start, after.End - after.Start)))
                return $"{World.Ref(World.Inventories[i])} changed although it wasn't edited.";
        }
        foreach (var set in _edits.OfType<SetTotal>())
        {
            decimal total = built.Inventories[set.Inventory].Items.Where(s => s.Definition == set.Item).Sum(s => (decimal)s.Amount.Value!);
            if (total != set.Amount)
                return $"{World.Ref(World.Inventories[set.Inventory])} should hold {set.Amount} {Name(set.Item)}, the built save has {total}.";
        }
        var stateful = _edits.Where(e => e is not SetTotal)
            .Select(e => (e.Inventory, Item: e is AddStacks a ? a.Item : Stack(e.Inventory, ((RemoveStack)e).Stack).Definition))
            .Distinct();
        foreach (var (inv, item) in stateful)
        {
            int expected = World.Inventories[inv].Items.Count(s => s.Definition == item)
                - _edits.OfType<RemoveStack>().Count(r => r.Inventory == inv && Stack(inv, r.Stack).Definition == item)
                + _edits.OfType<AddStacks>().Where(a => a.Inventory == inv && a.Item == item).Sum(a => a.Count);
            int actual = built.Inventories[inv].Items.Count(s => s.Definition == item);
            if (actual != expected)
                return $"{World.Ref(World.Inventories[inv])} should hold {expected} stacks of {Name(item)}, the built save has {actual}.";
        }
        return null;
    }

    string Name(Guid item) => World.Content.Items.Find(item)?.Name ?? item.ToString();
}
