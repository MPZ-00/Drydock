using System.Globalization;
using Drydock.Core;

namespace Drydock.Cli;

static class Commands
{
    public static void Saves()
    {
        var saves = Core.Saves.List();
        for (int i = 0; i < saves.Count; i++)
            Console.WriteLine($"{i + 1,3}  {saves[i].Written:yyyy-MM-dd HH:mm}  {saves[i].Name}");
        Console.WriteLine(saves.Count == 0 ? $"No saves in {Core.Saves.Root}" : "Pick a save by its number or part of its name.");
    }

    public static string ResolveSave(string save) => Core.Saves.Resolve(save);

    static World Load(string save)
    {
        var path = ResolveSave(save);
        var file = new FileInfo(path);
        file.Refresh();
        var archive = Status.Step("Reading save", () => SaveArchive.Load(path));
        var content = Status.Step("Reading game content", () => GameContent.Load(GameInstall.RootDir));
        return World.From(path, archive, content, file);
    }

    public static void Items(string? query)
    {
        var catalog = Status.Step("Reading game content", () => GameContent.Load(GameInstall.RootDir)).Items;
        var items = query == null ? catalog.Items.ToList() : catalog.Search(query).ToList();
        foreach (var i in items.OrderBy(i => i.Kind).ThenBy(i => i.Type).ThenBy(i => i.Name))
            Console.WriteLine($"{i.Guid}  {i.Kind,-10} {i.Type,-10} {i.Name}");
        Console.WriteLine($"{items.Count} items");
    }

    static string Kind(World w, WorldObject o) => o switch
    {
        { IsPlayer: true } => "Player",
        { Kind: ObjectKind.Character } => "Character",
        _ => o.Kind + (w.IsYours(o) ? "" : " (not yours)"),
    };

    public static void Inventories(string save, string? target, bool all)
    {
        var w = Load(save);
        if (target != null)
        {
            var matches = w.Match(target);
            if (matches.Count == 0)
                Console.Error.WriteLine($"No inventory matches '{target}'.");
            foreach (int n in matches)
                PrintInventory(w, n, w.Inventories[n]);
            return;
        }

        var numbered = w.Inventories.Select((inv, n) => (inv, n));
        var groups = numbered.GroupBy(x => x.inv.Object, ReferenceEqualityComparer.Instance)
            .Select(g => (Object: (WorldObject)g.Key!, Items: g.ToList()))
            .Where(g => all || w.IsYours(g.Object))
            .OrderByDescending(g => g.Object.IsPlayer)
            .ThenBy(g => g.Object.Kind)
            .ThenBy(g => w.ObjectName(g.Object), StringComparer.OrdinalIgnoreCase);
        int hidden = 0;
        foreach (var g in groups)
        {
            var shown = g.Items.Where(x => all || x.inv.Items.Count > 0 || g.Object.IsPlayer).ToList();
            hidden += g.Items.Count - shown.Count;
            if (shown.Count == 0)
                continue;
            Console.WriteLine($"{Kind(w, g.Object)}: {w.ObjectName(g.Object)}");
            foreach (var (inv, n) in shown)
            {
                int count = inv.Items.Count;
                Console.WriteLine($"  #{n,-5} {w.Ref(inv),-60} {(count == 0 ? "empty" : count == 1 ? "1 stack" : $"{count} stacks")}");
            }
        }
        if (!all)
            Console.WriteLine($"Showing your character and grids{(hidden > 0 ? $", {hidden} empty inventories hidden" : "")}. --all shows everything.");
        Console.WriteLine("Show one with: inventories <save> <name or #number>");
    }

