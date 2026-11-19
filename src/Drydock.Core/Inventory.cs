using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

public sealed record ItemStack(Guid Definition, SerializedField Field)
{
    public SerializedField Amount => Field.Member("Amount")!;
    public SerializedField? MetaData => Field.Member("MetaData");
    public bool HasMetaData => MetaData?.Value is not null || MetaData?.Members.Length > 0;
}

public enum ObjectKind { Character, Ship, Station, Other }

/// <summary>A top-level entity of the world that holds inventories. <see cref="Name"/> is raw, it may hold {Loc:...} placeholders.</summary>
public sealed record WorldObject(ObjectKind Kind, string Name, int? OwnerId)
{
    public bool IsPlayer => Kind == ObjectKind.Character && Name == Inventory.PlayerCharacter;
}

/// <summary>A block's position on its grid in meters. Stays put as long as the block does.</summary>
public readonly record struct BlockPosition(double X, double Y, double Z);

/// <summary>An inventory component in a save. <see cref="Block"/> is the raw name of the block holding it, null on characters.</summary>
/// <param name="Composite">The composite definition of the entity holding the inventory, which says what each slot accepts.</param>
public sealed record Inventory(
    WorldObject Object, string? Block, BlockPosition? Position, Guid? Composite, IReadOnlyList<string> Owners, Guid Slot, SerializedField Field)
{
    public const string PlayerCharacter = "CompositeCharacterServer";

    /// <summary>The chain of entity debug names down to this inventory.</summary>
    public string Owner => string.Join(" / ", Owners);

    public SerializedField ItemsField => Field.Member("Items")!;

    public IReadOnlyList<ItemStack> Items =>
        ItemsField.Members.Select(m => new ItemStack(ParseGuid(m.Member("Definition")!), m)).ToList();

    static Guid ParseGuid(SerializedField f) => f.Value is Guid g ? g : Guid.Parse(f.GetShortValueString().TrimStart('$'));

    const string InventoryType = "Keen.Game2.Simulation.WorldObjects.Items.InventoryObjectBuilder";
    const string EntityType = "Keen.VRage.DCS.ObjectBuilders.EntityObjectBuilderObjectBuilder";

    // spawner templates and blueprint previews, not things that exist in the world
    static readonly HashSet<string> SkippedSubtrees = ["Encounters", "ProjectionData"];

    /// <summary>The owner id of the player's character, which is also the owner id of the player's grids.</summary>
    public static int? PlayerOwnerId(IEnumerable<Inventory> inventories) =>
        inventories.FirstOrDefault(i => i.Object.IsPlayer)?.Object.OwnerId;

    public static IReadOnlyList<Inventory> FindAll(SaveArchive archive)
    {
        var result = new List<Inventory>();
        Visit(archive.Root, new Context(), null, result);
        return result;
    }

    sealed class Context
    {
        public readonly List<string> Owners = [];
        public WorldObject? Object;
        public string? Block;
        public BlockPosition? Position;
        public Guid? Composite;
    }

    static void Visit(SerializedField field, Context ctx, SerializedField? parent, List<Inventory> result)
    {
        if (SkippedSubtrees.Contains(field.Name))
            return;
        var type = field.TypeName();
        if (type.EndsWith(InventoryType, StringComparison.Ordinal) && parent?.Member("Key") is { } key && ctx.Object != null)
        {
            result.Add(new Inventory(ctx.Object, ctx.Block, ctx.Position, ctx.Composite, ctx.Owners.ToArray(), ParseGuid(key), field));
            return;
        }
        if (!type.EndsWith(EntityType, StringComparison.Ordinal))
        {
            foreach (var member in field.Members)
                Visit(member, ctx, field, result);
            return;
        }

        var saved = (ctx.Object, ctx.Block, ctx.Position, ctx.Composite);
        ctx.Owners.Add(DebugName(field));
        ctx.Composite = field.Member("Definition") is { } definition ? ParseGuid(definition) : null;
        if (ctx.Object == null)
            ctx.Object = DescribeObject(field);
        else
            (ctx.Block, ctx.Position) = (BlockName(field), ReadPosition(field));
        foreach (var member in field.Members)
            Visit(member, ctx, field, result);
        ctx.Owners.RemoveAt(ctx.Owners.Count - 1);
        (ctx.Object, ctx.Block, ctx.Position, ctx.Composite) = saved;
    }

    static BlockPosition? ReadPosition(SerializedField entity)
    {
        foreach (var component in Components(entity))
        {
            if (!component.TypeName().EndsWith(".ChildTransformComponentObjectBuilder", StringComparison.Ordinal))
                continue;
            var p = component.Member("TransformWithEulerHint")?.Member("Transform")?.Member("Position");
            if (p?.Member("X")?.Value is IConvertible x && p.Member("Y")?.Value is IConvertible y && p.Member("Z")?.Value is IConvertible z)
                return new BlockPosition(Convert.ToDouble(x), Convert.ToDouble(y), Convert.ToDouble(z));
        }
        return null;
    }

    static string DebugName(SerializedField entity)
    {
        if (entity.Member("DebugName")?.Value is string { Length: > 0 } name)
            return name;
        return entity.Member("Definition")?.GetShortValueString() ?? "?";
    }

    static IEnumerable<SerializedField> Components(SerializedField entity) =>
        entity.Member("ObjectBuilders")?.Members.Select(c => c.Member("Value")).OfType<SerializedField>() ?? [];

    static WorldObject DescribeObject(SerializedField entity)
    {
        string? gridName = null;
        int? motion = null, owner = null;
        foreach (var component in Components(entity))
        {
            var type = component.TypeName();
            if (type.EndsWith(".CubeGridObjectBuilder", StringComparison.Ordinal))
                gridName = RawText(component.Member("DisplayName"));
            else if (type.EndsWith(".GridPhysicsObjectBuilder", StringComparison.Ordinal))
                motion = Convert.ToInt32(component.Member("MotionType")?.Value ?? -1);
            else if (type.EndsWith(".OwnershipObjectBuilder", StringComparison.Ordinal))
                owner = component.Member("Owner")?.Member("_id")?.Value is IConvertible id ? Convert.ToInt32(id) : null;
        }
        var debugName = DebugName(entity);
        if (gridName != null)
            // motion type 0 is static, the game's stations
            return new WorldObject(motion == 0 ? ObjectKind.Station : ObjectKind.Ship, gridName, owner);
        var kind = debugName.Contains("Character", StringComparison.Ordinal) ? ObjectKind.Character : ObjectKind.Other;
        return new WorldObject(kind, debugName, owner);
    }

    static string BlockName(SerializedField entity)
    {
        foreach (var component in Components(entity))
            if (RawText(component.Member("DisplayName")) is { Length: > 0 } name)
                return name;
        var debugName = DebugName(entity);
        int cut = debugName.IndexOf("_Server", StringComparison.Ordinal);
        return cut > 0 ? debugName[..cut] : debugName;
    }

    static string? RawText(SerializedField? localizableText) => localizableText?.Member("RawText")?.Value as string;
}
