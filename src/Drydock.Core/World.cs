namespace Drydock.Core;

/// <summary>A loaded save with the game content needed to name, check and edit its inventories.</summary>
public sealed partial class World
{
    public string Path { get; }
    public SaveArchive Archive { get; }
    public GameContent Content { get; }
    public IReadOnlyList<Inventory> Inventories { get; }
    public int? PlayerOwner { get; }

    /// <summary>The save file's size and write time when it was read, to notice the game saving over it.</summary>
    public long LoadedLength { get; }
    public DateTime LoadedWriteTimeUtc { get; }

    readonly Dictionary<Inventory, string> _blockLabels;

    World(string path, SaveArchive archive, GameContent content, int? playerOwner, long length, DateTime writeTime)
    {
        Path = path;
        Archive = archive;
        Content = content;
        Inventories = Inventory.FindAll(archive);
        PlayerOwner = playerOwner;
        LoadedLength = length;
        LoadedWriteTimeUtc = writeTime;
        _blockLabels = Label(content, Inventories);
    }

    /// <summary>Wraps an archive already read from <paramref name="path"/>.</summary>
    public static World From(string path, SaveArchive archive, GameContent content, FileInfo loadedFile)
    {
        var inventories = Inventory.FindAll(archive);
        return new World(path, archive, content, Inventory.PlayerOwnerId(inventories), loadedFile.Length, loadedFile.LastWriteTimeUtc);
    }

    public static World Load(string savePath, GameContent content)
    {
        var file = new FileInfo(savePath);
        file.Refresh();
        return From(savePath, SaveArchive.Load(savePath), content, file);
    }

    /// <summary>The same save after an edit. Inventory indices stay the same since edits never add or remove inventories.</summary>
    public World WithArchive(SaveArchive edited) =>
        new(Path, edited, Content, PlayerOwner, LoadedLength, LoadedWriteTimeUtc);

    internal World WithoutOwner() => new(Path, Archive, Content, null, LoadedLength, LoadedWriteTimeUtc);

    // grid positions survive the game saving, walk order does not
    static Dictionary<Inventory, string> Label(GameContent content, IReadOnlyList<Inventory> inventories)
    {
        var labels = new Dictionary<Inventory, string>(ReferenceEqualityComparer.Instance);
        var objectIds = new Dictionary<WorldObject, int>(ReferenceEqualityComparer.Instance);
        var withBlock = inventories.Where(i => i.Block != null);
        foreach (var group in withBlock.GroupBy(i => (objectIds.TryAdd(i.Object, objectIds.Count) ? objectIds[i.Object] : objectIds[i.Object], content.Localize(i.Block!))))
        {
            var name = group.Key.Item2;
            var blocks = group.Select(i => i.Position).Distinct()
                .OrderBy(p => p?.X).ThenBy(p => p?.Y).ThenBy(p => p?.Z).ToList();
            foreach (var inv in group)
                labels[inv] = blocks.Count < 2 ? name : $"{name} ({blocks.IndexOf(inv.Position) + 1})";
        }
        return labels;
    }

    /// <summary>The block's name as shown and typed, numbered where one object has several blocks of that name. Empty without a block.</summary>
    public string BlockLabel(Inventory inv) => _blockLabels.GetValueOrDefault(inv, "");

    public string ObjectName(WorldObject o) => o.IsPlayer ? "player" : Content.Localize(o.Name);

    /// <summary>How to name an inventory: player, player:datapads, Aquila/Cargo Container.</summary>
    public string Ref(Inventory inv)
    {
        var name = ObjectName(inv.Object);
        if (inv.Block != null)
            name += "/" + BlockLabel(inv);
        var slot = Content.SlotName(inv.Slot);
        return slot == "main" ? name : $"{name}:{slot}";
    }

    public bool IsYours(WorldObject o) =>
        o.IsPlayer || (PlayerOwner != null && o.OwnerId == PlayerOwner && o.Kind != ObjectKind.Character);

    public InventoryRules? Rules(Inventory inv) => Content.Rules(inv.Composite, inv.Slot);

    /// <summary>Whether stacks of this item carry their own data (charge, ammo, datapad content) anywhere in the save.</summary>
    public bool HasState(Guid item) =>
        (_stateful ??= Inventories.SelectMany(i => i.Items).Where(s => s.HasMetaData).Select(s => s.Definition).ToHashSet()).Contains(item);

    HashSet<Guid>? _stateful;

    public double Mass(Inventory inv) =>
        inv.Items.Sum(s => (double)(decimal)s.Amount.Value! * (Content.Items.Find(s.Definition)?.MassPerUnit ?? 0));
}