    static void PrintInventory(World w, int number, Inventory inv)
    {
        var rules = w.Rules(inv);
        var mass = $"{w.Mass(inv):0.##}{(rules?.MaxMass is { } max ? $"/{max:0.##}" : "")}kg";
        var stacks = $"{inv.Items.Count}{(rules?.MaxStacks is { } most ? $"/{most}" : "")} stacks";
        Console.WriteLine($"#{number}  {w.Ref(inv)}  ({mass}, {stacks})");
        foreach (var stack in inv.Items)
        {
            var name = w.Content.Items.Find(stack.Definition)?.Name ?? stack.Definition.ToString();
            var amount = Convert.ToString(stack.Amount.Value, CultureInfo.InvariantCulture);
            var meta = stack.HasMetaData ? $"  {stack.MetaData!.GetShortValueString()}" : "";
            Console.WriteLine($"    {amount,10}  {name}{meta}");
        }
        if (inv.Items.Count == 0)
            Console.WriteLine("    (empty)");
    }

    public enum EditKind { Set, Add, Remove }

    /// <summary>One inventory edit from the command line. Amount 0 with set removes the item.</summary>
    public static int Edit(EditKind kind, string save, string target, string itemQuery, string? amountText, bool dryRun, bool force)
    {
        decimal amount = 0;
        if (kind != EditKind.Remove && !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return Fail($"'{amountText}' is not an amount. Use a dot for decimals.");
        if (amount < 0 || (kind == EditKind.Add && amount == 0))
            return Fail("Amount must be positive.");

        var w = Load(save);
        var items = w.Content.Items.Search(itemQuery);
        if (items.Count != 1)
        {
            Console.Error.WriteLine(items.Count == 0 ? $"No item matches '{itemQuery}'." : $"'{itemQuery}' matches {items.Count} items, be more specific:");
            foreach (var m in items.Take(15))
                Console.Error.WriteLine($"    {m.Name}  ({m.Key})");
            return 1;
        }
        var item = items[0];

        var matches = w.Route(w.Match(target), item, slotNamed: target.Contains(':'));
        if (matches.Count != 1)
        {
            Console.Error.WriteLine(matches.Count == 0
                ? $"No inventory matches '{target}'. Run 'inventories' to see them."
                : $"'{target}' matches {matches.Count} inventories, use one of these:");
            foreach (int m in matches.Take(15))
                Console.Error.WriteLine($"    #{m,-5} {w.Ref(w.Inventories[m])}");
            return 1;
        }
        int n = matches[0];
        var inventory = w.Inventories[n];
        if (kind != EditKind.Remove && amount > 0 && w.Accepts(n, item) == false && !force)
            return Fail($"{w.Rejection(n, item)} Use --force to override it.");

        var editor = new SaveEditor(w.Archive, w.Content.Items);
        try
        {
            switch (kind)
            {
                case EditKind.Add:
                    editor.AddItem(inventory, item.Guid, amount);
                    break;
                case EditKind.Set:
                    editor.SetTotal(inventory, item.Guid, amount);
                    break;
                case EditKind.Remove when inventory.Items.All(s => s.Definition != item.Guid):
                    return Fail($"{w.Ref(inventory)} holds no {item.Name}.");
                case EditKind.Remove:
                    editor.SetTotal(inventory, item.Guid, 0);
                    break;
            }
        }
        catch (ArgumentException e)
        {
            return Fail($"{item.Name}: {e.Message}");
        }
        if (!editor.HasChanges)
        {
            Console.WriteLine($"Nothing to change, {w.Ref(inventory)} already holds {amount} {item.Name}.");
            return 0;
        }

        var edited = Status.Step("Building the edited save", editor.Build);
        Console.WriteLine("After the edit:");
        var after = w.WithArchive(edited);
        PrintInventory(after, n, after.Inventories[n]);
        if (w.OverCapacity(inventory, after.Inventories[n]) is { } problem)
        {
            if (!force)
                return Fail($"{problem} Use --force to override it.");
            Console.WriteLine($"{problem} Overridden by --force.");
        }
        if (dryRun)
        {
            Console.WriteLine("Dry run, nothing written.");
            return 0;
        }
        try
        {
            var backup = Status.Step("Writing save", () => SaveWriter.Write(edited, w.Path));
            Console.WriteLine($"Written. Backup: {backup}");
            return 0;
        }
        catch (InvalidOperationException e)
        {
            return Fail(e.Message);
        }
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    public static void Paths(string file, string fieldName)
    {
        var archive = SaveArchive.Load(file);
        var seen = new Dictionary<string, int>();
        foreach (var (field, path) in archive.Root.Walk())
        {
            if (field.Name != fieldName)
                continue;
            var key = string.Join(" > ", path.Select(p => $"{Segment(p.Name)}:{Short(p.TypeName())}"))
                + $" > {field.Name}:{Short(field.TypeName())}";
            seen[key] = seen.GetValueOrDefault(key) + 1;
        }
        foreach (var (key, count) in seen.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"{count,5}  {key}");
    }

    public static void Bytes(string save, string ownerFilter)
    {
        var archive = SaveArchive.Load(ResolveSave(save));
        var buf = archive.MainChunk;
        string Hex(int start, int end) => Convert.ToHexString(buf, start, Math.Min(end - start, 64));
        foreach (var inv in Inventory.FindAll(archive).Where(i => i.Owner.Contains(ownerFilter, StringComparison.OrdinalIgnoreCase)))
        {
            var items = inv.ItemsField;
            Console.WriteLine($"{inv.Owner} slot {inv.Slot}: inventory {inv.Field.Start}..{inv.Field.End}, Items {items.Start}..{items.End} ({items.TypeName()}) objId={items.ObjectId}");
            var firstStart = items.Members.Length > 0 ? items.Members[0].Start : items.End;
            Console.WriteLine($"  header {Hex(items.Start, firstStart)}");
            foreach (var stack in items.Members)
            {
                Console.WriteLine($"  {stack.Name} {stack.Start}..{stack.End} {stack.TypeName()}  {Hex(stack.Start, stack.End)}");
                foreach (var m in stack.Members)
                    Console.WriteLine($"      {m.Name,-10} {m.Start}..{m.End} {m.TypeName()} value={m.Value?.GetType().Name}:{m.Value} flags={m.Flags} objId={m.ObjectId}  {Hex(m.Start, m.End)}");
            }
        }
    }

    public static void Show(string file, string name, int limit)
    {
        var archive = SaveArchive.Load(file);
        int shown = 0;
        foreach (var (field, _) in archive.Root.Walk())
        {
            if (!field.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                continue;
            Console.WriteLine(field.PrintOut(4000));
            if (++shown == limit)
                break;
        }
    }

    // list indices and dictionary keys would make every path unique
    static string Segment(string name) => name.StartsWith('[') || name.StartsWith('{') ? "*" : name;

    static string Short(string typeName)
    {
        int i = typeName.LastIndexOf('.');
        return i < 0 ? typeName : typeName[(i + 1)..];
    }
}
