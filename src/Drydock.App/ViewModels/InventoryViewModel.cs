using System.Collections.ObjectModel;
using System.Globalization;
using Drydock.Core;

namespace Drydock.App.ViewModels;

/// <summary>How an item's state reads and how far it goes, for the add dialog and added rows.</summary>
public sealed record StateInfo(decimal Max, bool WholeNumbers, bool Percent)
{
    public string Describe(decimal value) => Percent
        ? (value / Max * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
        : $"{value.ToString("0", CultureInfo.InvariantCulture)}/{Max.ToString("0", CultureInfo.InvariantCulture)}";
}

/// <param name="Edit">The pending edit behind this row, to revert it. Null for unchanged rows.</param>
/// <param name="Stack">The loaded stack index for items with state, to remove that stack.</param>
/// <param name="IsNew">The item isn't in the loaded inventory, the row comes from an edit.</param>
public sealed record RowViewModel(
    Guid Item, string Name, string AmountText, string MassText, string? State,
    bool CanEditAmount, int? Stack, bool IsChanged, bool IsRemoved, Edit? Edit,
    bool IsNew = false, string Key = "", string? Icon = null, int Inventory = 0, string? Slot = null)
{
    public string Status => IsRemoved ? "removed" : IsNew ? "new" : IsChanged ? "edited" : "";

    // added rows go away with Revert
    public bool CanRemove => !IsRemoved && !IsNew;

    /// <summary>Removes the whole row: the one stack for items with state, else the total down to zero.</summary>
    public Edit Removal() => Stack is { } stack ? new RemoveStack(Inventory, stack) : new SetTotal(Inventory, Item, 0);

    public string CopyText => string.Join("\t", Name, Key, AmountText, MassText, State ?? "");
}

/// <summary>The right-hand pane: the selected inventory as it will be after the pending edits.</summary>
public sealed record InventoryViewModel(
    int Index, string Ref, string MassText, double MassFraction, bool MassOver,
    string StacksText, double StacksFraction, bool StacksOver,
    IReadOnlyList<RowViewModel> Rows, IReadOnlyList<string> Overrides)
{
    /// <param name="loaded">The inventory as read from the save, which stack indices refer to.</param>
    /// <param name="preview">The same inventory with the pending edits applied, for totals and limits.</param>
    public static InventoryViewModel Build(
        InventoryView loaded, InventoryView preview, IReadOnlyList<Edit> edits, IReadOnlyList<Override> overrides,
        Func<Guid, string> name, IReadOnlyDictionary<Guid, StateInfo>? states = null, InventoryViewModel? previous = null,
        Func<Guid, ItemInfo?>? info = null)
    {
        var mine = edits.Where(e => e.Inventory == loaded.Index).ToList();
        var rows = new List<RowViewModel>();
        foreach (var row in loaded.Rows)
        {
            if (row.Stack is { } stack)
            {
                var removal = mine.OfType<RemoveStack>().FirstOrDefault(r => r.Stack == stack);
                rows.Add(new RowViewModel(row.Item, row.Name, Amounts.Format(row.Amount), Kg(row.Mass), row.State,
                    CanEditAmount: false, stack, IsChanged: removal != null, IsRemoved: removal != null, removal));
                continue;
            }
            var set = mine.OfType<SetTotal>().FirstOrDefault(s => s.Item == row.Item);
            decimal amount = set?.Amount ?? row.Amount;
            double perUnit = row.Amount == 0 ? 0 : row.Mass / (double)row.Amount;
            rows.Add(new RowViewModel(row.Item, row.Name, Amounts.Format(amount), Kg((double)amount * perUnit), null,
                CanEditAmount: true, null, IsChanged: set != null, IsRemoved: set?.Amount == 0, set));
        }
        foreach (var set in mine.OfType<SetTotal>().Where(s => loaded.Rows.All(r => r.Item != s.Item)))
        {
            var mass = preview.Rows.FirstOrDefault(r => r.Item == set.Item)?.Mass ?? 0;
            rows.Add(new RowViewModel(set.Item, name(set.Item), Amounts.Format(set.Amount), Kg(mass), null,
                CanEditAmount: true, null, IsChanged: true, IsRemoved: false, set, IsNew: true));
        }
        foreach (var add in mine.OfType<AddStacks>())
        {
            var state = add.State switch
            {
                NewState => "default",
                CopyState => "copy",
                ValueState v when states?.GetValueOrDefault(add.Item) is { } described => described.Describe(v.Value),
                ValueState v => v.Value.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
            rows.Add(new RowViewModel(add.Item, name(add.Item), add.Count.ToString(CultureInfo.InvariantCulture), "", state,
                CanEditAmount: false, null, IsChanged: true, IsRemoved: false, add, IsNew: true));
        }
        rows = rows.Select(r => info?.Invoke(r.Item) is { } i ? r with { Key = i.Key, Icon = i.Icon, Inventory = loaded.Index } : r with { Inventory = loaded.Index }).ToList();

        // the same list keeps WPF from rebuilding the rows, which would drop what the user is typing
        IReadOnlyList<RowViewModel> keptRows = previous?.Index == loaded.Index && previous.Rows.SequenceEqual(rows) ? previous.Rows : rows;
        return Create(loaded.Index, loaded.Ref, preview.Mass, preview.MaxMass, preview.Stacks, preview.MaxStacks, keptRows,
            overrides.Where(o => o.Inventory == loaded.Index).Select(o => o.Message).Distinct().ToList());
    }

    /// <summary>
    /// One section for a block whose slots each hold a single stack, like a battery's cells:
    /// the limits add up and each row names its slot.
    /// </summary>
    /// <param name="previews">The slots with the pending edits applied, in the same order as <paramref name="sections"/>.</param>
    public static InventoryViewModel Merge(IReadOnlyList<InventoryViewModel> sections, IReadOnlyList<InventoryView> previews)
    {
        var first = sections[0];
        int cut = first.Ref.LastIndexOf(':');
        var rows = sections.Zip(previews, (s, p) => s.Rows.Select(r => r with { Slot = p.Slot })).SelectMany(r => r).ToList();
        return Create(first.Index, cut > 0 ? first.Ref[..cut] : first.Ref,
            previews.Sum(p => p.Mass), previews.All(p => p.MaxMass != null) ? previews.Sum(p => p.MaxMass!.Value) : null,
            previews.Sum(p => p.Stacks), previews.All(p => p.MaxStacks != null) ? previews.Sum(p => p.MaxStacks!.Value) : null,
            rows, sections.SelectMany(s => s.Overrides).Distinct().ToList())
            // a new item goes to the first empty slot, a full block falls back to the first and the save prompt warns
            with { AddTarget = previews.FirstOrDefault(p => p.Stacks == 0)?.Index ?? first.Index };
    }

    static InventoryViewModel Create(int index, string title, double mass, double? maxMass, int stacks, int? maxStacks,
        IReadOnlyList<RowViewModel> rows, IReadOnlyList<string> overrides) =>
        new(index, title,
            $"{mass:0.##}{(maxMass is { } m ? $"/{m:0.##}" : "")}kg",
            maxMass is { } mm and > 0 ? Math.Min(1, mass / mm) : 0, maxMass is { } over && mass > over,
            $"{stacks}{(maxStacks is { } s ? $"/{s}" : "")} stacks",
            maxStacks is { } ms and > 0 ? Math.Min(1, (double)stacks / ms) : 0, maxStacks is { } most && stacks > most,
            rows, overrides) { AddTarget = index };

    /// <summary>The inventory Add item puts new items into.</summary>
    public int AddTarget { get; init; }

    /// <summary>Tells the pane's list a heading from a row.</summary>
    public bool IsSection => true;

    /// <summary>The rows as copy text, in the order the pane shows them, not the order they were picked.</summary>
    public string CopyRows(IEnumerable<RowViewModel> picked)
    {
        var set = picked.ToHashSet();
        return string.Join(Environment.NewLine, Rows.Where(set.Contains).Select(r => r.CopyText));
    }

    static string Kg(double mass) => mass.ToString("0.##", CultureInfo.InvariantCulture) + "kg";
}

/// <summary>Lays the pane's sections out as one list: each section's heading, then its rows.</summary>
public static class PaneItems
{
    /// <summary>A block whose slots each hold one stack shows as one section; a smelter's input and output stay apart.</summary>
    public static bool Compact(IReadOnlyList<InventoryView> inventories) =>
        inventories.Count > 1 && inventories.All(i => i.MaxStacks == 1);

    public static IReadOnlyList<object> Layout(IReadOnlyList<InventoryViewModel> sections) =>
        sections.SelectMany(s => s.Rows.Cast<object>().Prepend(s)).ToList();

    /// <summary>Changes only the entries that differ, so an unchanged row keeps its text box and selection.</summary>
    public static void Sync(ObservableCollection<object> items, IReadOnlyList<object> next)
    {
        for (int i = 0; i < next.Count; i++)
        {
            if (i >= items.Count)
                items.Add(next[i]);
            else if (!Equals(items[i], next[i]))
                items[i] = next[i];
        }
        while (items.Count > next.Count)
            items.RemoveAt(items.Count - 1);
    }
}
