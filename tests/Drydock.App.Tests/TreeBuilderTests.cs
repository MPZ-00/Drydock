using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App.Tests;

public class TreeBuilderTests
{
    static int _next;

    static InventoryView Inv(string? block, string slot, int rows = 1) =>
        new(_next++, $"{block}:{slot}", block, slot, 0, null, rows, null,
            Enumerable.Range(0, rows).Select(_ => new RowView(Guid.NewGuid(), "Iron", 1, 1, null, null)).ToList());

    static readonly InventoryView Input = Inv("Smelter", "input");
    static readonly InventoryView Output = Inv("Smelter", "output");
    static readonly InventoryView Cargo = Inv("Cargo Container (1)", "main");
    static readonly InventoryView EmptyCargo = Inv("Cargo Container (2)", "main", rows: 0);

    static readonly ObjectView Player = new("player", ObjectKind.Character, IsPlayer: true, Yours: true,
        [Inv(null, "main"), Inv(null, "consumables", rows: 0)]);
    static readonly ObjectView Aquila = new("Aquila", ObjectKind.Ship, false, true, [Input, Output, Cargo, EmptyCargo]);
    static readonly ObjectView Pirate = new("Pirate Base", ObjectKind.Station, false, false, [Inv("Cargo Container", "main")]);

    static readonly IReadOnlyList<ObjectView> World = [Player, Aquila, Pirate];

    static TreeNode Node(IEnumerable<TreeNode> nodes, string label) => nodes.Single(n => n.Label == label);

    [Fact]
    public void Single_main_slot_blocks_are_leaves()
    {
        var aquila = Node(TreeBuilder.Build(World, showAll: false, "", new HashSet<int>()), "Aquila (ship)");
        var cargo = Node(aquila.Children, "Cargo Container (1)");
        Assert.Same(Cargo, cargo.Inventory);
        Assert.Empty(cargo.Children);
        var smelter = Node(aquila.Children, "Smelter");
        Assert.Null(smelter.Inventory);
        Assert.Equal(["input", "output"], smelter.Children.Select(c => c.Label));
    }

    [Fact]
    public void A_block_with_one_inventory_is_a_leaf_whatever_the_slot_is_called()
    {
        var ejector = Inv("Connector", "ejector");
        var ship = new ObjectView("Grasshopper", ObjectKind.Ship, false, true, [ejector, Inv("O2/H2 Generator", "input", rows: 0), Inv("O2/H2 Generator", "output")]);
        var node = Node(TreeBuilder.Build([ship], false, "", new HashSet<int>()), "Grasshopper (ship)");

        var connector = Node(node.Children, "Connector");
        Assert.Same(ejector, connector.Inventory);
        Assert.Empty(connector.Children);
        // the hidden empty input still makes the generator two inventories, so its slot names stay
        Assert.Equal(["output"], Node(node.Children, "O2/H2 Generator").Children.Select(c => c.Label));
    }

    [Fact]
    public void A_block_holds_all_its_inventories_for_the_grouped_view()
    {
        var emptyInput = Inv("Refinery", "input", rows: 0);
        var output = Inv("Refinery", "output");
        var ship = new ObjectView("Grasshopper", ObjectKind.Ship, false, true, [emptyInput, output]);
        var node = Node(TreeBuilder.Build([ship], false, "", new HashSet<int>()), "Grasshopper (ship)");

        Assert.Empty(node.Inventories);
        var refinery = Node(node.Children, "Refinery");
        Assert.Equal([emptyInput, output], refinery.Inventories);
        Assert.Equal([output], Assert.Single(refinery.Children).Inventories);
    }

    [Fact]
    public void Player_slots_sit_under_the_player_and_stay_when_empty()
    {
        var player = Node(TreeBuilder.Build(World, false, "", new HashSet<int>()), "Player");
        Assert.Equal(["main", "consumables"], player.Children.Select(c => c.Label));
    }

    [Fact]
    public void Unowned_objects_and_empty_inventories_hidden_unless_show_all()
    {
        var mine = TreeBuilder.Build(World, false, "", new HashSet<int>());
        Assert.DoesNotContain(mine, n => n.Label.StartsWith("Pirate Base"));
        Assert.DoesNotContain(Node(mine, "Aquila (ship)").Children, n => n.Label == "Cargo Container (2)");

        var all = TreeBuilder.Build(World, true, "", new HashSet<int>());
        Assert.Contains(all, n => n.Label == "Pirate Base (station)");
        Assert.Contains(Node(all, "Aquila (ship)").Children, n => n.Label == "Cargo Container (2)");
    }

    [Fact]
    public void Filter_keeps_ancestors()
    {
        var tree = TreeBuilder.Build(World, false, "smelter", new HashSet<int>());
        var aquila = Assert.Single(tree);
        var smelter = Assert.Single(aquila.Children);
        Assert.Equal(["input", "output"], smelter.Children.Select(c => c.Label));
        Assert.True(aquila.IsExpanded && smelter.IsExpanded);
    }

    [Fact]
    public void Filter_on_an_object_keeps_all_of_it()
    {
        var aquila = Assert.Single(TreeBuilder.Build(World, false, "aqui", new HashSet<int>()));
        Assert.Equal(["Smelter", "Cargo Container (1)"], aquila.Children.Select(c => c.Label));
    }

    [Fact]
    public void Pending_marks_bubble_up()
    {
        var aquila = Node(TreeBuilder.Build(World, false, "", new HashSet<int> { Input.Index }), "Aquila (ship)");
        var smelter = Node(aquila.Children, "Smelter");
        Assert.True(aquila.HasPending);
        Assert.True(smelter.HasPending);
        Assert.True(Node(smelter.Children, "input").HasPending);
        Assert.False(Node(smelter.Children, "output").HasPending);
    }

    [Fact]
    public void No_owner_world_shows_only_player_without_show_all()
    {
        IReadOnlyList<ObjectView> world = [Player, Aquila with { Yours = false }];
        Assert.Equal(["Player"], TreeBuilder.Build(world, false, "", new HashSet<int>()).Select(n => n.Label));
    }
}
