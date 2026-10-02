namespace Chart.Pure.Tests.Internal;

using Chart.Internal;
using Xunit;

public class HillshadeTests
{
    [Fact]
    public void Factor_Should_BeNeutral_When_TheGroundIsFlat() =>
        Assert.Equal(1f, Hillshade.Factor(0, 0, 0));

    [Fact]
    public void Factor_Should_Lighten_When_ThePixelStandsAboveItsNeighbours() =>
        Assert.Equal(1.08f + (3f / 12f / 1.25f), Hillshade.Factor(3, 3, 0), 4);

    [Fact]
    public void Factor_Should_Darken_When_ThePixelSitsBelowItsNeighbours() =>
        Assert.Equal(0.92f - (3f / 12f / 1.25f), Hillshade.Factor(-3, -1, 0), 4);

    [Fact]
    public void Factor_Should_CapTheSlope_When_TheStepIsACliff() =>
        Assert.Equal(Hillshade.Factor(4, 4, 4), Hillshade.Factor(90, 90, 90));
}
