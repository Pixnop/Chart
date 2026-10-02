namespace Chart.Scenarios;

using Atlas.XUnit;
using Chart.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Runs Chart's scan over the real blocks of the headless server. The layer that draws the map
/// needs a game client, which Atlas cannot boot, but the rule it scans by and the tile it shades
/// from are plain code over a block accessor, and the server has one too. The scenarios change
/// blocks and put them back, so they live in a class of their own, which gets a world of its own.
/// </summary>
[Trait("Category", "E2E")]
public class ScanScenarios : ChartScenarioBase
{
    private const int ChunkSize = 32;

    // Matches the fixture's FixedSpawn (512, 8, 512), a chunk corner, and its CavernWorldgen: a
    // granite floor at y 1..4 under a roof, the dimension declaring its scan top inside the roof.
    private const int CavernTileX = 512;
    private const int CavernTileZ = 512;
    private const int CavernFloorY = 4;
    private const int CavernDeclaredScanTop = 21;

    /// <summary>
    /// A custom dimension is scanned with SurfaceRule so that it is mapped like the overworld, whose
    /// map is drawn from the engine's RainHeightMap. Pin that the two agree on the real engine, on
    /// the overworld column at the world spawn as it gets blocks (the engine updates the height map
    /// on every SetBlock): the bare surface, a plant that lets rain through, a solid block higher
    /// up, water, and a plant standing in that water. The plant is the first one of the block
    /// registry that lets rain through and is not a block entity; its code is in the failure
    /// messages. A scan that stops on the first block that is not air gets the plant wrong, which
    /// is why the rule exists.
    /// </summary>
    [AtlasScenario]
    public async Task SurfaceRule_Should_FindTheEnginesRainHeight_When_BlocksAreAddedToAnOverworldColumn()
    {
        IWorldAccessor world = World.Api.World;
        IBlockAccessor accessor = world.BlockAccessor;
        Block air = world.GetBlock(0)!;
        Block plant = world.Blocks.First(b =>
            b != null && b.Id != 0 && b.RainPermeable && !b.ForFluidsLayer && b.EntityClass == null
            && b.BlockMaterial == EnumBlockMaterial.Plant);
        int graniteId = world.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;
        int waterId = world.GetBlock(new AssetLocation("game", "water-still-7"))!.BlockId;

        // The world spawn column, loaded from the start.
        BlockPos spawn = World.Spawn;
        int x = spawn.X;
        int z = spawn.Z;
        IMapChunk mapChunk = accessor.GetMapChunk(x / ChunkSize, z / ChunkSize)!;

        int EngineHeight() => mapChunk.RainHeightMap[((z % ChunkSize) * ChunkSize) + (x % ChunkSize)];

        int ScanHeight(Func<BlockPos, int> idAt) =>
            SurfaceScan.Find(y => idAt(new BlockPos(x, y, z, 0)), accessor.MapSizeY - 1, skipCeiling: false);

        void AssertHeight(string situation, int expected)
        {
            int engine = EngineHeight();
            int scanned = ScanHeight(pos => SurfaceRule.BlockAt(accessor, pos, air).Id);
            Assert.True(engine == expected, $"{situation}: the engine's rain height is {engine}, expected {expected}.");
            Assert.True(scanned == engine, $"{situation}: the engine's rain height is {engine}, the scan found {scanned}.");
        }

        void Clear(BlockPos pos)
        {
            accessor.SetBlock(0, pos, BlockLayersAccess.Solid);
            accessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
        }

        int ground = EngineHeight();
        var above = new BlockPos(x, ground + 1, z, 0);
        var high = new BlockPos(x, ground + 6, z, 0);

        try
        {
            AssertHeight("the bare surface", ground);

            accessor.SetBlock(plant.Id, above);
            AssertHeight($"a plant that lets rain through ({plant.Code}) on the surface", ground);
            int naive = ScanHeight(pos => accessor.GetBlock(pos).Id);
            Assert.True(naive != ground, $"A scan for the first block that is not air should stop on the plant ({plant.Code}) above the ground at {ground}, and found {naive}.");

            accessor.SetBlock(graniteId, high);
            AssertHeight("a solid block higher up", ground + 6);
            accessor.SetBlock(0, high);

            Clear(above);
            accessor.SetBlock(waterId, above);
            AssertHeight("water on the surface", ground + 1);

            accessor.SetBlock(plant.Id, above, BlockLayersAccess.Solid);
            AssertHeight($"a plant ({plant.Code}) standing in that water", ground + 1);
        }
        finally
        {
            Clear(above);
            Clear(high);
        }

        // The engine takes the blocks out of its height map as it took them in.
        await World.Ticks(1);
        Assert.Equal(ground, EngineHeight());
    }

