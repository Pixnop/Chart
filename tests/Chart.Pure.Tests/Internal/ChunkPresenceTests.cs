using Chart.Internal;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class ChunkPresenceTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(10, false)]
    public void NeedsMapChunk_Should_BeTrueInTheOverworldOnly_When_AskedForADimension(int dimension, bool expected)
    {
        Assert.Equal(expected, ChunkPresence.NeedsMapChunk(dimension));
    }

    [Fact]
    public void IsLoaded_Should_BeFalse_When_TheSliceIsAbsent()
    {
        Assert.False(ChunkPresence.IsLoaded(null));
    }

    [Fact]
    public void IsLoaded_Should_BeFalse_When_TheSliceIsNotAClientChunk()
    {
        Assert.False(ChunkPresence.IsLoaded(Substitute.For<IWorldChunk>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsLoaded_Should_FollowLoadedFromServer_When_TheSliceIsAClientChunk(bool loadedFromServer)
    {
        Assert.Equal(loadedFromServer, ChunkPresence.IsLoaded(ClientSlice(loadedFromServer)));
    }

    [Fact]
    public void IsNeighbourPresent_Should_NotNeedAMapChunk_When_TheDimensionIsCustom()
    {
        // A dimension's slices can all be there while the overworld's map chunk of the same
        // (x, z) is not: the slice alone decides.
        Assert.True(ChunkPresence.IsNeighbourPresent(10, mapChunk: null, ClientSlice(loadedFromServer: true)));
    }

    [Fact]
    public void IsNeighbourPresent_Should_IgnoreTheMapChunk_When_TheSliceOfACustomDimensionIsMissing()
    {
        // The map chunk there is the overworld's, and says nothing about the dimension.
        Assert.False(ChunkPresence.IsNeighbourPresent(10, Substitute.For<IMapChunk>(), slice: null));
        Assert.False(ChunkPresence.IsNeighbourPresent(10, Substitute.For<IMapChunk>(), ClientSlice(loadedFromServer: false)));
    }

    [Fact]
    public void IsNeighbourPresent_Should_NeedTheMapChunk_When_TheDimensionIsTheOverworld()
    {
        Assert.True(ChunkPresence.IsNeighbourPresent(0, Substitute.For<IMapChunk>(), slice: null));
        Assert.False(ChunkPresence.IsNeighbourPresent(0, mapChunk: null, ClientSlice(loadedFromServer: true)));
    }

    private static IClientChunk ClientSlice(bool loadedFromServer)
    {
        var slice = Substitute.For<IClientChunk>();
        slice.LoadedFromServer.Returns(loadedFromServer);
        return slice;
    }
}
