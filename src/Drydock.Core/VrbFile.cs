using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace Drydock.Core;

/// <summary>
/// The raw VR3B container: header, entry table and checksums. Used to swap the main chunk without touching anything else.
/// See docs/vrb-format.md.
/// </summary>
public sealed class VrbFile
{
    public const int HeaderSize = 192;
    const uint Seed = 0x42335256; // "VR3B" as little-endian u32
    static readonly (string Name, int At)[] Entries = [("bundle", 16), ("type", 52), ("chunktable", 88), ("main", 132)];
    const int MainAt = 132;

    public byte[] Data { get; }

    public VrbFile(byte[] data)
    {
        if (data.Length < HeaderSize || !data.AsSpan(0, 4).SequenceEqual("VR3B"u8))
            throw new InvalidDataException("Not a VR3B archive.");
        Data = data;
    }

    public static VrbFile Load(string path) => new(File.ReadAllBytes(path));

    readonly record struct Entry(string Name, int At, long Offset, ulong Checksum, long Size, long RawSize, byte Compression);

    Entry Read(string name, int at) => new(
        name, at,
        BinaryPrimitives.ReadInt64LittleEndian(Data.AsSpan(at)),
        BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(at + 8)),
        BinaryPrimitives.ReadInt64LittleEndian(Data.AsSpan(at + 16)),
        BinaryPrimitives.ReadInt64LittleEndian(Data.AsSpan(at + 24)),
        Data[at + 32]);

    IEnumerable<Entry> Present() => Entries.Select(e => Read(e.Name, e.At)).Where(e => e.Offset != 0);

    static ulong Hash(ReadOnlySpan<byte> data) => XxHash64.HashToUInt64(data, Seed);

    static ulong HeaderHash(byte[] data)
    {
        var header = data.AsSpan(0, HeaderSize).ToArray();
        header.AsSpan(8, 8).Clear();
        return Hash(header);
    }

    public bool Verify(out string problem)
    {
        if (BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(8)) != HeaderHash(Data))
        {
            problem = "header checksum mismatch";
            return false;
        }
        foreach (var e in Present())
        {
            if (e.Offset + e.Size > Data.Length || Hash(Data.AsSpan((int)e.Offset, (int)e.Size)) != e.Checksum)
            {
                problem = $"{e.Name} checksum mismatch";
                return false;
            }
        }
        problem = "";
        return true;
    }

    /// <summary>A copy with the main chunk replaced by <paramref name="payload"/> (uncompressed), recompressed and re-checksummed.</summary>
    public VrbFile WithMain(byte[] payload)
    {
        var main = Read("main", MainAt);
        if (main.Offset != HeaderSize)
            throw new InvalidDataException("Main chunk is not directly after the header, layout unsupported.");
        byte[] stored = main.Compression switch
        {
            0 => payload,
            2 => Brotli(payload),
            _ => throw new InvalidDataException($"Writing compression {main.Compression} is not supported."),
        };

        long oldEnd = main.Offset + main.Size;
        long delta = stored.Length - main.Size;
        var tail = Data.AsSpan((int)oldEnd);
        var output = new byte[HeaderSize + stored.Length + tail.Length];
        Data.AsSpan(0, HeaderSize).CopyTo(output);
        stored.CopyTo(output, HeaderSize);
        tail.CopyTo(output.AsSpan(HeaderSize + stored.Length));

        foreach (var e in Present())
            if (e.Name != "main" && e.Offset >= oldEnd)
                BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(e.At), e.Offset + delta);
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(MainAt + 8), Hash(stored));
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(MainAt + 16), stored.Length);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(MainAt + 24), payload.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(8), HeaderHash(output));

        var result = new VrbFile(output);
        if (!result.Verify(out var problem))
            throw new InvalidOperationException($"Rewritten archive fails verification: {problem}");
        return result;
    }

    static byte[] Brotli(byte[] payload)
    {
        // same settings the Python tool used, the game loaded those fine
        using var encoder = new BrotliEncoder(quality: 11, window: 22);
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(payload.Length)];
        var status = encoder.Compress(payload, buffer, out _, out int written, isFinalBlock: true);
        if (status != System.Buffers.OperationStatus.Done)
            throw new InvalidOperationException($"Brotli compression failed: {status}");
        return buffer[..written];
    }
}
