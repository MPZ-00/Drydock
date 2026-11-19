using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Drydock.Core;

public sealed record ItemInfo(Guid Guid, string Key, string Name, string Kind, string Type, double MassPerUnit, double VolumePerUnit);

/// <summary>
/// Item definitions from the game's content. definitionsets.vrb says which definitions exist, their runtime type and
/// what they inherit from; the JSON .def files hold the values, merged over their base.
/// </summary>
public sealed partial class ItemCatalog
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

    /// <param name="gameRoot">The SpaceEngineers2 folder holding GameData.</param>
    public static ItemCatalog Load(string gameRoot, string language = "en-US")
    {
        var content = Path.Combine(gameRoot, "GameData", "Vanilla", "Content");
        var definitions = DefinitionIndex.Load(Path.Combine(content, "definitionsets.vrb"));
        var files = IndexDefFiles(content);
        var texts = LoadTexts(Path.Combine(content, "MainMenuData", "Texts", language));
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
            items[def.Guid] = new ItemInfo(
                def.Guid,
                key,
                texts.GetValueOrDefault(key, key),
                kind,
                value["Type"]?.GetValue<string>() ?? kind,
                Number(value, "MassPerUnit"),
                Number(value, "VolumePerUnit"));
        }
        return new ItemCatalog(items);
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

    static Dictionary<Guid, string> IndexDefFiles(string content)
    {
        var files = new Dictionary<Guid, string>();
        foreach (var file in Directory.EnumerateFiles(content, "*.def", SearchOption.AllDirectories))
        {
            var match = ValueGuid().Match(File.ReadAllText(file));
            if (match.Success)
                files.TryAdd(Guid.Parse(match.Groups[1].Value), file);
        }
        return files;
    }

    [GeneratedRegex("\"\\$Value\":\\s*\\{\\s*\"Guid\":\\s*\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex ValueGuid();

    static Dictionary<string, string> LoadTexts(string dir)
    {
        var texts = new Dictionary<string, string>();
        if (!Directory.Exists(dir))
            return texts;
        foreach (var file in Directory.EnumerateFiles(dir, "*.loc-texts"))
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            foreach (var (k, v) in entries ?? [])
                texts.TryAdd(k, v);
        }
        return texts;
    }

    static double Number(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}
