using System.Text.Json.Nodes;

namespace Drydock.Core;

/// <param name="Stackable">False for tools, guns and power modules, which the game keeps one per stack.</param>
/// <param name="Divisible">True for ore and ingots, which come in fractions.</param>
/// <param name="Icon">A picture of the item in the game install, when one could be found.</param>
public sealed record ItemInfo(Guid Guid, string Key, string Name, string Kind, string Type, double MassPerUnit, double VolumePerUnit, bool Stackable, bool Divisible, string? Icon = null);

/// <summary>
/// Item definitions from the game's content. definitionsets.vrb says which definitions exist, their runtime type and
/// what they inherit from; the JSON .def files hold the values, merged over their base.
/// </summary>
public sealed class ItemCatalog
{
    // runtime definition types the inventory accepts; anything else in a slot shows up by GUID
    static readonly Dictionary<string, string> ItemKinds = new()
    {
        ["Keen.Game2.Simulation.WorldObjects.Items.ItemDefinition"] = "Item",
        ["Keen.Game2.Simulation.WorldObjects.Items.ResourceContainerItemDefinition"] = "Item",
        ["Keen.Game2.Simulation.WorldObjects.Characters.Consumables.ConsumableDefinition"] = "Consumable",
        ["Keen.Game2.Simulation.WorldObjects.Datapads.DatapadDefinition"] = "Datapad",
        ["Keen.Game2.Simulation.WorldObjects.QuestItems.QuestItemDefinition"] = "Quest",
        ["Keen.Game2.Simulation.WorldObjects.BlockSchematics.BlockSchematicItemDefinition"] = "Schematic",
    };

    readonly Dictionary<Guid, ItemInfo> _byGuid;

    public IReadOnlyCollection<ItemInfo> Items => _byGuid.Values;

    ItemCatalog(Dictionary<Guid, ItemInfo> byGuid) => _byGuid = byGuid;

    internal static ItemCatalog From(IEnumerable<ItemInfo> items) => new(items.ToDictionary(i => i.Guid));

    public ItemInfo? Find(Guid guid) => _byGuid.GetValueOrDefault(guid);

