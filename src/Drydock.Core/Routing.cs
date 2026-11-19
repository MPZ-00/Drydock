namespace Drydock.Core;

public sealed partial class World
{
    /// <summary>
    /// Inventories a target names: #number, player[:slot], or object[/block][:slot] where each part is an exact name
    /// or, failing that, part of one.
    /// </summary>
    public List<int> Match(string target)
    {
        var all = Enumerable.Range(0, Inventories.Count).ToList();
        if (int.TryParse(target.TrimStart('#'), out int number))
            return number >= 0 && number < Inventories.Count ? [number] : [];

        string? slot = null;
        int colon = target.LastIndexOf(':');
        if (colon > 0 && all.Any(n => Content.SlotName(Inventories[n].Slot).Equals(target[(colon + 1)..], StringComparison.OrdinalIgnoreCase)))
            (target, slot) = (target[..colon], target[(colon + 1)..]);
        int slash = target.IndexOf('/');
        var objectPart = slash < 0 ? target : target[..slash];
        var blockPart = slash < 0 ? null : target[(slash + 1)..];

        var hits = Pick(all, n => ObjectName(Inventories[n].Object), objectPart);
        if (blockPart != null)
            hits = Pick(hits, n => BlockLabel(Inventories[n]), blockPart);
        if (slot != null)
            hits = hits.Where(n => Content.SlotName(Inventories[n].Slot).Equals(slot, StringComparison.OrdinalIgnoreCase)).ToList();
        return hits;
    }

    static List<int> Pick(List<int> candidates, Func<int, string> name, string query)
    {
        var exact = candidates.Where(n => name(n).Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0 ? exact : candidates.Where(n => name(n).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public bool? Accepts(int index, ItemInfo item) => Rules(Inventories[index])?.Accepts(item);

    /// <summary>Narrows matches to the slot the game would put the item in, when no slot was named.</summary>
    public List<int> Route(List<int> matches, ItemInfo item, bool slotNamed)
    {
        if (matches.Count < 2 || slotNamed)
            return matches;
        var accepting = matches.Where(i => Accepts(i, item) == true).ToList();
        if (accepting.Count > 0)
            matches = accepting;
        var slot = item.Kind switch
        {
            "Consumable" => "consumables",
            "Datapad" => "datapads",
            "Quest" => "quest",
            "Schematic" => "schematics",
            _ => "main",
        };
        var preferred = matches.Where(i => Content.SlotName(Inventories[i].Slot) == slot).ToList();
        return preferred.Count > 0 && matches.Count > 1 ? preferred : matches;
    }

    /// <summary>Another slot of the same block that accepts the item.</summary>
    public int? SlotThatTakes(int index, ItemInfo item)
    {
        var inv = Inventories[index];
        int other = Enumerable.Range(0, Inventories.Count).FirstOrDefault(i => i != index && Accepts(i, item) == true
            && ReferenceEquals(Inventories[i].Object, inv.Object) && Inventories[i].Position == inv.Position
            && Inventories[i].Block == inv.Block, -1);
        return other >= 0 ? other : null;
    }

    public string Rejection(int index, ItemInfo item) =>
        $"{Ref(Inventories[index])} doesn't take {item.Name}."
        + (SlotThatTakes(index, item) is { } other ? $" It goes in \"{Ref(Inventories[other])}\"." : "");

    /// <summary>Why the edit overfills the inventory, or null. An inventory already over its limit may still shrink.</summary>
    public string? OverCapacity(Inventory before, Inventory after)
    {
        if (Rules(before) is not { } rules)
            return null;
        double massBefore = Mass(before), massAfter = Mass(after);
        // the two inventories come from different worlds and only one of them has its block label here
        var name = after.Block == null || _blockLabels.ContainsKey(after) ? Ref(after) : Ref(before);
        if (rules.MaxMass is { } maxMass && massAfter > maxMass && massAfter > massBefore)
            return $"{name} holds {maxMass:0.##}kg max.";
        if (rules.MaxStacks is { } maxStacks && after.Items.Count > maxStacks && after.Items.Count > before.Items.Count)
            return $"{name} holds {maxStacks} stacks max.";
        return null;
    }
}
