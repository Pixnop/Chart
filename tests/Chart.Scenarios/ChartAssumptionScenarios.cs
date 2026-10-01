namespace Chart.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Chart.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Pins the engine and Manifold contracts Chart's client renderer is built on.
/// Chart cannot load in this headless server (client-only mod); each scenario
/// here guards one assumption baked into DimensionAwareChunkMapLayer or
/// ChartModSystem, against the published Manifold release and the current game
/// version. A failure means a game or Manifold update invalidated the
/// assumption, not that Chart broke.
/// </summary>
[Trait("Category", "E2E")]
public class ChartAssumptionScenarios : ChartScenarioBase
{
    private const int ChunkSize = 32;

    // Matches the fixture's FixedSpawn (512, 8, 512) and its SlabWorldgen (granite y 1..4).
    private const int ProbeX = 512;
    private const int ProbeZ = 512;
    private const int SlabTopY = 4;

    // The fixture's CavernWorldgen: the slab's floor, plus a roof at y 20..22; the dimension
    // declares its scan top inside that roof.
    private const int CavernRoofTopY = 22;
    private const int CavernDeclaredScanTop = 21;

    /// <summary>
    /// DimensionAwareChunkMapLayer never reads RainHeightMap outside the overworld, because
    /// map chunks are not per dimension: the height it holds is the overworld's surface at the
    /// same X/Z. In an all-air dimension no height can point at a solid block, so if this read
    /// ever returns solid, per-dimension height maps have appeared and Chart could use them
    /// (and bring hillshade back) instead of scanning every column.
    /// </summary>
    [AtlasScenario]
    public async Task RainHeightMap_Should_PointAtAir_When_ReadInVoidDimension()
    {
        int dimId = await DimensionId("void");

        // Probe under the world spawn: its overworld map chunk is guaranteed loaded,
        // unlike the fixture's fixed dim spawn column. Generate the void dim there.
        var spawn = World.Spawn;
        int cx = spawn.X / ChunkSize;
        int cz = spawn.Z / ChunkSize;
        CommandResult pregen = await World.ExecuteCommand($"/chartfx pregen void {spawn.X} {spawn.Z}");
        Assert.True(pregen.Ok, "pregen reported failure.");
        await WaitForDimChunk(dimId, cx, cz);

        var mc = World.Api.World.BlockAccessor.GetMapChunk(cx, cz);
        Assert.NotNull(mc);

        int height = mc!.RainHeightMap[((spawn.Z % ChunkSize) * ChunkSize) + (spawn.X % ChunkSize)];
        var atHeight = new BlockPos(spawn.X, height, spawn.Z, dimId);
        int blockId = World.BlockAt(atHeight).Id;

        Assert.True(
            blockId == 0,
            $"RainHeightMap points at solid block id {blockId} (y={height}) inside an all-air custom dim; the heightmap is now dim-aware and Chart's fallback-scan assumption no longer holds.");
    }

    /// <summary>
    /// Chart reads chunk slices at cy + dim * 1024 (TryPrefetchSlices) and blocks via
    /// UnpackAndReadBlock(Index3d, FluidOrSolid). Pin the same read path server-side:
    /// the slab dim's granite must be reachable through the dim-encoded chunk Y.
    /// </summary>
    [AtlasScenario]
    public async Task DimEncodedChunkSlice_Should_ExposeDimBlocks_When_ReadAtCyPlusDimTimes1024()
    {
        int dimId = await DimensionId("slab");
        var probe = new BlockPos(ProbeX, SlabTopY, ProbeZ, dimId);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        int cy = SlabTopY / ChunkSize;
        var chunk = World.Api.WorldManager.GetChunk(
            ProbeX / ChunkSize,
            (dimId * 1024) + cy,
            ProbeZ / ChunkSize);
        Assert.NotNull(chunk);

        int blockId = chunk!.UnpackAndReadBlock(
            MapUtil.Index3d(ProbeX % ChunkSize, SlabTopY % ChunkSize, ProbeZ % ChunkSize, ChunkSize, ChunkSize),
            BlockLayersAccess.FluidOrSolid);

        int graniteId = World.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;
        Assert.Equal(graniteId, blockId);
    }

    /// <summary>
    /// In a roofed dimension a scan from above stops on the roof, so the owner mod declares a
    /// scan top (the "chart:scanTopY" metadata) and Chart steps through the ceiling it starts
    /// in. Run Chart's own scan over the real blocks of the fixture's cavern dimension: without
    /// the hint it finds the roof, with it the floor.
    /// </summary>
    [AtlasScenario]
    public async Task SurfaceScan_Should_FindTheFloorUnderTheRoof_When_StartedInsideTheCeiling()
    {
        int dimId = await DimensionId("cavern");
        var probe = new BlockPos(ProbeX, CavernRoofTopY, ProbeZ, dimId);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        int IdAt(int y) => World.BlockAt(new BlockPos(ProbeX, y, ProbeZ, dimId)).Id;

        Assert.Equal(CavernRoofTopY, SurfaceScan.Find(IdAt, scanTop: 64, skipCeiling: false));
        Assert.Equal(SlabTopY, SurfaceScan.Find(IdAt, CavernDeclaredScanTop, skipCeiling: true));
    }

    /// <summary>
    /// ChartModSystem purges a dimension's tile cache when Manifold's client mirror
    /// relays the Destroyed event. Pin the server-side registry contract that feeds
    /// that mirror: removing an ephemeral dimension must raise Destroyed.
    /// </summary>
    [AtlasScenario]
    public async Task RegistryDestroyed_Should_Fire_When_EphemeralDimensionIsRemoved()
    {
        CommandResult createResult = await World.ExecuteCommand("/chartfx create-ephemeral tempmap");
        Assert.True(createResult.Ok, "create-ephemeral reported failure.");
        await DimensionId("tempmap");

        CommandResult removeResult = await World.ExecuteCommand("/chartfx remove tempmap");
        Assert.True(removeResult.Ok, "remove reported failure.");
        Assert.Equal("removed", removeResult.Message);

        await World.Until(
            () => FlagIsSet("chartfixture:event:destroyed:tempmap"),
            timeoutTicks: 200);
    }

    /// <summary>
    /// DimensionMapPath sanitizes reserved filesystem characters out of dimension
    /// codes before composing cache file names. Pin that codes really carry a colon
    /// (AssetLocation domain:path), the character that motivates the sanitization.
    /// </summary>
    [AtlasScenario]
    public async Task DimensionCode_Should_ContainColon_When_ComposedByManifold()
    {
        await DimensionId("slab");
        string? code = ReadDimensionCode("slab");

        Assert.Equal("chartfixture:slab", code);
        await World.Ticks(1);
    }

    /// <summary>
    /// Waits until the dim-encoded chunk column exists (same encoding Chart uses for
    /// its slice lookups); void dims have no block to probe for readiness.
    /// </summary>
    private async Task WaitForDimChunk(int dimId, int cx, int cz)
    {
        await World.Until(
            () => World.Api.WorldManager.GetChunk(cx, dimId * 1024, cz) != null,
            timeoutTicks: 1200);
    }
}
