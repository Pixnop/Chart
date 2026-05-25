using System.Collections.Concurrent;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Chart.Internal;

/// <summary>
/// Map layer that samples chunks into RGBA tiles using <see cref="ChunkSampler"/>,
/// stores them in the active dimension's <see cref="MapTileStore"/>, and renders them
/// on the world map via <see cref="MultiChunkMapComponent"/>.
///
/// Rendering path: manual MultiChunkMapComponent path. We instantiate
/// MultiChunkMapComponent directly (public API) and call its Render method,
/// bypassing ChunkMapLayer's private loadFromChunkPixels. This gives us full
/// control over which store is active without coupling to ChunkMapLayer internals.
/// </summary>
internal sealed class DimensionAwareChunkMapLayer : RGBMapLayer
{
    // MapLayer.api is ICoreAPI; cache the client cast for all client operations.
    private readonly ICoreClientAPI? _capi;

    // One MultiChunkMapComponent per chunk column rendered on the map.
    // Key: (chunkX, chunkZ) in chunk coordinates.
    // Accessed only on the main thread (Render / OnTick).
    private readonly Dictionary<(int Cx, int Cz), MultiChunkMapComponent> _components = new();

    // Tracks which chunk columns are pending a re-sample. Filled by the ChunkDirty
    // callback (any thread) and drained on the main thread in OnTick.
    private readonly ConcurrentQueue<(int Cx, int Cz)> _dirtyQueue = new();

    // Diagnostic counter: log the first few ProcessChunk calls after each swap so we can
    // verify which dim we are actually sampling (player dim vs tracker's expected dim).
    private int _diagLogCounter;

    // Reused BlockPos to avoid per-column allocation during sampling.
    private BlockPos? _samplePos;

    /// <summary>
    /// VS instantiates map layers via Activator.CreateInstance(typeof(T), api, mapSink).
    /// The constructor must be exactly (ICoreAPI, IWorldMapManager) - no extra parameters.
    /// </summary>
    public DimensionAwareChunkMapLayer(ICoreAPI api, IWorldMapManager mapSink)
        : base(api, mapSink)
    {
        _capi = api as ICoreClientAPI;
    }

    /// <inheritdoc/>
    public override string Title => "Chart";

    /// <inheritdoc/>
    public override string LayerGroupCode => "chart";

    /// <inheritdoc/>
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

    /// <inheritdoc/>
    public override MapLegendItem[] LegendItems => System.Array.Empty<MapLegendItem>();

    /// <inheritdoc/>
    public override EnumMinMagFilter MagFilter => EnumMinMagFilter.Nearest;

    /// <inheritdoc/>
    public override EnumMinMagFilter MinFilter => EnumMinMagFilter.Nearest;

    /// <inheritdoc/>
    public override void OnLoaded()
    {
        base.OnLoaded();
        if (_capi == null)
        {
            return;
        }

        // Dimension 0 is the overworld; the actual dimension is set in Set() calls.
        _samplePos = new BlockPos(0);
        _capi.Event.ChunkDirty += OnChunkDirty;
    }

    /// <inheritdoc/>
    public override void OnMapOpenedClient()
    {
        _capi?.Logger.Notification("[Chart] map opened");
        base.OnMapOpenedClient();

        // Re-upload all tiles from the active store so anything loaded while the map
        // was closed still gets rendered.
        _ = UploadAllStoredTiles();
    }

    /// <inheritdoc/>
    public override void OnMapClosedClient()
    {
        _capi?.Logger.Notification("[Chart] map closed");
        base.OnMapClosedClient();
    }

    /// <inheritdoc/>
    public override void OnViewChangedClient(List<FastVec2i> nowVisible, List<FastVec2i> nowHidden)
    {
        if (_capi == null)
        {
            return;
        }

        // Queue newly visible chunks for (re-)generation.
        foreach (var v in nowVisible)
        {
            _dirtyQueue.Enqueue((v.X, v.Y));
        }
    }