    /// <summary>
    /// ScannedTile is what the layer builds for each tile of a dimension. Run it over the real
    /// blocks of the fixture's roofed dimension, read through SurfaceRule on the server's block
    /// accessor, for the chunk at the dimension's fixed spawn. A bare floor scans to its top on
    /// every column and casts no relief. Blocks stood on it are lit and shade the pixels south and
    /// east of them, those just outside the tile's west and north edges included: the tile reads
    /// that row and column from the neighbouring chunks.
    /// </summary>
    [AtlasScenario]
    public async Task ScannedTile_Should_ShadeBlocksOnTheFloor_When_ScanningARoofedDimension()
    {
        int dimId = await DimensionId("cavern");
        IBlockAccessor accessor = World.Api.World.BlockAccessor;
        Block air = World.Api.World.GetBlock(0)!;
        int graniteId = World.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;

        // The tile reads one row and one column beyond its north-west corner: wait for the chunks
        // that hold them as well as for the tile's own.
        bool FloorAt(int wx, int wz) => World.BlockAt(new BlockPos(wx, CavernFloorY, wz, dimId)).Id != 0;
        await World.Until(
            () => FloorAt(CavernTileX - 1, CavernTileZ - 1) && FloorAt(CavernTileX - 1, CavernTileZ)
                && FloorAt(CavernTileX, CavernTileZ - 1) && FloorAt(CavernTileX, CavernTileZ),
            timeoutTicks: 1200);

        ScannedTile BuildTile() => new(
            ChunkSize,
            CavernTileX,
            CavernTileZ,
            CavernDeclaredScanTop,
            skipCeiling: true,
            (wx, wy, wz) => SurfaceRule.BlockAt(accessor, new BlockPos(wx, wy, wz, dimId), air).Id);

        ScannedTile bare = BuildTile();
        foreach (var (lx, lz) in Pixels())
        {
            Assert.Equal(CavernFloorY, bare.Scan(lx, lz));
            Assert.Equal(1f, bare.Relief(lx, lz, CavernFloorY));
        }

        // Three blocks on the floor, placed by their pixel in the tile: one inside it, and one in
        // each of the neighbouring chunks, just outside its west and its north edge.
        (int X, int Z) inside = (16, 16);
        (int X, int Z) west = (-1, 8);
        (int X, int Z) north = (8, -1);
        BlockPos[] placed = new[] { inside, west, north }
            .Select(p => new BlockPos(CavernTileX + p.X, CavernFloorY + 1, CavernTileZ + p.Z, dimId))
            .ToArray();
        try
        {
            foreach (BlockPos pos in placed)
            {
                accessor.SetBlock(graniteId, pos);
            }

            ScannedTile tile = BuildTile();
            float ReliefAt(int lx, int lz) => tile.Relief(lx, lz, tile.Scan(lx, lz));

            // The block inside stands one above the floor and is lit. The pixels south and east of
            // it, which have it as their north and west neighbour, are shaded.
            Assert.Equal(CavernFloorY + 1, tile.Scan(inside.X, inside.Z));
            Assert.Equal(Hillshade.Factor(1, 1, 1), ReliefAt(inside.X, inside.Z));
            Assert.True(ReliefAt(inside.X, inside.Z) > 1f);
            Assert.Equal(Hillshade.Factor(0, 0, -1), ReliefAt(inside.X, inside.Z + 1));
            Assert.True(ReliefAt(inside.X, inside.Z + 1) < 1f);
            Assert.Equal(Hillshade.Factor(0, -1, 0), ReliefAt(inside.X + 1, inside.Z));
            Assert.True(ReliefAt(inside.X + 1, inside.Z) < 1f);

            // The blocks outside the tile shade its first column and its first row.
            Assert.Equal(Hillshade.Factor(0, -1, 0), ReliefAt(west.X + 1, west.Z));
            Assert.Equal(Hillshade.Factor(0, 0, -1), ReliefAt(north.X, north.Z + 1));

            // Nothing else is shaded: the block inside touches four pixels, itself and the ones east,
            // south and south-east of it, and each of the other two touches the two in the tile.
            Assert.Equal(8, Pixels().Count(p => ReliefAt(p.Lx, p.Lz) != 1f));
        }
        finally
        {
            foreach (BlockPos pos in placed)
            {
                accessor.SetBlock(0, pos);
            }
        }

        Assert.All(placed, pos => Assert.Equal(0, World.BlockAt(pos).Id));
    }

    // Every local (lx, lz) of a tile, row by row.
    private static IEnumerable<(int Lx, int Lz)> Pixels() =>
        from lz in Enumerable.Range(0, ChunkSize)
        from lx in Enumerable.Range(0, ChunkSize)
        select (lx, lz);
}
