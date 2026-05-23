using System.Linq;
using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class MapTileStoreTests
{
    [Fact]
    public void Empty_Store_Has_No_Tiles()
    {
        var store = new MapTileStore();
        Assert.False(store.HasTile(0, 0));
        Assert.Null(store.GetTile(0, 0));
    }

    [Fact]
    public void Set_And_Get_Tile_Roundtrip()
    {
        var store = new MapTileStore();
        var tile = new byte[ChunkSampler.TileBytes];
        tile[0] = 0xAB; tile[ChunkSampler.TileBytes - 1] = 0xCD;
        store.SetTile(3, -7, tile);

        Assert.True(store.HasTile(3, -7));
        Assert.Equal(tile, store.GetTile(3, -7));
    }

    [Fact]
    public void ToBytes_FromBytes_Roundtrip_Preserves_Tiles()
    {
        var store = new MapTileStore();
        var tileA = new byte[ChunkSampler.TileBytes]; tileA[0] = 0x11;
        var tileB = new byte[ChunkSampler.TileBytes]; tileB[100] = 0x22;
        store.SetTile(0, 0, tileA);
        store.SetTile(5, -10, tileB);

        var restored = MapTileStore.FromBytes(store.ToBytes());

        Assert.True(restored.HasTile(0, 0));
        Assert.True(restored.HasTile(5, -10));
        Assert.Equal(tileA, restored.GetTile(0, 0));
        Assert.Equal(tileB, restored.GetTile(5, -10));
    }

    [Fact]
    public void FromBytes_Handles_Null_Empty_And_Corrupt_Input()
    {
        Assert.False(MapTileStore.FromBytes(null).HasTile(0, 0));
        Assert.False(MapTileStore.FromBytes(System.Array.Empty<byte>()).HasTile(0, 0));
        var corrupt = MapTileStore.FromBytes(new byte[] { 0xFF, 0x01, 0x02 });
        Assert.False(corrupt.HasTile(0, 0));
    }

    [Fact]
    public void AllTiles_Returns_All_Stored_Tiles()
    {
        var store = new MapTileStore();
        var tileA = new byte[ChunkSampler.TileBytes]; tileA[0] = 0xAA;
        var tileB = new byte[ChunkSampler.TileBytes]; tileB[0] = 0xBB;
        store.SetTile(1, 2, tileA);
        store.SetTile(-3, 4, tileB);

        var all = store.AllTiles().ToList();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Key.Cx == 1 && t.Key.Cz == 2 && t.Tile[0] == 0xAA);
        Assert.Contains(all, t => t.Key.Cx == -3 && t.Key.Cz == 4 && t.Tile[0] == 0xBB);
    }

    [Fact]
    public void AllTiles_Empty_Store_Returns_Empty_Sequence()
    {
        var store = new MapTileStore();
        Assert.Empty(store.AllTiles());
    }
}