    /// <inheritdoc/>
    public override void OnTick(float dt)
    {
        base.OnTick(dt);
        if (_capi == null)
        {
            return;
        }

        // Drain the dirty queue - each entry is a chunk column to (re-)sample.
        const int maxPerTick = 32;
        int processed = 0;
        while (processed < maxPerTick && _dirtyQueue.TryDequeue(out var coord))
        {
            ProcessChunk(coord.Cx, coord.Cz);
            processed++;
        }
    }

    /// <inheritdoc/>
    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!Active || _capi == null)
        {
            return;
        }

        foreach (var comp in _components.Values)
        {
            comp.Render(mapElem, dt);
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_capi != null)
        {
            _capi.Event.ChunkDirty -= OnChunkDirty;
        }

        foreach (var comp in _components.Values)
        {
            comp.ActuallyDispose();
        }

        _components.Clear();
        base.Dispose();
    }

    /// <summary>
    /// Called by <see cref="ChartModSystem"/> after the active store has been swapped
    /// to a new dimension. Disposes all current GPU components, drains the dirty queue
    /// (its entries belong to the old dimension), and rebuilds from the tiles already
    /// in the newly active store (fast path for revisited dimensions).
    /// </summary>
    public void OnActiveStoreSwapped()
    {
        int before = _components.Count;
        foreach (var comp in _components.Values)
        {
            comp.ActuallyDispose();
        }

        _components.Clear();
        _diagLogCounter = 0;

        // Drain the dirty queue so stale chunk coordinates from the old dimension do
        // not get sampled against the new dimension's block accessor data.
        int drained = 0;
        while (_dirtyQueue.TryDequeue(out _))
        {
            drained++;
        }

        var tiles = UploadAllStoredTiles();
        _capi?.Logger.Notification(
            "[Chart] swapped active store: cleared {0} components, drained {1} dirty entries, repainted {2} tiles.",
            before,
            drained,
            tiles);
    }

    private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        if (reason == EnumChunkDirtyReason.NewlyLoaded ||
            reason == EnumChunkDirtyReason.MarkedDirty)
        {
            // chunkCoord.X/Z are chunk coordinates; Y is the vertical slice index.
            // Multiple vertical slices fire per column - deduplicated at ProcessChunk time.
            _dirtyQueue.Enqueue((chunkCoord.X, chunkCoord.Z));
        }
    }

    /// <summary>
    /// Samples one chunk column and pushes the result into the tile store and the
    /// GPU component. Must run on the main thread (GPU upload).
    /// </summary>
    private void ProcessChunk(int cx, int cz)
    {
        if (_capi == null)
        {
            return;
        }

        var mc = _capi.World.BlockAccessor.GetMapChunk(cx, cz);
        if (mc == null)
        {
            return;
        }

        var store = _capi.ModLoader.GetModSystem<ChartModSystem>()?.ActiveStore;
        if (store == null)
        {
            return;
        }

        // Diagnostic: log dim at sample time for the first chunk after a swap. _diagLogCounter resets on swap.
        if (_diagLogCounter < 3)
        {
            int dimAtSample = -1;
            try { dimAtSample = _capi.World.Player?.Entity?.Pos.Dimension ?? -1; }
            catch { dimAtSample = -2; }
            _capi.Logger.Notification(
                "[Chart] diag ProcessChunk cx={0} cz={1} playerDim={2} center-height={3}",
                cx, cz, dimAtSample, mc.RainHeightMap[16 * 32 + 16]);
            _diagLogCounter++;
        }

        // BlockAccessor.GetBlock(BlockPos) reads from the BlockPos's `dimension` field, NOT
        // from the player's current dimension. _samplePos was constructed with dim=0 and our
        // Set(x,y,z) overload does not touch the dim. We must set it explicitly each call
        // so the sample reads from the dim the player is actually in.
        int currentDim = _capi.World.Player?.Entity?.Pos.Dimension ?? 0;
        _samplePos!.dimension = currentDim;

        // We compute our own per-column surface by scanning downward via BlockAccessor
        // (now dim-aware), because IMapChunk.RainHeightMap is shared across dims.
        var heights = new int[ChunkSampler.TileEdge * ChunkSampler.TileEdge];
        var topBlockIds = new int[ChunkSampler.TileEdge * ChunkSampler.TileEdge];
        int maxY = _capi.World.BlockAccessor.MapSizeY - 1;
        for (int z = 0; z < ChunkSampler.TileEdge; z++)
        {
            for (int x = 0; x < ChunkSampler.TileEdge; x++)
            {
                int i = (z * ChunkSampler.TileEdge) + x;
                int worldX = (cx * ChunkSampler.TileEdge) + x;
                int worldZ = (cz * ChunkSampler.TileEdge) + z;

                int foundY = 0;
                int foundBlockId = 0;
                for (int y = maxY; y > 0; y--)
                {
                    _samplePos!.Set(worldX, y, worldZ);
                    var block = _capi.World.BlockAccessor.GetBlock(_samplePos);
                    if (block != null && block.Id != 0)
                    {
                        foundY = y;
                        foundBlockId = block.Id;
                        break;
                    }
                }

                heights[i] = foundY;
                topBlockIds[i] = foundBlockId;
            }
        }

        // Build palette: block-id -> 0xRRGGBBAA packed uint.
        // System.Func is explicit to avoid ambiguity with Vintagestory.API.Common.Func.
        System.Func<int, uint> palette = blockId =>
        {
            var block = _capi.World.GetBlock(blockId);
            if (block == null)
            {
                return 0u;
            }

            // Block.GetColor returns an ABGR-packed int: (a<<24)|(b<<16)|(g<<8)|r
            // (same layout as ColorUtil.ColorFromRgba - low byte is R, high byte is A).
            // ChunkSampler expects 0xRRGGBBAA, so we must re-pack the channels.
            // _samplePos holds the last sampled column position - close enough for tinting.
            int abgr = block.GetColor(_capi, _samplePos!);
            return ColorConvert.AbgrToPackedRgba(abgr);
        };

        var tile = ChunkSampler.Sample(heights, topBlockIds, palette);
        store.SetTile(cx, cz, tile);
        UploadTileToComponent(cx, cz, tile);
    }

    /// <summary>
    /// Converts a ChunkSampler RGBA byte[] tile to int[] RGBA pixel array and
    /// creates or updates the <see cref="MultiChunkMapComponent"/> for (cx, cz).
    /// </summary>
    private void UploadTileToComponent(int cx, int cz, byte[] tile)
    {
        if (_capi == null)
        {
            return;
        }

        // Convert byte[] RGBA (4 bytes per pixel) to int[] RGBA (1 int per pixel).
        var pixels = new int[ChunkSampler.TileEdge * ChunkSampler.TileEdge];
        for (int i = 0; i < pixels.Length; i++)
        {
            int b = i * 4;
            int r = tile[b];
            int g = tile[b + 1];
            int bl = tile[b + 2];
            int a = tile[b + 3];
            pixels[i] = ColorUtil.ColorFromRgba(r, g, bl, a);
        }

        if (!_components.TryGetValue((cx, cz), out var comp))
        {
            comp = new MultiChunkMapComponent(_capi, new FastVec2i(cx, cz));
            _components[(cx, cz)] = comp;
        }

        // dx=0, dz=0: one component per chunk (not the 3x3 grouping ChunkMapLayer uses).
        comp.setChunk(0, 0, pixels);
        comp.FinishSetChunks();
    }

    /// <summary>
    /// Uploads all tiles from the active store to GPU components.
    /// Must be called on the main thread.
    /// </summary>
    /// <returns>Number of tiles uploaded.</returns>
    private int UploadAllStoredTiles()
    {
        if (_capi == null)
        {
            return 0;
        }

        var store = _capi.ModLoader.GetModSystem<ChartModSystem>()?.ActiveStore;
        if (store == null)
        {
            return 0;
        }

        int count = 0;
        foreach (var (key, tile) in store.AllTiles())
        {
            UploadTileToComponent(key.Cx, key.Cz, tile);
            count++;
        }

        return count;
    }
}
