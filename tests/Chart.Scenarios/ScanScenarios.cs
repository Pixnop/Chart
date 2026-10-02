namespace Chart.Scenarios;

using Atlas.XUnit;
using Chart.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Runs Chart's scan over the real blocks of the headless server. The layer that draws the map
/// needs a game client, which Atlas cannot boot, but the rule it scans by is plain code over a
/// block accessor, and the server has one too. The scenarios change blocks and put them back, so
/// they live in a class of their own, which gets a world of its own.
/// </summary>
[Trait("Category", "E2E")]
public class ScanScenarios : ChartScenarioBase
{
    private const int ChunkSize = 32;

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
}
