using System.Globalization;
using Drydock.Core;

namespace Drydock.Cli;

static class Commands
{
    static readonly string SaveRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceEngineers2", "AppData", "SaveGames");

    /// <summary>A save folder path, or the name of a folder in the game's SaveGames.</summary>
    public static string ResolveSave(string save)
    {
        var dir = Directory.Exists(save) ? save : Path.Combine(SaveRoot, save);
        var file = Path.Combine(dir, "savegame.vrb");
        if (!File.Exists(file))
            throw new FileNotFoundException($"No savegame.vrb in {dir}");
        return file;
    }

    public static void Items(string? query)
    {
        var catalog = ItemCatalog.Load(GameInstall.RootDir);
        var items = query == null ? catalog.Items.ToList() : catalog.Search(query).ToList();
        foreach (var i in items.OrderBy(i => i.Kind).ThenBy(i => i.Type).ThenBy(i => i.Name))
            Console.WriteLine($"{i.Guid}  {i.Kind,-10} {i.Type,-10} {i.Name}");
        Console.WriteLine($"{items.Count} items");
    }

    public static void Inventories(string save, string? ownerFilter, bool showEmpty)
    {
        var archive = SaveArchive.Load(ResolveSave(save));
        var catalog = ItemCatalog.Load(GameInstall.RootDir);
        var all = Inventory.FindAll(archive);
        for (int n = 0; n < all.Count; n++)
        {
            var inv = all[n];
            if (ownerFilter != null && !inv.Owner.Contains(ownerFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (inv.Items.Count == 0 && !showEmpty)
                continue;
            PrintInventory(n, inv, catalog);
        }
        Console.WriteLine("Use the #number to pick an inventory in set, add and remove.");
    }

    static void PrintInventory(int number, Inventory inv, ItemCatalog catalog)
    {
        Console.WriteLine($"#{number}  {inv.Owner}  (slot {inv.Slot.ToString()[..8]})");
        foreach (var stack in inv.Items)
        {
            var name = catalog.Find(stack.Definition)?.Name ?? stack.Definition.ToString();
            var amount = Convert.ToString(stack.Amount.Value, CultureInfo.InvariantCulture);
            var meta = stack.HasMetaData ? $"  {stack.MetaData!.GetShortValueString()}" : "";
            Console.WriteLine($"    {amount,10}  {name}{meta}");
        }
    }

    public enum EditKind { Set, Add, Remove }

    /// <summary>One inventory edit from the command line. Amount 0 with set removes the stack.</summary>
    public static int Edit(EditKind kind, string save, string inventoryNumber, string itemQuery, string? amountText, bool dryRun)
    {
        var path = ResolveSave(save);
        var archive = SaveArchive.Load(path);
        var catalog = ItemCatalog.Load(GameInstall.RootDir);
        var all = Inventory.FindAll(archive);
        if (!int.TryParse(inventoryNumber.TrimStart('#'), out int n) || n < 0 || n >= all.Count)
            return Fail($"No inventory #{inventoryNumber}. Run 'inventories' to see the numbers.");
        var inventory = all[n];

        var matches = catalog.Search(itemQuery);
        if (matches.Count != 1)
        {
            Console.Error.WriteLine(matches.Count == 0 ? $"No item matches '{itemQuery}'." : $"'{itemQuery}' matches {matches.Count} items, be more specific:");
            foreach (var m in matches.Take(15))
                Console.Error.WriteLine($"    {m.Name}  ({m.Key})");
            return 1;
        }
        var item = matches[0];

        decimal amount = 0;
        if (kind != EditKind.Remove && !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return Fail($"'{amountText}' is not an amount. Use a dot for decimals.");
        if (amount < 0 || (kind == EditKind.Add && amount == 0))
            return Fail("Amount must be positive.");

        var editor = new SaveEditor(archive);
        var stacks = inventory.Items.Where(s => s.Definition == item.Guid).ToList();
        switch (kind)
        {
            case EditKind.Add:
                editor.AddItem(inventory, item.Guid, amount);
                break;
            case EditKind.Set when stacks.Count == 0:
                if (amount > 0)
                    editor.AddItem(inventory, item.Guid, amount);
                break;
            case EditKind.Set when stacks.Count > 1:
                return Fail($"#{n} has {stacks.Count} stacks of {item.Name}; set needs exactly one. Use remove and add instead.");
            case EditKind.Set when amount == 0:
                editor.Remove(inventory, stacks[0]);
                break;
            case EditKind.Set:
                editor.SetAmount(stacks[0], amount);
                break;
            case EditKind.Remove when stacks.Count == 0:
                return Fail($"#{n} holds no {item.Name}.");
            case EditKind.Remove:
                foreach (var stack in stacks)
                    editor.Remove(inventory, stack);
                break;
        }
        if (!editor.HasChanges)
        {
            Console.WriteLine("Nothing to change.");
            return 0;
        }

        var edited = editor.Build();
        var after = Inventory.FindAll(edited)[n];
        Console.WriteLine("After the edit:");
        PrintInventory(n, after, catalog);
        if (dryRun)
        {
            Console.WriteLine("dry run, nothing written");
            return 0;
        }
        try
        {
            var backup = SaveWriter.Write(edited, path);
            Console.WriteLine($"written. backup: {backup}");
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
