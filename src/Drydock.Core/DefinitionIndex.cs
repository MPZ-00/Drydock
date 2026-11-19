namespace Drydock.Core;

public sealed record DefinitionEntry(Guid Guid, string Type, Guid BaseGuid, int PartialKind);

/// <summary>The game's list of every content definition, read from definitionsets.vrb.</summary>
public sealed class DefinitionIndex
{
    readonly Dictionary<Guid, DefinitionEntry> _entries;

    public IReadOnlyCollection<DefinitionEntry> All => _entries.Values;

    DefinitionIndex(Dictionary<Guid, DefinitionEntry> entries) => _entries = entries;

    public DefinitionEntry? Find(Guid guid) => _entries.GetValueOrDefault(guid);

    public static DefinitionIndex Load(string path)
    {
        var archive = SaveArchive.Load(path);
        var entries = new Dictionary<Guid, DefinitionEntry>();
        foreach (var set in archive.Root.Member("DefinitionSets")!.Members)
        {
            var definitions = set.Member("$Value")?.Member("_definitions");
            if (definitions == null)
                continue;
            foreach (var pair in definitions.Members)
            {
                var v = pair.Member("$Value")!;
                var guid = (Guid)v.Member("Guid")!.Value!;
                entries.TryAdd(guid, new DefinitionEntry(
                    guid,
                    v.Member("Type")!.GetShortValueString(),
                    v.Member("BaseGuid")?.Value as Guid? ?? Guid.Empty,
                    Convert.ToInt32(v.Member("PartialKind")?.Value ?? 0)));
            }
        }
        return new DefinitionIndex(entries);
    }
}
