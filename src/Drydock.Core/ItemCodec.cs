using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Drydock.Core;

/// <summary>Binary encodings of the values an inventory edit writes. Layouts were read off real saves, see docs.</summary>
public static class ItemCodec
{
    const byte NamePrefix = 0x81;
    const byte NullPrefix = 0xFF;

    /// <summary>A collection length: 4 bytes, the high byte first (it doubles as the null/value prefix), the rest little-endian.</summary>
    public static byte[] Count(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.RotateLeft((uint)count, 8));
        return bytes;
    }

    public static int ReadCount(ReadOnlySpan<byte> bytes) => (int)uint.RotateRight(BinaryPrimitives.ReadUInt32LittleEndian(bytes), 8);

    /// <summary>An amount: the 16 bytes of a .NET decimal as laid out in memory.</summary>
    public static byte[] Amount(decimal value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Amount must be positive.");
        // FixedPoint keeps 6 decimal places; normalize away trailing zeros like the game's own values
        value = decimal.Round(value, 6) / 1.000000000000000000000000000000000m;
        var bytes = new byte[16];
        MemoryMarshal.Write(bytes, in value);
        return bytes;
    }

    public static decimal ReadAmount(ReadOnlySpan<byte> bytes) => MemoryMarshal.Read<decimal>(bytes);

    /// <summary>A definition reference: the name prefix and the GUID.</summary>
    public static byte[] DefinitionRef(Guid guid)
    {
        var bytes = new byte[17];
        bytes[0] = NamePrefix;
        guid.TryWriteBytes(bytes.AsSpan(1));
        return bytes;
    }

    /// <summary>An item stack without metadata.</summary>
    public static byte[] Stack(Guid definition, decimal amount) =>
        [.. DefinitionRef(definition), .. Amount(amount), NullPrefix];
}
