using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Drydock.Core;

/// <summary>What an inventory accepts, from the game's inventory definition. Both sets empty means anything.</summary>
public sealed record InventoryRules(IReadOnlySet<string> Types, IReadOnlySet<Guid> Items)
{
    public bool Accepts(ItemInfo item) =>
        (Types.Count == 0 && Items.Count == 0) || Types.Contains(item.Type) || Items.Contains(item.Guid);
}

/// <summary>What Drydock reads from the game's content folder: item definitions, localized texts and inventory slots.</summary>
public sealed partial class GameContent
{
    readonly Dictionary<string, string> _texts;
    readonly Dictionary<Guid, string> _slotKeys;
    readonly Dictionary<Guid, Dictionary<Guid, Guid>> _compositeSlots;
    readonly Dictionary<Guid, InventoryRules> _inventoryRules;
    readonly DefinitionIndex _definitions;

    public ItemCatalog Items { get; }

    GameContent(ItemCatalog items, Dictionary<string, string> texts, Dictionary<Guid, string> slotKeys,
        Dictionary<Guid, Dictionary<Guid, Guid>> compositeSlots, Dictionary<Guid, InventoryRules> inventoryRules, DefinitionIndex definitions)
    {
        Items = items;
        _texts = texts;
        _slotKeys = slotKeys;
        _compositeSlots = compositeSlots;
        _inventoryRules = inventoryRules;
        _definitions = definitions;
    }

    /// <param name="gameRoot">The SpaceEngineers2 folder holding GameData.</param>
    public static GameContent Load(string gameRoot, string language = "en-US")
    {
        var content = Path.Combine(gameRoot, "GameData", "Vanilla", "Content");
        var texts = LoadTexts(Path.Combine(content, "MainMenuData", "Texts", language));
        var files = new Dictionary<Guid, string>();
        var slotKeys = new Dictionary<Guid, List<string>>();
        var compositeSlots = new Dictionary<Guid, Dictionary<Guid, Guid>>();
        var inventoryRules = new Dictionary<Guid, InventoryRules>();
        foreach (var file in Directory.EnumerateFiles(content, "*.def", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (ValueGuid().Match(text) is not { Success: true } match)
                continue;
            var guid = Guid.Parse(match.Groups[1].Value);
            files.TryAdd(guid, file);
            // composite definitions name their components, which is the only place inventory slots get a name
            if (text.Contains("\"$Key\"", StringComparison.Ordinal))
                foreach (Match m in SlotKey().Matches(text))
                {
                    var slot = Guid.Parse(m.Groups[2].Value);
                    if (!slotKeys.TryGetValue(slot, out var keys))
                        slotKeys[slot] = keys = [];
                    if (!keys.Contains(m.Groups[1].Value))
                        keys.Add(m.Groups[1].Value);
                }
            if (text.Contains("EntityCompositeDefinitionObjectBuilder\"", StringComparison.Ordinal))
                compositeSlots[guid] = ComponentDefinitions(JsonNode.Parse(text)?["$Value"]?["Components"]);
            else if (text.Contains("InventoryDefinitionObjectBuilder\"", StringComparison.Ordinal))
                inventoryRules[guid] = Rules(JsonNode.Parse(text)?["$Value"]);
        }
        var definitions = DefinitionIndex.Load(Path.Combine(content, "definitionsets.vrb"));
        return new GameContent(ItemCatalog.Build(definitions, files, texts), texts,
            slotKeys.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(KeyRank).First()),
            compositeSlots, inventoryRules, definitions);
    }

    // one component can be named twice, the smelter's output is also its "InventoryStockpile"; the specific name wins
    static int KeyRank(string key) => key switch { "InventoryStockpile" => 2, "Inventory" => 1, _ => 0 };

    /// <summary>Component key to definition, from a composite's list or its delta form (Keys plus Changed by index).</summary>
    static Dictionary<Guid, Guid> ComponentDefinitions(JsonNode? components)
    {
        var result = new Dictionary<Guid, Guid>();
        void Put(JsonNode? key, JsonNode? value)
        {
            if (key is JsonValue k && Guid.TryParse(k.ToString(), out var slot)
                && value?["Definition"] is JsonValue d && Guid.TryParse(d.ToString(), out var def))
                result[slot] = def;
        }
        if (components is JsonArray list)
            foreach (var entry in list)
                Put(entry?["Key"], entry?["Value"]);
        else if (components?["Keys"] is JsonArray keys && components["Changed"] is JsonArray changed)
            foreach (var change in changed)
                if (change?["Index"] is JsonValue i && i.TryGetValue<int>(out int index) && index >= 0 && index < keys.Count)
                    Put(keys[index], change["Value"]);
        return result;
    }

    static InventoryRules Rules(JsonNode? value)
    {
        var types = (value?["WhitelistedTypes"] as JsonValue)?.ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var items = value?["WhitelistedItems"] switch
        {
            JsonArray a => a,
            JsonObject delta => delta["Added"] as JsonArray,
            _ => null,
        };
        var guids = items?.Select(n => Guid.TryParse(n?.ToString(), out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty) ?? [];
        return new InventoryRules(types.ToHashSet(), guids.ToHashSet());
    }

    /// <summary>What a slot of an entity accepts, following the composite's base chain. Null when the game data doesn't say.</summary>
    public InventoryRules? Rules(Guid? composite, Guid slot)
    {
        for (int depth = 0; composite is { } c && c != Guid.Empty && depth < 16; depth++)
        {
            if (_compositeSlots.TryGetValue(c, out var slots) && slots.TryGetValue(slot, out var def))
                return _inventoryRules.GetValueOrDefault(def);
            composite = _definitions.Find(c)?.BaseGuid;
        }
        return null;
    }

    public string Text(string key) => _texts.GetValueOrDefault(key, key);

    /// <summary>Short slot name for an inventory component key: main, consumables, datapads, input, output, cell0.</summary>
    public string SlotName(Guid slot)
    {
        if (!_slotKeys.TryGetValue(slot, out var key))
            return slot.ToString()[..8];
        return key switch
        {
            "Inventory" => "main",
            "ConsumableInventory" => "consumables",
            "DatapadInventory" => "datapads",
            "QuestItemsInventory" => "quest",
            "BlockSchematicsInventory" => "schematics",
            _ => key.Replace("Inventory", "").Replace("Slot", "").Replace(" ", "").ToLowerInvariant() is { Length: > 0 } s ? s : key,
        };
    }

    /// <summary>Render a name as the game would, "{Loc:Locker} {BLOCK_SIZE:...}" becomes "Locker".</summary>
    public string Localize(string raw) =>
        Placeholder().Replace(raw, m => m.Groups[1].Value == "Loc" ? Text(m.Groups[2].Value) : "").Trim();

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

    [GeneratedRegex("\"\\$Value\":\\s*\\{\\s*\"Guid\":\\s*\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex ValueGuid();

    [GeneratedRegex("\"\\$Key\":\\s*\"([^\"]+)\",\\s*\"\\$Value\":\\s*\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex SlotKey();

    [GeneratedRegex("\\{(\\w+):([^}]*)\\}")]
    private static partial Regex Placeholder();
}
