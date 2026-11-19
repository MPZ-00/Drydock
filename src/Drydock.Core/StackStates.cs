using System.Buffers.Binary;
using System.Globalization;
using Keen.VRage.Library.Serialization.Binary.Reflection;

namespace Drydock.Core;

/// <summary>Reads and sets one value in the metadata a stack carries, such as charge or ammo.</summary>
public interface IStackState
{
    string Describe(ItemStack stack);

    /// <summary>The full value in the codec's own unit (1 for a charge ratio, rounds for ammo).</summary>
    decimal Max(ItemStack stack);

    bool WholeNumbers { get; }

    /// <summary>A complete stack encoding (amount 1): <paramref name="template"/> with the value replaced.</summary>
    byte[] Encode(SaveArchive archive, ItemStack template, decimal value);
}

public static class StackStates
{
    /// <summary>The codec for this stack's metadata, or null when its layout is not known.</summary>
    public static IStackState? For(GameContent content, ItemStack sample)
    {
        var type = sample.MetaData?.TypeName() ?? "";
        if (type.EndsWith(".ResourceContainerItemMetaData", StringComparison.Ordinal))
            return Charge.Instance;
        if (type.EndsWith(".GunItemMetaData", StringComparison.Ordinal))
            return new Ammo(content);
        return null;
    }

    static SerializedField Field(ItemStack stack, string name) =>
        stack.MetaData?.Member(name) ?? throw new ArgumentException($"the stack has no {name}.");

    static byte[] Replace(SaveArchive archive, ItemStack template, SerializedField field, byte[] value)
    {
        var bytes = archive.MainChunk[template.Field.Start..template.Field.End];
        ItemCodec.Amount(1).CopyTo(bytes, template.Amount.Start - template.Field.Start);
        if (value.Length != field.End - field.Start)
            throw new InvalidOperationException("The value's encoding has a different size than the save's.");
        value.CopyTo(bytes, field.Start - template.Field.Start);
        return bytes;
    }

    static void CheckRange(IStackState codec, ItemStack template, decimal value)
    {
        if (value < 0 || value > codec.Max(template))
            throw new ArgumentException($"use a value from 0 to {codec.Max(template)}.");
        if (codec.WholeNumbers && value != decimal.Truncate(value))
            throw new ArgumentException("use a whole number.");
    }

    /// <summary>Power cells and modules: FillRatio, a FixedPoint from 0 to 1.</summary>
    sealed class Charge : IStackState
    {
        public static readonly Charge Instance = new();

        public bool WholeNumbers => false;

        public decimal Max(ItemStack stack) => 1;

        public string Describe(ItemStack stack) =>
            (Convert.ToDecimal(Field(stack, "FillRatio").Value, CultureInfo.InvariantCulture) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        public byte[] Encode(SaveArchive archive, ItemStack template, decimal value)
        {
            CheckRange(this, template, value);
            return Replace(archive, template, Field(template, "FillRatio"), ItemCodec.Fixed(value));
        }
    }

    /// <summary>Guns: Ammo as an Int32, the full magazine from the ammo definition of the gun's projectile.</summary>
    sealed class Ammo(GameContent content) : IStackState
    {
        public bool WholeNumbers => true;

        public decimal Max(ItemStack stack) =>
            Field(stack, "Projectile").Value is { } p && Guid.TryParse(p.ToString(), out var projectile) && content.AmmoCapacity(projectile) is { } capacity
                ? capacity
                : throw new ArgumentException("the magazine size of this gun is not known.");

        public string Describe(ItemStack stack)
        {
            var ammo = Convert.ToInt32(Field(stack, "Ammo").Value, CultureInfo.InvariantCulture);
            try
            {
                return $"{ammo}/{Max(stack)}";
            }
            catch (ArgumentException)
            {
                return ammo.ToString(CultureInfo.InvariantCulture);
            }
        }

        public byte[] Encode(SaveArchive archive, ItemStack template, decimal value)
        {
            CheckRange(this, template, value);
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)value);
            return Replace(archive, template, Field(template, "Ammo"), bytes);
        }
    }
}
