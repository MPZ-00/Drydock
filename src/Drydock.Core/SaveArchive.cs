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

    /// <summary>Parsed from an edited main chunk without rebuilding the file. <see cref="File"/> is still the original.</summary>
    public bool IsPreview { get; }

    SaveArchive(string path, VrbFile file, ArchiveHeader header, ReflectedTypeModel typeModel, byte[] mainChunk, SerializedField root, bool isPreview = false)
    {
        Path = path;
        File = file;
        Header = header;
        TypeModel = typeModel;
        MainChunk = mainChunk;
        Root = root;
        IsPreview = isPreview;
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
        return new SaveArchive(path, file, header, model, buffer, ParseMain(context, model, header, buffer, path));
    }

    static SerializedField ParseMain(SerializationContext context, ReflectedTypeModel model, ArchiveHeader header, byte[] buffer, string path)
    {
        var main = header.MainChunk;
        var rootSchema = model.GetEntry(main.RootType)
            ?? throw new InvalidDataException($"{path}: main chunk root type is missing from the type table.");
        var root = new ReflectionBinaryParser(context, new VRageBinaryReader(buffer), model, rootSchema, "[Main]", main.DeltaEncoded).Parse();
        if (root.HasError)
            throw new InvalidDataException($"{path}: parsed with errors, the save layout is not understood.");
        return root;
    }

    /// <summary>Parse an edited main chunk with this archive's type table, skipping compression and checksums.</summary>
    public SaveArchive Preview(byte[] mainChunk)
    {
        VRageRuntime.EnsureInitialized();
        using var context = new SerializationContext(new MemoryStream(), System.IO.Path.GetFileName(Path));
        return new SaveArchive(Path, File, Header, TypeModel, mainChunk, ParseMain(context, TypeModel, Header, mainChunk, Path), isPreview: true);
    }

    /// <summary>A new archive with the main chunk replaced, re-parsed from the rebuilt bytes.</summary>
    public SaveArchive WithMainChunk(byte[] mainChunk) => Parse(Path, File.WithMain(mainChunk));
}
