using System.Globalization;
using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App.Tests;

public class AmountsTests
{
    [Theory]
    [InlineData("de-DE", "1,5", 1.5)]
    [InlineData("de-DE", "1.5", 1.5)]
    [InlineData("de-DE", "1.000,5", 1000.5)]
    [InlineData("en-US", "1,000.5", 1000.5)]
    [InlineData("en-US", "1.5", 1.5)]
    [InlineData("en-US", " 20 ", 20)]
    [InlineData("de-DE", "1.000.000", 1000000)]
    public void Amounts_accept_both_decimal_marks(string culture, string text, double expected)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.True(Amounts.TryParse(text, out var value));
            Assert.Equal((decimal)expected, value);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1,2,3.4.5")]
    public void Garbage_is_refused(string text) => Assert.False(Amounts.TryParse(text, out _));
}

public class InventoryViewModelTests
{
    static readonly Guid Iron = Guid.NewGuid(), Cell = Guid.NewGuid();

    static InventoryView View(params RowView[] rows) => new(7, "Aquila/Cargo", "Cargo", "main", 10, 100, rows.Length, 5, rows);

    static string Name(Guid g) => g == Iron ? "Iron" : "Power Cell";

    [Fact]
    public void Changed_rows_are_marked()
    {
        var loaded = View(new RowView(Iron, "Iron", 10, 10, null, null));
        var preview = View(new RowView(Iron, "Iron", 20, 20, null, null));
        var vm = InventoryViewModel.Build(loaded, preview, [new SetTotal(7, Iron, 20)], [], Name);

        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsChanged);
        Assert.Equal("20", row.AmountText);
    }

    [Fact]
    public void Removed_and_added_stacks_show_as_rows()
    {
        var loaded = View(new RowView(Cell, "Power Cell", 1, 5, 0, "30%"), new RowView(Cell, "Power Cell", 1, 5, 1, "100%"));
        var edits = new Edit[] { new RemoveStack(7, 0), new AddStacks(7, Cell, 2, new ValueState(0.5m)) };
        var states = new Dictionary<Guid, StateInfo> { [Cell] = new(1, WholeNumbers: false, Percent: true) };
        var vm = InventoryViewModel.Build(loaded, loaded, edits, [], Name, states);

        Assert.True(vm.Rows[0].IsRemoved);
        Assert.False(vm.Rows[1].IsRemoved);
        var added = vm.Rows[2];
        Assert.True(added.IsChanged);
        Assert.Equal("2", added.AmountText);
        Assert.Equal("50%", added.State);
    }

    [Fact]
    public void Unchanged_rows_keep_their_list_so_typing_survives_a_preview()
    {
        var loaded = View(new RowView(Iron, "Iron", 10, 10, null, null));
        var first = InventoryViewModel.Build(loaded, loaded, [], [], Name);
        var preview = View(new RowView(Iron, "Iron", 10, 10, null, null)) with { Mass = 99 };

        var second = InventoryViewModel.Build(loaded, preview, [], [], Name, previous: first);

        Assert.Same(first.Rows, second.Rows);
        Assert.Equal("99/100kg", second.MassText);
    }

    [Fact]
    public void Status_names_the_change()
    {
        var loaded = View(new RowView(Iron, "Iron", 10, 10, null, null), new RowView(Cell, "Power Cell", 1, 5, 0, "30%"), new RowView(Cell, "Power Cell", 1, 5, 1, "90%"));
        var edits = new Edit[] { new SetTotal(7, Iron, 20), new RemoveStack(7, 0), new AddStacks(7, Cell, 1, new NewState()) };
        var vm = InventoryViewModel.Build(loaded, loaded, edits, [], Name);

        Assert.Equal(["edited", "removed", "", "new"], vm.Rows.Select(r => r.Status));
    }

    [Fact]
    public void Rows_carry_the_item_key_and_icon()
    {
        var info = new ItemInfo(Iron, "Ore/Iron", "Iron", "Item", "Ore", 1, 0, true, true, @"C:\icons\iron.png");
        var vm = InventoryViewModel.Build(View(new RowView(Iron, "Iron", 10, 10, null, null)), View(), [], [], Name, info: g => g == Iron ? info : null);

        Assert.Equal("Ore/Iron", vm.Rows[0].Key);
        Assert.Equal(@"C:\icons\iron.png", vm.Rows[0].Icon);
    }

    [Fact]
    public void Copy_text_is_tab_separated()
    {
        var vm = InventoryViewModel.Build(View(new RowView(Cell, "Power Cell", 1, 5, 0, "30%")), View(), [], [], Name);
        Assert.Equal("Power Cell\t\t1\t5kg\t30%", vm.Rows[0].CopyText);
    }

    [Fact]
    public void Every_loaded_row_can_be_removed()
    {
        var loaded = View(new RowView(Iron, "Iron", 10, 10, null, null), new RowView(Cell, "Power Cell", 1, 5, 0, "30%"));
        var edits = new Edit[] { new AddStacks(7, Cell, 1, new NewState()) };
        var vm = InventoryViewModel.Build(loaded, loaded, edits, [], Name);

        Assert.Equal([true, true, false], vm.Rows.Select(r => r.CanRemove));
        Assert.Equal(new SetTotal(7, Iron, 0), vm.Rows[0].Removal());
        Assert.Equal(new RemoveStack(7, 0), vm.Rows[1].Removal());
        Assert.All(vm.Rows, r => Assert.Equal(7, r.Inventory));
    }

    [Fact]
    public void Copied_rows_keep_the_pane_order()
    {
        var vm = InventoryViewModel.Build(View(new RowView(Iron, "Iron", 10, 10, null, null), new RowView(Cell, "Power Cell", 1, 5, 0, "30%")), View(), [], [], Name);
        Assert.Equal(vm.Rows[0].CopyText + Environment.NewLine + vm.Rows[1].CopyText, vm.CopyRows([vm.Rows[1], vm.Rows[0]]));
    }

    [Fact]
    public void Sections_lay_out_as_a_heading_then_its_rows()
    {
        var input = InventoryViewModel.Build(View(new RowView(Iron, "Iron", 10, 10, null, null)), View(), [], [], Name);
        var output = InventoryViewModel.Build(View(new RowView(Cell, "Power Cell", 1, 5, 0, "30%")) with { Index = 8 }, View(), [], [], Name);

        Assert.Equal([input, input.Rows[0], output, output.Rows[0]], PaneItems.Layout([input, output]));
    }

    [Fact]
    public void Refreshing_the_pane_keeps_rows_that_did_not_change()
    {
        var before = InventoryViewModel.Build(View(new RowView(Iron, "Iron", 10, 10, null, null)), View(), [], [], Name);
        var items = new System.Collections.ObjectModel.ObservableCollection<object>(PaneItems.Layout([before]));
        var row = items[1];
        var after = before with { MassText = "99kg", Rows = [before.Rows[0] with { }] };

        PaneItems.Sync(items, PaneItems.Layout([after]));

        Assert.Same(after, items[0]);
        Assert.Same(row, items[1]);
    }

    [Fact]
    public void Limits_turn_red_when_over()
    {
        var preview = View(new RowView(Iron, "Iron", 120, 120, null, null)) with { Mass = 120 };
        var vm = InventoryViewModel.Build(View(), preview, [new SetTotal(7, Iron, 120)], [new Override(7, "Aquila/Cargo holds 100kg max.")], Name);

        Assert.True(vm.MassOver);
        Assert.Equal("120/100kg", vm.MassText);
        Assert.Equal(["Aquila/Cargo holds 100kg max."], vm.Overrides);
    }
}

