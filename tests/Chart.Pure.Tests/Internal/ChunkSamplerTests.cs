using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class ChunkSamplerTests
{
    [Fact]
    public void Sample_Should_Return_Transparent_Tile_For_Empty_Column_Data()
    {
        var heights = new int[32 * 32]; // all 0 (no surface)
        var topBlockIds = new int[32 * 32]; // all 0 (air)
        var palette = (int blockId) => 0u; // RGBA 0 = fully transparent

        var tile = ChunkSampler.Sample(heights, topBlockIds, palette);

        Assert.Equal(32 * 32 * 4, tile.Length);
        Assert.All(tile, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Sample_Should_Use_Palette_Color_For_Each_Column_Top_Block()
    {
        var heights = new int[32 * 32];
        var topBlockIds = new int[32 * 32];
        for (int i = 0; i < topBlockIds.Length; i++) topBlockIds[i] = 1;
        var palette = (int blockId) => 0xFF80FF80u; // RGBA pale green

        var tile = ChunkSampler.Sample(heights, topBlockIds, palette);

        // First pixel: R=0xFF, G=0x80, B=0xFF, A=0x80 (depending on byte order; the test asserts our chosen order).
        Assert.Equal(0xFF, tile[0]);
        Assert.Equal(0x80, tile[1]);
        Assert.Equal(0xFF, tile[2]);
        Assert.Equal(0x80, tile[3]);
    }

    [Fact]
    public void Sample_Should_Mix_Palettes_When_Columns_Differ()
    {
        var heights = new int[32 * 32];
        var topBlockIds = new int[32 * 32];
        topBlockIds[0] = 1;
        topBlockIds[1] = 2;
        var palette = (int blockId) => blockId == 1 ? 0xFFFF0000u : 0xFF0000FFu;

        var tile = ChunkSampler.Sample(heights, topBlockIds, palette);

        Assert.Equal(0xFF, tile[0]); Assert.Equal(0xFF, tile[1]); Assert.Equal(0x00, tile[2]); Assert.Equal(0x00, tile[3]);
        Assert.Equal(0xFF, tile[4]); Assert.Equal(0x00, tile[5]); Assert.Equal(0x00, tile[6]); Assert.Equal(0xFF, tile[7]);
    }
}
