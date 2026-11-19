using Drydock.Core;

namespace Drydock.Tests;

public class EditPlanTests
{
    static World W => WorldTests.Loaded.Value;

    static ItemInfo Item(string name) => W.Content.Items.Search(name).Single();

    static int PlayerMain => W.Match("player").Single(i => W.Content.SlotName(W.Inventories[i].Slot) == "main");

    static decimal Total(World w, int inventory, Guid item) =>
        w.Inventories[inventory].Items.Where(s => s.Definition == item).Sum(s => (decimal)s.Amount.Value!);

    static (int Inventory, int Stack) FirstStack(Func<ItemStack, bool> where)
    {
        for (int i = 0; i < W.Inventories.Count; i++)
            for (int s = 0; s < W.Inventories[i].Items.Count; s++)
                if (where(W.Inventories[i].Items[s]))
                    return (i, s);
        throw new InvalidOperationException("no such stack in the test save");
    }

    [SaveFact]
    public void Setting_an_item_twice_keeps_the_last()
    {
        var plan = new EditPlan(W);
        var iron = Item("Iron Ore").Guid;
        plan.Apply(new SetTotal(PlayerMain, iron, 10));
        plan.Apply(new SetTotal(PlayerMain, iron, 20));

        Assert.Single(plan.Edits);
        Assert.Equal(20m, Total(plan.Preview(), PlayerMain, iron));
    }

    [SaveFact]
    public void Setting_back_to_the_loaded_total_drops_the_edit()
    {
        var plan = new EditPlan(W);
        var iron = Item("Iron Ore").Guid;
        plan.Apply(new SetTotal(PlayerMain, iron, Total(W, PlayerMain, iron) + 5));
        plan.Apply(new SetTotal(PlayerMain, iron, Total(W, PlayerMain, iron)));

        Assert.Empty(plan.Edits);
    }

    [SaveFact]
    public void Removing_a_stack_twice_is_one_edit()
    {
        var (inv, stack) = FirstStack(s => W.HasState(s.Definition));
        var plan = new EditPlan(W);
        plan.Apply(new RemoveStack(inv, stack));
        plan.Apply(new RemoveStack(inv, stack));

        Assert.Single(plan.Edits);
        Assert.Equal(W.Inventories[inv].Items.Count - 1, plan.Preview().Inventories[inv].Items.Count);
    }

    [SaveFact]
    public void Plain_and_stateful_edits_do_not_mix()
    {
        var (inv, stack) = FirstStack(s => !W.HasState(s.Definition));
        var (sInv, sStack) = FirstStack(s => W.HasState(s.Definition));
        var plan = new EditPlan(W);

        Assert.Throws<ArgumentException>(() => plan.Apply(new RemoveStack(inv, stack)));
        Assert.Throws<ArgumentException>(() => plan.Apply(new SetTotal(sInv, W.Inventories[sInv].Items[sStack].Definition, 3)));
    }

    [SaveFact]
    public void Adding_the_same_state_twice_adds_up()
    {
        var (inv, stack) = FirstStack(s => W.HasState(s.Definition));
        var item = W.Inventories[inv].Items[stack].Definition;
        var plan = new EditPlan(W);
        plan.Apply(new AddStacks(inv, item, 1, new CopyState(inv, stack)));
        plan.Apply(new AddStacks(inv, item, 2, new CopyState(inv, stack)));

        Assert.Equal(3, ((AddStacks)Assert.Single(plan.Edits)).Count);
    }

    [SaveFact]
    public void Check_passes_for_the_built_plan()
    {
        var (inv, stack) = FirstStack(s => W.HasState(s.Definition));
        var item = W.Inventories[inv].Items[stack].Definition;
        var plan = new EditPlan(W);
        plan.Apply(new SetTotal(PlayerMain, Item("Iron Ore").Guid, 20));
        plan.Apply(new RemoveStack(inv, stack));
        plan.Apply(new AddStacks(inv, item, 2, new NewState()));

        Assert.Null(plan.Check(W.WithArchive(plan.Replay().Build())));
    }

    [SaveFact]
    public void Check_catches_a_wrong_total()
    {
        var iron = Item("Iron Ore").Guid;
        var plan = new EditPlan(W);
        plan.Apply(new SetTotal(PlayerMain, iron, 20));
        var other = new EditPlan(W);
        other.Apply(new SetTotal(PlayerMain, iron, 21));

        Assert.NotNull(plan.Check(W.WithArchive(other.Replay().Build())));
    }

    [SaveFact]
    public void Check_catches_a_change_to_an_untouched_inventory()
    {
        var iron = Item("Iron Ore").Guid;
        int consumables = W.Match("player:consumables").Single();
        var plan = new EditPlan(W);
        plan.Apply(new SetTotal(PlayerMain, iron, 20));
        var other = new EditPlan(W);
        other.Apply(new SetTotal(PlayerMain, iron, 20));
        other.Apply(new SetTotal(consumables, Item("Medikit").Guid, Total(W, consumables, Item("Medikit").Guid) + 1));

        Assert.NotNull(plan.Check(W.WithArchive(other.Replay().Build())));
    }

    [SaveFact]
    public void Overrides_list_rejected_slots_and_capacity()
    {
        int consumables = W.Match("player:consumables").Single();
        var plan = new EditPlan(W);
        plan.Apply(new SetTotal(consumables, Item("Medikit").Guid, 6));
        plan.Apply(new SetTotal(consumables, Item("Iron Ore").Guid, 1));

        var messages = plan.Overrides(plan.Preview()).Select(o => o.Message).ToList();
        Assert.Contains(messages, m => m.StartsWith("player:consumables doesn't take "));
        Assert.Contains(messages, m => m is "player:consumables holds 25kg max." or "player:consumables holds 5 stacks max.");
    }

    [SaveFact]
    public void Invalid_amounts_are_refused_on_apply()
    {
        var plan = new EditPlan(W);
        Assert.Throws<ArgumentException>(() => plan.Apply(new SetTotal(PlayerMain, Item("Steel Plate").Guid, 0.5m)));
        Assert.Empty(plan.Edits);
    }
}
