using Drydock.Core;

namespace Drydock.App.ViewModels;

/// <summary>One line of the inventory tree: an object, a block, or a slot. Leaves carry the inventory.</summary>
public sealed class TreeNode(string label, IReadOnlyList<TreeNode> children, InventoryView? inventory, bool hasPending,
    IReadOnlyList<InventoryView>? inventories = null) : ObservableObject
{
    /// <summary>What the pane shows for this line: the leaf's inventory, all of a block's, or none for an object.</summary>
    public IReadOnlyList<InventoryView> Inventories { get; } = inventories ?? (inventory is { } own ? [own] : []);

    public string Label { get; } = label;
    public IReadOnlyList<TreeNode> Children { get; } = children;
    public InventoryView? Inventory { get; } = inventory;
    public bool HasPending { get; } = hasPending;

    bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }
}

public static class TreeBuilder
{
    public static IReadOnlyList<TreeNode> Build(
        IReadOnlyList<ObjectView> objects, bool showAll, string filter, ISet<int> pending, ISet<string>? expanded = null)
    {
        filter = filter.Trim();
        bool filtering = filter.Length > 0;
        bool Matches(string text) => !filtering || text.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var result = new List<TreeNode>();
        foreach (var o in objects)
        {
            if (!showAll && !o.Yours)
                continue;
            var inventories = o.Inventories.Where(i => showAll || o.IsPlayer || i.Rows.Count > 0).ToList();
            bool objectMatches = Matches(o.Name) || (o.IsPlayer && Matches("Player"));
            var children = new List<TreeNode>();
            foreach (var block in inventories.GroupBy(i => i.Block))
            {
                if (block.Key == null)
                {
                    children.AddRange(block.Where(i => objectMatches || Matches(i.Slot)).Select(i => Leaf(i.Slot, i, pending)));
                    continue;
                }
                bool blockMatches = objectMatches || Matches(block.Key);
                var slots = block.Where(i => blockMatches || Matches(i.Slot)).ToList();
                if (slots.Count == 0)
                    continue;
                // counted before empty ones are hidden, so a generator with an empty input keeps its slot names
                if (o.Inventories.Count(i => i.Block == block.Key) == 1)
                    children.Add(Leaf(block.Key, slots[0], pending));
                else
                    children.Add(Branch(block.Key, slots.Select(i => Leaf(i.Slot, i, pending)).ToList(),
                        filtering || expanded?.Contains($"{Label(o)}/{block.Key}") == true,
                        o.Inventories.Where(i => i.Block == block.Key).ToList()));
            }
            if (children.Count > 0)
                result.Add(Branch(Label(o), children, filtering || o.IsPlayer || expanded?.Contains(Label(o)) == true));
        }
        return result;
    }

    static string Label(ObjectView o) => o.IsPlayer ? "Player" : $"{o.Name} ({o.Kind.ToString().ToLowerInvariant()})";

    /// <summary>Keys ("Aquila (ship)", "Aquila (ship)/Smelter") of the open branches, to keep them open across rebuilds.</summary>
    public static ISet<string> ExpandedKeys(IEnumerable<TreeNode> roots) =>
        roots.Where(r => r.IsExpanded).Select(r => r.Label)
            .Concat(roots.SelectMany(r => r.Children.Where(c => c.IsExpanded).Select(c => $"{r.Label}/{c.Label}")))
            .ToHashSet();

    static TreeNode Leaf(string label, InventoryView inventory, ISet<int> pending) =>
        new(label, [], inventory, pending.Contains(inventory.Index));

    static TreeNode Branch(string label, IReadOnlyList<TreeNode> children, bool expanded, IReadOnlyList<InventoryView>? inventories = null) =>
        new(label, children, null, children.Any(c => c.HasPending), inventories ?? []) { IsExpanded = expanded };
}
