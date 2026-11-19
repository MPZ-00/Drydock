using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

public sealed record ItemStack(Guid Definition, SerializedField Field)
{
    public SerializedField Amount => Field.Member("Amount")!;
    public SerializedField? MetaData => Field.Member("MetaData");
    public bool HasMetaData => MetaData?.Value is not null || MetaData?.Members.Length > 0;
}

/// <summary>An inventory component in a save, with the chain of entities that own it.</summary>
public sealed record Inventory(IReadOnlyList<string> Owners, Guid Slot, SerializedField Field)
{
    public string Owner => string.Join(" / ", Owners);

    public SerializedField ItemsField => Field.Member("Items")!;

    public IReadOnlyList<ItemStack> Items =>
        ItemsField.Members.Select(m => new ItemStack(ParseGuid(m.Member("Definition")!), m)).ToList();

    static Guid ParseGuid(SerializedField f) => f.Value is Guid g ? g : Guid.Parse(f.GetShortValueString().TrimStart('$'));

    const string InventoryType = "Keen.Game2.Simulation.WorldObjects.Items.InventoryObjectBuilder";
    const string EntityType = "Keen.VRage.DCS.ObjectBuilders.EntityObjectBuilderObjectBuilder";

    // spawner templates and blueprint previews, not things the player owns
    static readonly HashSet<string> SkippedSubtrees = ["Encounters", "ProjectionData"];

    public static IReadOnlyList<Inventory> FindAll(SaveArchive archive)
    {
        var result = new List<Inventory>();
        Visit(archive.Root, [], null, result);
        return result;
    }

    static void Visit(SerializedField field, List<string> owners, SerializedField? parent, List<Inventory> result)
    {
        if (SkippedSubtrees.Contains(field.Name))
            return;
        var type = field.TypeName();
        if (type.EndsWith(InventoryType, StringComparison.Ordinal) && parent?.Member("Key") is { } key)
        {
            result.Add(new Inventory(owners.ToArray(), ParseGuid(key), field));
            return;
        }
        bool isEntity = type.EndsWith(EntityType, StringComparison.Ordinal);
        if (isEntity)
            owners.Add(EntityName(field));
        foreach (var member in field.Members)
            Visit(member, owners, field, result);
        if (isEntity)
            owners.RemoveAt(owners.Count - 1);
    }

    static string EntityName(SerializedField entity)
    {
        if (entity.Member("DebugName")?.Value is string { Length: > 0 } name)
            return name;
        return entity.Member("Definition")?.GetShortValueString() ?? "?";
    }
}
