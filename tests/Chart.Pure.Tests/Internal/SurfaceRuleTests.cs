using Chart.Internal;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class SurfaceRuleTests
{
    private static readonly Block Air = new() { BlockId = 0 };

    private static readonly BlockPos Pos = new(10, 20, 30, 0);

    [Fact]
    public void BlockAt_Should_ReturnTheBlock_When_ItStopsRain()
    {
        var stone = new Block { BlockId = 3 };

        Assert.Same(stone, SurfaceRule.BlockAt(AccessorReading(stone), Pos, Air));
    }

    [Fact]
    public void BlockAt_Should_ReturnAir_When_TheBlockLetsRainThrough()
    {
        var tallGrass = new Block { BlockId = 4, RainPermeable = true };

        Assert.Same(Air, SurfaceRule.BlockAt(AccessorReading(tallGrass), Pos, Air));
    }

    [Fact]
    public void BlockAt_Should_ReturnAir_When_NoBlockIsThere()
    {
        var accessor = Substitute.For<IBlockAccessor>();
        accessor.GetBlock(Pos, BlockLayersAccess.FluidOrSolid).Returns(_ => null!);

        Assert.Same(Air, SurfaceRule.BlockAt(accessor, Pos, Air));
    }

    [Fact]
    public void BlockAt_Should_ReturnTheFluid_When_APlantStandsInIt()
    {
        // Seagrass in water: the solid layer, which a plain read gives first, holds the plant; the
        // engine's rain height map reads the fluid first and stops on the water.
        var water = new Block { BlockId = 5 };
        var seagrass = new Block { BlockId = 6, RainPermeable = true };
        var accessor = Substitute.For<IBlockAccessor>();
        accessor.GetBlock(Pos, BlockLayersAccess.FluidOrSolid).Returns(water);
        accessor.GetBlock(Pos, BlockLayersAccess.Solid).Returns(seagrass);
        accessor.GetBlock(Pos).Returns(seagrass);

        Assert.Same(water, SurfaceRule.BlockAt(accessor, Pos, Air));
    }

    private static IBlockAccessor AccessorReading(Block block)
    {
        var accessor = Substitute.For<IBlockAccessor>();
        accessor.GetBlock(Pos, BlockLayersAccess.FluidOrSolid).Returns(block);
        return accessor;
    }
}
