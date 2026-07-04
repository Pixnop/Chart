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
    [InlineData(32767.9, 0)]
    [InlineData(32768.0, 1)]
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
    [InlineData(8.0, 0, true)]
    [InlineData(8.0, 1, false)]
    [InlineData(32776.0, 1, true)]
    [InlineData(32776.0, 0, false)]
    public void IsVisibleIn_MatchesWaypointDimAgainstCurrentDim(double waypointY, int currentDim, bool expected)
    {
        Assert.Equal(expected, WaypointDimension.IsVisibleIn(waypointY, currentDim));
    }
}
