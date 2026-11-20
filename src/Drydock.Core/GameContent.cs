using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Drydock.Core;

/// <summary>An inventory's limits from the game's inventory definition. Empty whitelists mean anything goes.</summary>
public sealed record InventoryRules(string[] Types, Guid[] Items, double? MaxMass, int? MaxStacks)
{
    public bool Accepts(ItemInfo item) =>
        (Types.Length == 0 && Items.Length == 0) || Types.Contains(item.Type) || Items.Contains(item.Guid);
}

/// <summary>What Drydock reads from the game's content folder: item definitions, localized texts and inventory slots.</summary>
public sealed partial class GameContent
{
    // reading ~19k .def files dominates a cold start
    sealed record Snapshot(
        string Stamp,
        List<ItemInfo> Items,
        Dictionary<Guid, string> SlotKeys,
        Dictionary<Guid, Dictionary<Guid, Guid>> CompositeSlots,
        Dictionary<Guid, InventoryRules> InventoryRules,
        Dictionary<Guid, Guid> CompositeBases,
        Dictionary<Guid, int> AmmoCapacity);

    const int SnapshotFormat = 4;

    public static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Drydock", "cache");

    readonly Dictionary<string, string> _texts;
    readonly Snapshot _snapshot;

    public ItemCatalog Items { get; }

    GameContent(Snapshot snapshot, Dictionary<string, string> texts)
    {
        _snapshot = snapshot;
        _texts = texts;
        Items = ItemCatalog.From(snapshot.Items);
    }

    /// <param name="gameRoot">The SpaceEngineers2 folder holding GameData.</param>
    /// <param name="useCache">Read and write the snapshot in <see cref="CacheDir"/>.</param>
    public static GameContent Load(string gameRoot, string language = "en-US", bool useCache = true)
    {
        var content = Path.Combine(gameRoot, "GameData", "Vanilla", "Content");
        var texts = LoadTexts(Path.Combine(content, "MainMenuData", "Texts", language));
        var stamp = Stamp(content, language);
        var cacheFile = Path.Combine(CacheDir, $"content-{language}.json");
        if (useCache && ReadCache(cacheFile, stamp) is { } cached)
            return new GameContent(cached, texts);
        var snapshot = Scan(content, texts, stamp);
        if (useCache)
            WriteCache(cacheFile, snapshot);
        return new GameContent(snapshot, texts);
    }

    // content only changes with game updates
    static string Stamp(string content, string language)
    {
        var sets = new FileInfo(Path.Combine(content, "definitionsets.vrb"));
        return $"{SnapshotFormat}|{GameInstall.GameVersion()}|{sets.Length}|{sets.LastWriteTimeUtc.Ticks}|{language}";
    }

    static Snapshot? ReadCache(string file, string stamp)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file));
            return snapshot?.Stamp == stamp ? snapshot : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    static void WriteCache(string file, Snapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a cache that can't be written only costs speed
        }
    }

    static Snapshot Scan(string content, Dictionary<string, string> texts, string stamp)
    {
        var files = new Dictionary<Guid, string>();
        var slotKeys = new Dictionary<Guid, List<string>>();
        var compositeSlots = new Dictionary<Guid, Dictionary<Guid, Guid>>();
        var inventoryRules = new Dictionary<Guid, InventoryRules>();
        var ammoCapacity = new Dictionary<Guid, int>();
        foreach (var file in Directory.EnumerateFiles(content, "*.def", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (ValueGuid().Match(text) is not { Success: true } match)
                continue;
            var guid = Guid.Parse(match.Groups[1].Value);
            files.TryAdd(guid, file);
            // the only place slots get a name
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
            else if (text.Contains("AmmoDefinitionObjectBuilder\"", StringComparison.Ordinal) && Ammo(JsonNode.Parse(text)?["$Value"]) is { } ammo)
                ammoCapacity[ammo.Projectile] = Math.Max(ammo.Capacity, ammoCapacity.GetValueOrDefault(ammo.Projectile));
        }
        var definitions = DefinitionIndex.Load(Path.Combine(content, "definitionsets.vrb"));
        var bases = definitions.All.Where(d => d.BaseGuid != Guid.Empty && compositeSlots.ContainsKey(d.Guid))
            .ToDictionary(d => d.Guid, d => d.BaseGuid);
        return new Snapshot(
            stamp,
            ItemCatalog.Build(definitions, files, texts, content).Items.ToList(),
            slotKeys.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(KeyRank).First()),
            compositeSlots,
            inventoryRules,
            bases,
            ammoCapacity);
    }

    // the smelter's output is also keyed InventoryStockpile
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
        double? maxMass = value?["MaxMass"] is JsonValue m && m.TryGetValue<double>(out var mass) ? mass : null;
        int? maxStacks = value?["MaxStacks"] is JsonValue s && s.TryGetValue<int>(out var stacks) ? stacks : null;
        return new InventoryRules(types, [.. guids], maxMass, maxStacks);
    }

    static (Guid Projectile, int Capacity)? Ammo(JsonNode? value) =>
        value?["ProjectileDefinition"] is JsonValue p && Guid.TryParse(p.ToString(), out var projectile)
            && value["Capacity"] is JsonValue c && c.TryGetValue<int>(out var capacity)
            ? (projectile, capacity) : null;

    /// <summary>Rounds a full magazine holds for guns firing this projectile.</summary>
    public int? AmmoCapacity(Guid projectile) =>
        _snapshot.AmmoCapacity?.TryGetValue(projectile, out var capacity) == true ? capacity : null;

    /// <summary>What a slot of an entity accepts, following the composite's base chain. Null when the game data doesn't say.</summary>
    public InventoryRules? Rules(Guid? composite, Guid slot)
    {
        for (int depth = 0; composite is { } c && c != Guid.Empty && depth < 16; depth++)
        {
            if (_snapshot.CompositeSlots.TryGetValue(c, out var slots) && slots.TryGetValue(slot, out var def))
                return _snapshot.InventoryRules.GetValueOrDefault(def);
            composite = _snapshot.CompositeBases.TryGetValue(c, out var b) ? b : null;
        }
        return null;
    }

    public string Text(string key) => _texts.GetValueOrDefault(key, key);

    /// <summary>Short slot name for an inventory component key: main, consumables, datapads, input, output, cell0.</summary>
    public string SlotName(Guid slot)
    {
        if (!_snapshot.SlotKeys.TryGetValue(slot, out var key))
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
