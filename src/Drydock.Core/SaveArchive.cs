using Keen.VRage.Library.Serialization;
using Keen.VRage.Library.Serialization.Binary;
using Keen.VRage.Library.Serialization.Binary.Archive;
using Keen.VRage.Library.Serialization.Binary.Reflection;
using Keen.VRage.Library.Serialization.Binary.TypeModel.Reflection;

namespace Drydock.Core;

/// <summary>
/// A VR3B archive parsed with the type layout stored in the file itself, so it needs none of the game's own types.
/// </summary>
public sealed class SaveArchive
{
    public string Path { get; }

    /// <summary>The whole file as read (or as rebuilt by <see cref="WithMainChunk"/>).</summary>
    public VrbFile File { get; }

    public ArchiveHeader Header { get; }
    public ReflectedTypeModel TypeModel { get; }

    /// <summary>The decompressed main chunk. Field offsets in <see cref="Root"/> point into this.</summary>
    public byte[] MainChunk { get; }

    public SerializedField Root { get; }

    SaveArchive(string path, VrbFile file, ArchiveHeader header, ReflectedTypeModel typeModel, byte[] mainChunk, SerializedField root)
    {
        Path = path;
        File = file;
        Header = header;
        TypeModel = typeModel;
        MainChunk = mainChunk;
        Root = root;
    }

    public static SaveArchive Load(string path) => Parse(path, VrbFile.Load(path));

    /// <summary>Parse an archive; checksums are validated by the game's parser on the way.</summary>
    public static SaveArchive Parse(string path, VrbFile file)
    {
        VRageRuntime.EnsureInitialized();
        using var stream = new MemoryStream(file.Data, writable: false);
        using var context = new SerializationContext(stream, System.IO.Path.GetFileName(path));
        var parser = new BinaryArchiveParser(context);
        var header = parser.ReadHeader();
        var model = parser.ReadReflectionTypeTable(in header);
        var main = header.MainChunk;
        var buffer = new byte[main.UncompressedSize];
        parser.ReadChunk(in main, buffer, "[Main]");
        var rootSchema = model.GetEntry(main.RootType)
            ?? throw new InvalidDataException($"{path}: main chunk root type is missing from the type table.");
        var root = new ReflectionBinaryParser(context, new VRageBinaryReader(buffer), model, rootSchema, "[Main]", main.DeltaEncoded).Parse();
        if (root.HasError)
            throw new InvalidDataException($"{path}: parsed with errors, the save layout is not understood.");
        return new SaveArchive(path, file, header, model, buffer, root);
    }

    /// <summary>A new archive with the main chunk replaced, re-parsed from the rebuilt bytes.</summary>
    public SaveArchive WithMainChunk(byte[] mainChunk) => Parse(Path, File.WithMain(mainChunk));
}