public class IconCacheTests
{
    // the game's icons are DDS files with a DX10 header holding plain RGBA pixels, despite the .png name
    static byte[] Dds(int width, int height, byte[] rgba, int dxgi = 29)
    {
        var data = new byte[148 + rgba.Length];
        "DDS "u8.CopyTo(data);
        BitConverter.GetBytes(124).CopyTo(data, 4);
        BitConverter.GetBytes(height).CopyTo(data, 12);
        BitConverter.GetBytes(width).CopyTo(data, 16);
        BitConverter.GetBytes(width * 4).CopyTo(data, 20);
        BitConverter.GetBytes(32).CopyTo(data, 76);
        BitConverter.GetBytes(4).CopyTo(data, 80);
        "DX10"u8.CopyTo(data.AsSpan(84));
        BitConverter.GetBytes(dxgi).CopyTo(data, 128);
        rgba.CopyTo(data, 148);
        return data;
    }

    [Fact]
    public void Game_icons_decode_from_dds()
    {
        var image = IconCache.Decode(Dds(2, 1, [255, 0, 0, 255, 0, 0, 255, 128]))!;

        Assert.Equal(2, image.PixelWidth);
        var bgra = new byte[8];
        image.CopyPixels(bgra, 8, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 128 }, bgra);
    }

    [GameFact]
    public void The_games_own_icons_decode()
    {
        var items = GameContent.Load(GameInstall.RootDir).Items.Items;
        foreach (var name in new[] { "Medikit", "Steel Plate" })
        {
            var image = IconCache.Load(items.First(i => i.Name == name).Icon);
            Assert.NotNull(image);
            Assert.True(image.PixelWidth >= 64, $"{name} is {image.PixelWidth}px");
        }
        Assert.All(items.Where(i => i.Icon != null), i => Assert.NotNull(IconCache.Load(i.Icon)));
    }

    [Fact]
    public void Unknown_dds_formats_are_no_icon() =>
        Assert.Null(IconCache.Decode(Dds(1, 1, [1, 2, 3, 4], dxgi: 71)));

    [Fact]
    public void A_broken_icon_is_no_icon()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "not a picture");
        try
        {
            Assert.Null(IconCache.Load(file));
            Assert.Null(IconCache.Load(null));
            Assert.Null(IconCache.Load(Path.Combine(Path.GetTempPath(), "no-such-icon.png")));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
