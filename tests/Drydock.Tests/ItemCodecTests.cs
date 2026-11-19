using Drydock.Core;

namespace Drydock.Tests;

public class ItemCodecTests
{
    [Theory]
    [InlineData(0, "00000000")]
    [InlineData(8, "00080000")]
    [InlineData(300, "002C0100")]
    public void Count_matches_save_encoding(int count, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(ItemCodec.Count(count)));
        Assert.Equal(count, ItemCodec.ReadCount(Convert.FromHexString(hex)));
    }

    [Fact]
    public void Amount_matches_save_encoding()
    {
        // 70 rifle magazines as stored in a real save
        Assert.Equal("00000000000000004600000000000000", Convert.ToHexString(ItemCodec.Amount(70m)));
    }

    [Fact]
    public void Amount_drops_trailing_zeros()
    {
        Assert.Equal(ItemCodec.Amount(12.5m), ItemCodec.Amount(12.500000m));
    }

    [Fact]
    public void Amount_rejects_non_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemCodec.Amount(0));
    }

    [Fact]
    public void Stack_without_metadata_is_34_bytes()
    {
        var guid = Guid.Parse("64ceb520-d2f0-433f-88fa-5ddc900b5f60");
        var bytes = ItemCodec.Stack(guid, 70);
        Assert.Equal("8120B5CE64F0D23F4388FA5DDC900B5F6000000000000000004600000000000000FF", Convert.ToHexString(bytes));
    }
}
