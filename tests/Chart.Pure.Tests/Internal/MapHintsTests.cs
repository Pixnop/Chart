using Chart.Internal;
using Manifold.Api;
using NSubstitute;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class MapHintsTests
{
    [Fact]
    public void ScanTopY_Should_BeNull_When_ThereIsNoDimensionOrNoHint()
    {
        Assert.Null(MapHints.ScanTopY(null));
        Assert.Null(MapHints.ScanTopY(DimensionWith(("displayName", "The Fractured Deep"))));
    }

    [Theory]
    [InlineData(MapHints.ScanTopKey)]
    [InlineData(MapHints.LegacyScanTopKey)]
    public void ScanTopY_Should_ReadTheDeclaredValue_When_SetUnderEitherKey(string key)
    {
        Assert.Equal(111, MapHints.ScanTopY(DimensionWith((key, 111))));
    }

    [Fact]
    public void ScanTopY_Should_PreferTheNamespacedKey_When_BothAreSet()
    {
        var dimension = DimensionWith((MapHints.LegacyScanTopKey, 50), (MapHints.ScanTopKey, 111));

        Assert.Equal(111, MapHints.ScanTopY(dimension));
    }

    [Fact]
    public void ScanTopY_Should_AcceptOtherIntegerTypes()
    {
        Assert.Equal(111, MapHints.ScanTopY(DimensionWith((MapHints.ScanTopKey, 111L))));
        Assert.Equal(111, MapHints.ScanTopY(DimensionWith((MapHints.ScanTopKey, (short)111))));
    }

    [Theory]
    [InlineData("111")]
    [InlineData(111.5)]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(null)]
    public void ScanTopY_Should_BeNull_When_TheValueIsNotAPositiveInteger(object? value)
    {
        Assert.Null(MapHints.ScanTopY(DimensionWith((MapHints.ScanTopKey, value))));
    }

    private static IDimension DimensionWith(params (string Key, object? Value)[] metadata)
    {
        var dimension = Substitute.For<IDimension>();
        dimension.Metadata.Returns(metadata.ToDictionary(m => m.Key, m => m.Value));
        return dimension;
    }
}
