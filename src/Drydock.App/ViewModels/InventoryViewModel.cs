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
    bool IsNew = false, string Key = "", string? Icon = null)
{
    public string Status => IsRemoved ? "removed" : IsNew ? "new" : IsChanged ? "edited" : "";

    public string CopyText => string.Join("	", Name, Key, AmountText, MassText, State ?? "");
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
        if (info != null)
            rows = rows.Select(r => info(r.Item) is { } i ? r with { Key = i.Key, Icon = i.Icon } : r).ToList();

        // the same list keeps WPF from rebuilding the rows, which would drop what the user is typing
        IReadOnlyList<RowViewModel> keptRows = previous?.Index == loaded.Index && previous.Rows.SequenceEqual(rows) ? previous.Rows : rows;
        bool massOver = preview.MaxMass is { } maxMass && preview.Mass > maxMass;
        bool stacksOver = preview.MaxStacks is { } maxStacks && preview.Stacks > maxStacks;
        return new InventoryViewModel(
            loaded.Index, loaded.Ref,
            $"{preview.Mass:0.##}{(preview.MaxMass is { } m ? $"/{m:0.##}" : "")}kg",
            preview.MaxMass is { } mm and > 0 ? Math.Min(1, preview.Mass / mm) : 0, massOver,
            $"{preview.Stacks}{(preview.MaxStacks is { } s ? $"/{s}" : "")} stacks",
            preview.MaxStacks is { } ms and > 0 ? Math.Min(1, (double)preview.Stacks / ms) : 0, stacksOver,
            keptRows,
            overrides.Where(o => o.Inventory == loaded.Index).Select(o => o.Message).Distinct().ToList());
    }

    static string Kg(double mass) => mass.ToString("0.##", CultureInfo.InvariantCulture) + "kg";
}
