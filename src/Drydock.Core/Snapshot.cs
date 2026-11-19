using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Drydock.Tests")]

namespace Drydock.Core;

public sealed record ObjectView(string Name, ObjectKind Kind, bool IsPlayer, bool Yours, IReadOnlyList<InventoryView> Inventories);

/// <param name="Block">The block label, null on characters.</param>
public sealed record InventoryView(
    int Index, string Ref, string? Block, string Slot, double Mass, double? MaxMass, int Stacks, int? MaxStacks, IReadOnlyList<RowView> Rows);

/// <param name="Stack">The stack's index in the inventory for items with state, which get one row per stack. Null for plain items.</param>
/// <param name="State">A short text of the stack's own data, null for plain items.</param>
public sealed record RowView(Guid Item, string Name, decimal Amount, double Mass, int? Stack, string? State);

/// <summary>Plain copies of a world's inventories, safe to hand to another thread.</summary>
public static class Snapshot
{
    public static IReadOnlyList<ObjectView> Of(World world) =>
        world.Inventories.Select((inv, index) => (inv, index))
            .GroupBy(x => x.inv.Object, ReferenceEqualityComparer.Instance)
            .Select(g =>
            {
                var o = (WorldObject)g.Key!;
                return new ObjectView(world.ObjectName(o), o.Kind, o.IsPlayer, world.IsYours(o),
                    g.Select(x => View(world, x.inv, x.index)).ToList());
            })
            .OrderByDescending(o => o.IsPlayer)
            .ThenBy(o => o.Kind)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    static InventoryView View(World world, Inventory inv, int index)
    {
        var rules = world.Rules(inv);
        return new InventoryView(index, world.Ref(inv), inv.Block == null ? null : world.BlockLabel(inv),
            world.Content.SlotName(inv.Slot), world.Mass(inv), rules?.MaxMass, inv.Items.Count, rules?.MaxStacks, Rows(world, inv));
    }

    static List<RowView> Rows(World world, Inventory inv)
    {
        var rows = new List<RowView>();
        var items = inv.Items;
        foreach (var group in items.Select((s, i) => (s, i)).GroupBy(x => x.s.Definition))
        {
            var info = world.Content.Items.Find(group.Key);
            var name = info?.Name ?? group.Key.ToString();
            double mass = info?.MassPerUnit ?? 0;
            if (world.HasState(group.Key))
            {
                foreach (var (s, i) in group)
                    rows.Add(new RowView(group.Key, name, Amount(s), (double)Amount(s) * mass, i,
                        State(world, s)));
                continue;
            }
            decimal total = group.Sum(x => Amount(x.s));
            rows.Add(new RowView(group.Key, name, total, (double)total * mass, null, null));
        }
        return rows;
    }

    static decimal Amount(ItemStack s) => (decimal)s.Amount.Value!;

    static string? State(World world, ItemStack s) =>
        !s.HasMetaData ? null
        : StackStates.For(world.Content, s) is { } codec ? codec.Describe(s)
        : s.MetaData!.GetShortValueString();
}
