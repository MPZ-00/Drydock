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
    public void Limits_turn_red_when_over()
    {
        var preview = View(new RowView(Iron, "Iron", 120, 120, null, null)) with { Mass = 120 };
        var vm = InventoryViewModel.Build(View(), preview, [new SetTotal(7, Iron, 120)], [new Override(7, "Aquila/Cargo holds 100kg max.")], Name);

        Assert.True(vm.MassOver);
        Assert.Equal("120/100kg", vm.MassText);
        Assert.Equal(["Aquila/Cargo holds 100kg max."], vm.Overrides);
    }
}
