using Chart.Internal;
using Vintagestory.API.MathTools;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public class WaypointDimensionTests
{
    [Fact]
    public void EngineDimensionBoundary_IsTheExpected32768Blocks()
    {
        // The whole intrinsic-dimension scheme rests on this engine constant:
        // vanilla /waypoint add stores Pos.XYZ whose Y is InternalY
        // (y + dimension * DimensionBoundary). If this moves, WaypointDimension
        // decodes garbage.
        Assert.Equal(32768, BlockPos.DimensionBoundary);
    }

    [Theory]
    [InlineData(8.0, 0)]
    [InlineData(16383.9, 0)]
    [InlineData(32768.0, 1)]
    [InlineData(49151.9, 1)]
    [InlineData(32776.0, 1)]
    [InlineData(98304.0, 3)]
    public void DimensionOf_DecodesTheDimensionSlice(double waypointY, int expectedDim)
    {
        Assert.Equal(expectedDim, WaypointDimension.DimensionOf(waypointY));
    }

    [Fact]
    public void DimensionOf_TreatsSlightlyNegativeY_AsOverworld()
    {
        // Bedrock-level glitches can produce Y just below zero; those pins are
        // overworld pins, not dimension -1.
        Assert.Equal(0, WaypointDimension.DimensionOf(-0.5));
    }

    [Theory]
    [InlineData(-35.0, 0)]
    [InlineData(-35.0, 1)]
    [InlineData(-35.0, 10)]
    public void DimensionOf_Should_FindTheDimension_When_ThePinIsUnderItsY0(double y, int dimension)
    {
        // The void starts to kill under y -30 and the death pin is stored where the fall ended:
        // just under the dimension's own slice of the world.
        Assert.Equal(dimension, WaypointDimension.DimensionOf(WaypointDimension.InternalY(y, dimension)));
    }

    [Theory]
    [InlineData(8.0, 0, true)]
    [InlineData(8.0, 1, false)]
    [InlineData(32776.0, 1, true)]
    [InlineData(32776.0, 0, false)]
    public void IsVisibleIn_MatchesWaypointDimAgainstCurrentDim(double waypointY, int currentDim, bool expected)
    {
        Assert.Equal(expected, WaypointDimension.IsVisibleIn(waypointY, currentDim));
    }

    [Theory]
    [InlineData(3.0, 0)]
    [InlineData(3.0, 10)]
    [InlineData(250.5, 1)]
    public void InternalY_RoundTripsThroughDimensionOf(double y, int dimension)
    {
        double stored = WaypointDimension.InternalY(y, dimension);

        Assert.Equal(dimension, WaypointDimension.DimensionOf(stored));
        Assert.Equal(y, stored - (dimension * 32768.0));
    }
}