    /// <summary>Find by exact GUID, then by localization key, display name or "name type" like "Iron Ore" (case insensitive).</summary>
    public IReadOnlyList<ItemInfo> Search(string query)
    {
        if (Guid.TryParse(query, out var guid))
            return Find(guid) is { } hit ? [hit] : [];
        var exact = Items.Where(i => Names(i).Any(n => n.Equals(query, StringComparison.OrdinalIgnoreCase))).ToList();
        if (exact.Count > 0)
            return exact;
        return Items.Where(i => Names(i).Any(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    static IEnumerable<string> Names(ItemInfo i) => [i.Key, i.Name, $"{i.Name} {i.Type}"];

    /// <param name="files">The .def file of each definition GUID.</param>
    /// <param name="texts">Localized texts by key.</param>
    /// <param name="contentRoot">The game's Content folder, for the terminal's icons.</param>
    internal static ItemCatalog Build(DefinitionIndex definitions, Dictionary<Guid, string> files, Dictionary<string, string> texts, string? contentRoot = null)
    {
        var resolved = new Dictionary<Guid, JsonObject?>();

        var items = new Dictionary<Guid, ItemInfo>();
        foreach (var def in definitions.All)
        {
            var typeName = def.Type[(def.Type.IndexOf(':') + 1)..];
            if (!ItemKinds.TryGetValue(typeName, out var kind))
                continue;
            var value = Resolve(def.Guid, definitions, files, resolved);
            // abstract bases like DatapadBase have no name of their own
            if (value?["DisplayName"]?.GetValue<string>() is not { Length: > 0 } key)
                continue;
            int flags = StackFlags(value["Flags"]);
            items[def.Guid] = new ItemInfo(
                def.Guid,
                key,
                texts.GetValueOrDefault(key, key),
                kind,
                value["Type"]?.GetValue<string>() ?? kind,
                Number(value, "MassPerUnit"),
                Number(value, "VolumePerUnit"),
                (flags & Stackable) != 0,
                (flags & Divisible) != 0,
                files.TryGetValue(def.Guid, out var file) ? FindIcon(file, contentRoot) : null);
        }
        return new ItemCatalog(items);
    }

    /// <summary>
    /// The released game has no map from an item's icon GUID to its file (that lives in the editor's meta files),
    /// so this looks for a PNG named like an icon in or next to the item's definition folder.
    /// </summary>
    internal static string? FindIcon(string defFile, string? contentRoot = null)
    {
        return OwnIcon(defFile) ?? (contentRoot == null ? null : TerminalIcon(defFile, contentRoot));
    }

    // the terminal keeps icons in a copy of the content layout: Components/Simple/SteelPlate -> Terminal/Components/Simple/SteelPlate.png
    static string? TerminalIcon(string defFile, string contentRoot)
    {
        var itemDir = Path.GetRelativePath(contentRoot, Path.GetDirectoryName(defFile)!);
        if (itemDir.StartsWith("..", StringComparison.Ordinal) || Path.GetDirectoryName(itemDir) is not { Length: > 0 } group)
            return null;
        var dir = Path.Combine(contentRoot, "MainMenuData", "UI", "Terminal", group);
        if (!Directory.Exists(dir))
            return null;
        var name = Plain(Path.GetFileName(itemDir));
        return Directory.EnumerateFiles(dir, "*.png").FirstOrDefault(f => Plain(Path.GetFileNameWithoutExtension(f)) == name);
    }

    static string Plain(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant().Replace("icon", "");

    static string? OwnIcon(string defFile)
    {
        var dir = Path.GetDirectoryName(defFile)!;
        var subfolders = new[] { "Icon", "Icons" }.Select(f => Path.Combine(dir, f)).Where(Directory.Exists).ToList();
        var inSubfolders = subfolders.SelectMany(f => Directory.EnumerateFiles(f, "*.png")).ToList();
        var named = inSubfolders.Concat(Directory.EnumerateFiles(dir, "*.png"))
            .Where(f => Path.GetFileName(f).Contains("icon", StringComparison.OrdinalIgnoreCase)).ToList();
        var item = Path.GetFileNameWithoutExtension(defFile);
        return named.FirstOrDefault(f => item.Contains(Path.GetFileNameWithoutExtension(f).Replace("_Icon", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
            // several icons and none named for this item: guessing would give every datapad the encrypted one
            ?? (named.Count == 1 ? named[0] : null)
            ?? (inSubfolders.Count == 1 ? inSubfolders[0] : null);
    }

    // the game's ItemStackFlags
    const int Stackable = 1, Divisible = 2, PresetNormal = 3;

    /// <summary>"Stackable, Divisible", "PresetNormal" or a number. A definition without the field is treated as normal.</summary>
    static int StackFlags(JsonNode? node)
    {
        if (node is not JsonValue value)
            return PresetNormal;
        if (value.TryGetValue<int>(out int number))
            return number;
        int flags = 0;
        foreach (var name in value.ToString().Split(',', StringSplitOptions.TrimEntries))
            flags |= name switch { "Stackable" => Stackable, "Divisible" => Divisible, "PresetNormal" => PresetNormal, _ => 0 };
        return flags;
    }

    /// <summary>A definition's $Value with its base chain merged underneath, top-level fields only.</summary>
    static JsonObject? Resolve(Guid guid, DefinitionIndex definitions, Dictionary<Guid, string> files, Dictionary<Guid, JsonObject?> cache)
    {
        if (cache.TryGetValue(guid, out var hit))
            return hit;
        cache[guid] = null; // guards against inheritance cycles
        JsonObject? own = null;
        if (files.TryGetValue(guid, out var file) && JsonNode.Parse(File.ReadAllText(file))?["$Value"] is JsonObject v)
            own = v;
        JsonObject? merged = null;
        if (definitions.Find(guid) is { BaseGuid: var b } && b != Guid.Empty && Resolve(b, definitions, files, cache) is { } baseValue)
            merged = (JsonObject)baseValue.DeepClone();
        if (own != null)
        {
            merged ??= new JsonObject();
            foreach (var (name, node) in own)
                merged[name] = node?.DeepClone();
        }
        return cache[guid] = merged;
    }

    static double Number(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}
