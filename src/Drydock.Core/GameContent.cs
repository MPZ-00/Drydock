using System.Text.Json;
using System.Text.RegularExpressions;

namespace Drydock.Core;

/// <summary>What Drydock reads from the game's content folder: item definitions, localized texts and inventory slot names.</summary>
public sealed partial class GameContent
{
    readonly Dictionary<string, string> _texts;
    readonly Dictionary<Guid, string> _slotKeys;

    public ItemCatalog Items { get; }

    GameContent(ItemCatalog items, Dictionary<string, string> texts, Dictionary<Guid, string> slotKeys)
    {
        Items = items;
        _texts = texts;
        _slotKeys = slotKeys;
    }

    /// <param name="gameRoot">The SpaceEngineers2 folder holding GameData.</param>
    public static GameContent Load(string gameRoot, string language = "en-US")
    {
        var content = Path.Combine(gameRoot, "GameData", "Vanilla", "Content");
        var texts = LoadTexts(Path.Combine(content, "MainMenuData", "Texts", language));
        var files = new Dictionary<Guid, string>();
        var slotKeys = new Dictionary<Guid, string>();
        foreach (var file in Directory.EnumerateFiles(content, "*.def", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (ValueGuid().Match(text) is { Success: true } match)
                files.TryAdd(Guid.Parse(match.Groups[1].Value), file);
            // composite definitions name their components, which is the only place inventory slots get a name
            if (text.Contains("\"$Key\"", StringComparison.Ordinal))
                foreach (Match m in SlotKey().Matches(text))
                    slotKeys.TryAdd(Guid.Parse(m.Groups[2].Value), m.Groups[1].Value);
        }
        var definitions = DefinitionIndex.Load(Path.Combine(content, "definitionsets.vrb"));
        return new GameContent(ItemCatalog.Build(definitions, files, texts), texts, slotKeys);
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
