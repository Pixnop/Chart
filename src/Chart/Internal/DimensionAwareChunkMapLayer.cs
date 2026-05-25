using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Chart.Internal;

/// <summary>
/// Map layer that samples chunks into RGBA tiles using the vanilla
/// <c>ChunkMapLayer</c> palette + hillshade pipeline, stores them in the active
/// dimension's <see cref="MapTileStore"/>, and renders them on the world map via
/// <see cref="MultiChunkMapComponent"/>.
///
/// Vanilla rendering pipeline (default / non-colorAccurate path):
/// 1. Fixed 13-colour material palette (see <see cref="VanillaMapPalette"/>).
/// 2. Surface height from <c>IMapChunk.RainHeightMap</c>; fallback scan for custom dims.
/// 3. Block lookup via <c>IWorldChunk.UnpackAndReadBlock(FluidOrSolid)</c>.
/// 4. Snow skip: if the top block is snow, sample Y-1 for the real terrain.
/// 5. Water/ice edge: if a lake pixel has any non-lake cardinal neighbour, paint it as "wateredge".
/// 6. Shadow map: per-pixel brightness from 3-neighbour (NW, N, W) slope, box-blurred (r=2),
///    then combined blurred+raw formula (see vanilla GenerateChunkImage section 6).
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

    // Palette built once in OnLoaded.
    private readonly VanillaMapPalette _palette = new();

    // Reused BlockPos to avoid per-column allocation during sampling.
    private BlockPos? _samplePos;

    // Chunk size (typically 32). Cached once in OnLoaded.
    private int _chunkSize;

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
    public override MapLegendItem[] LegendItems => Array.Empty<MapLegendItem>();

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

        _chunkSize = GlobalConstants.ChunkSize;

        // Dimension 0 is the overworld; the actual dimension is set each ProcessChunk call.
        _samplePos = new BlockPos(0);

        // Build the vanilla material palette from the full block registry.
        _palette.Build(_capi.World.Blocks);

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
    /// Uses the vanilla ChunkMapLayer default rendering pipeline.
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

        // BlockAccessor.GetBlock(BlockPos) reads from the BlockPos's `dimension` field.
        // _samplePos was constructed with dim=0 - set the correct dim explicitly each call.
        int currentDim = _capi.World.Player?.Entity?.Pos.Dimension ?? 0;
        _samplePos!.dimension = currentDim;

        int cs = _chunkSize;
        int mapSizeY = _capi.World.BlockAccessor.MapSizeY;
        int numChunkSlices = mapSizeY / cs;

        // Prefetch all vertical chunk slices (vanilla pattern). VS encodes the dimension
        // into chunk Y as `cy + dim * 1024`, so we must offset here or we read the
        // overworld's slice even when the player is in a custom dim.
        var chunkSlices = new IWorldChunk[numChunkSlices];
        int dimChunkYOffset = currentDim * 1024;
        for (int cy = 0; cy < numChunkSlices; cy++)
        {
            var slice = _capi.World.BlockAccessor.GetChunk(cx, cy + dimChunkYOffset, cz);
            if (slice == null || !(slice is IClientChunk clientSlice && clientSlice.LoadedFromServer))
            {
                // Some slices can legitimately be null for very tall worlds above build height;
                // only bail if a low slice is missing. Use a conservative threshold.
                if (cy < numChunkSlices / 2)
                {
                    // Low portion of the world is not loaded yet - defer.
                    return;
                }

                // Upper slices may simply not exist above the build height.
                // Leave them null; we guard with (cy < chunkSlices.Length) checks below.
                chunkSlices[cy] = null!;
                continue;
            }

            chunkSlices[cy] = slice;
        }

        // Fetch neighbour map chunks for cross-chunk slope calculation (vanilla pattern).
        var mcNW = _capi.World.BlockAccessor.GetMapChunk(cx - 1, cz - 1);
        var mcW  = _capi.World.BlockAccessor.GetMapChunk(cx - 1, cz);
        var mcN  = _capi.World.BlockAccessor.GetMapChunk(cx,     cz - 1);

        int pixCount = cs * cs;
        var tintedImage = new int[pixCount];

        // Shadow map (1 byte per pixel, 128 = neutral brightness).
        var shadowMap = new byte[pixCount];
        for (int i = 0; i < shadowMap.Length; i++)
        {
            shadowMap[i] = 128;
        }

        int playerY = (int)(_capi.World.Player?.Entity?.Pos.Y ?? 128.0);
        int scanTop = Math.Min(mapSizeY - 1, playerY + 64);

        for (int i = 0; i < pixCount; i++)
        {
            // Local (lx, lz) within this chunk (0..31).
            int lx = i % cs;
            int lz = i / cs;

            // --- Surface height ---
            // Primary: RainHeightMap (vanilla uses this directly).
            int y = mc.RainHeightMap[i];
            int cy = y / cs;

            // Guard: if cy is out of range for the prefetched slices, skip the pixel.
            if (cy >= numChunkSlices)
            {
                continue;
            }

            // --- Block lookup first (sets usedFallback if RainHeightMap was stale) ---
            var currentSlice = chunkSlices[cy];
            int blockId;
            Block block;
            bool usedFallback = false;

            if (currentSlice != null)
            {
                blockId = currentSlice.UnpackAndReadBlock(
                    MapUtil.Index3d(lx, y % cs, lz, cs, cs),
                    BlockLayersAccess.FluidOrSolid);
                block = _capi.World.Blocks[blockId];
            }
            else
            {
                blockId = 0;
                block = _capi.World.Blocks[0];
            }

            // --- Fallback scan for custom Manifold dims (RainHeightMap may be 0 there) ---
            // Vanilla worldgen populates RainHeightMap; Manifold custom dims do not.
            // Without this fallback, custom dims render empty.
            if (blockId == 0 || block == null || block.Id == 0)
            {
                usedFallback = true;
                _samplePos!.Set((cx * cs) + lx, 0, (cz * cs) + lz);
                for (int yy = scanTop; yy > 0; yy--)
                {
                    _samplePos.Set((cx * cs) + lx, yy, (cz * cs) + lz);
                    var fb = _capi.World.BlockAccessor.GetBlock(_samplePos);
                    if (fb != null && fb.Id != 0)
                    {
                        y = yy;
                        cy = y / cs;
                        blockId = fb.Id;
                        block = fb;
                        break;
                    }
                }

                // If still nothing found, leave pixel transparent and continue.
                if (block == null || block.Id == 0)
                {
                    continue;
                }
            }

            // --- Snow skip (vanilla default mode) ---
            // If the surface block is snow, peek one block lower to get the actual terrain.
            if (block.BlockMaterial == EnumBlockMaterial.Snow && y > 0)
            {
                int yBelow = y - 1;
                int cyBelow = yBelow / cs;
                if (cyBelow < numChunkSlices && chunkSlices[cyBelow] != null)
                {
                    int belowId = chunkSlices[cyBelow].UnpackAndReadBlock(
                        MapUtil.Index3d(lx, yBelow % cs, lz, cs, cs),
                        BlockLayersAccess.FluidOrSolid);
                    if (belowId != 0)
                    {
                        block = _capi.World.Blocks[belowId];
                        y = yBelow;
                    }
                }
            }

            // --- Slope / shadow factor (vanilla 3-neighbour NW, N, W) ---
            // Skip when the fallback was used: neighbour mc.RainHeightMap is stale
            // (overworld values in custom dims), so the slope delta would be garbage.
            float b = 1f;
            if (!usedFallback)
            {
                int topX = lx - 1;
                int leftZ = lz - 1;
                int botX = lx;
                int rightZ = lz;

                IMapChunk leftTopMc = mc;
                IMapChunk rightTopMc = mc;
                IMapChunk leftBotMc = mc;

                if (topX < 0 && leftZ < 0)
                {
                    leftTopMc = mcNW!;
                    rightTopMc = mcW!;
                    leftBotMc = mcN!;
                }
                else
                {
                    if (topX < 0) { leftTopMc = mcW!; rightTopMc = mcW!; }
                    if (leftZ < 0) { leftTopMc = mcN!; leftBotMc = mcN!; }
                }

                int topXMod = GameMath.Mod(topX, cs);
                int leftZMod = GameMath.Mod(leftZ, cs);

                int leftTop = leftTopMc == null ? 0 : (y - leftTopMc.RainHeightMap[(leftZMod * cs) + topXMod]);
                int rightTop = rightTopMc == null ? 0 : (y - rightTopMc.RainHeightMap[(rightZ * cs) + topXMod]);
                int leftBot = leftBotMc == null ? 0 : (y - leftBotMc.RainHeightMap[(leftZMod * cs) + botX]);

                float slopedir = Math.Sign(leftTop) + Math.Sign(rightTop) + Math.Sign(leftBot);
                float steepness = Math.Max(Math.Max(Math.Abs(leftTop), Math.Abs(rightTop)), Math.Abs(leftBot));

                // Softened from vanilla (cap 0.5 / div 10) because our tiles are 32x32 and
                // not 96x96 like vanilla, so cross-chunk discrepancies show more.
                float magnitude = Math.Min(0.3f, steepness / 12f) / 1.25f;
                if (slopedir > 0f) b = 1.08f + magnitude;
                if (slopedir < 0f) b = 0.92f - magnitude;
            }

            // --- Colour assignment ---
            if (VanillaMapPalette.IsLake(block))
            {
                // Water/ice edge detection: if any cardinal neighbour (N, S, E, W) is
                // non-lake, render this pixel as "wateredge" (dark shoreline).
                // Shadow factor is NOT applied to water pixels.
                bool allLake = IsNeighbourLake(cx, cz, lx, lz, y, cs, chunkSlices, numChunkSlices);
                tintedImage[i] = allLake ? _palette.GetColor(block.Id) : _palette.WaterEdgeColor;
            }
            else
            {
                // Write brightness factor into shadow map.
                shadowMap[i] = (byte)Math.Max(0, Math.Min(255, shadowMap[i] * b));
                tintedImage[i] = _palette.GetColor(block.Id);
            }
        }

        // --- Shadow map blur + apply (vanilla formula) ---
        // Keep a copy of the raw pre-blur shadow values.
        var rawShadow = new byte[shadowMap.Length];
        Array.Copy(shadowMap, rawShadow, shadowMap.Length);

        // Box-blur the shadow map with radius 1. Vanilla uses radius 2 but operates on
        // 3x3-grouped 96x96 tiles where the blur stays inside one big tile; our tiles
        // are 32x32 so a larger blur creates visible seams at chunk boundaries.
        BlurTool.Blur(shadowMap, cs, cs, 1);

        // Combine blurred and raw shadows and apply to each pixel colour.
        for (int i = 0; i < pixCount; i++)
        {
            // Blurred component (quantised brightness step).
            float bVal = (int)((shadowMap[i] / 128f - 1f) * 5) / 5f;
            // Raw component (fractional sharp-edge detail added back).
            bVal += ((rawShadow[i] / 128f - 1f) * 5 % 1) / 5f;

            // Apply to pixel colour and force alpha = 255.
            tintedImage[i] = ColorUtil.ColorMultiply3Clamped(tintedImage[i], bVal + 1f) | (255 << 24);
        }

        // Convert int[] BGRA to byte[] BGRA tile (4 bytes per pixel).
        var tile = IntArrayToByteTile(tintedImage);
        store.SetTile(cx, cz, tile);
        UploadTileToComponent(cx, cz, tintedImage);

        // Re-enqueue the 4 cardinal neighbours so their edges resample using our
        // now-loaded mc.RainHeightMap. Reduces visible seams at chunk boundaries
        // when chunks load in a staggered order. Only re-enqueue chunks already
        // rendered (in _components) to avoid expanding the working set unbounded.
        if (_components.ContainsKey((cx - 1, cz))) _dirtyQueue.Enqueue((cx - 1, cz));
        if (_components.ContainsKey((cx + 1, cz))) _dirtyQueue.Enqueue((cx + 1, cz));
        if (_components.ContainsKey((cx, cz - 1))) _dirtyQueue.Enqueue((cx, cz - 1));
        if (_components.ContainsKey((cx, cz + 1))) _dirtyQueue.Enqueue((cx, cz + 1));
    }

    /// <summary>
    /// Checks whether all 4 cardinal neighbours (N, S, E, W) at the same Y are lake blocks.
    /// Returns false if any neighbour is non-lake, which signals that this pixel should be
    /// rendered as a shoreline ("wateredge").
    /// </summary>
    private bool IsNeighbourLake(
        int cx, int cz,
        int lx, int lz,
        int y,
        int cs,
        IWorldChunk[] slices,
        int numSlices)
    {
        int cy = y / cs;
        int yLocal = y % cs;
        if (cy >= numSlices || slices[cy] == null)
        {
            return true; // can't tell; treat as interior water
        }

        // The 4 cardinal offsets: (dx, dz) pairs.
        Span<(int dx, int dz)> offsets = stackalloc (int, int)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
        foreach (var (dx, dz) in offsets)
        {
            int nx = lx + dx;
            int nz = lz + dz;

            Block? nb;
            if (nx >= 0 && nx < cs && nz >= 0 && nz < cs)
            {
                // Same chunk.
                int nId = slices[cy].UnpackAndReadBlock(
                    MapUtil.Index3d(nx, yLocal, nz, cs, cs),
                    BlockLayersAccess.FluidOrSolid);
                nb = _capi!.World.Blocks[nId];
            }
            else
            {
                // Cross-chunk neighbour.
                int ncx = cx + (nx < 0 ? -1 : (nx >= cs ? 1 : 0));
                int ncz = cz + (nz < 0 ? -1 : (nz >= cs ? 1 : 0));
                int nnx = ((nx % cs) + cs) % cs;
                int nnz = ((nz % cs) + cs) % cs;
                var nmc = _capi!.World.BlockAccessor.GetMapChunk(ncx, ncz);
                if (nmc == null)
                {
                    continue; // Unknown neighbour - skip; don't force edge.
                }

                // For the neighbour chunk we need to look up the correct slice.
                // Reuse the Y we already have (same height as current pixel).
                _samplePos!.Set((ncx * cs) + nnx, y, (ncz * cs) + nnz);
                nb = _capi.World.BlockAccessor.GetBlock(_samplePos);
            }

            if (nb == null || !VanillaMapPalette.IsLake(nb))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts a vanilla int[] BGRA pixel array to the byte[] tile format stored in
    /// <see cref="MapTileStore"/> and used by <see cref="UploadTileToComponent"/>.
    /// The tile format is BGRA, 4 bytes per pixel, row-major.
    /// </summary>
    private static byte[] IntArrayToByteTile(int[] pixels)
    {
        var tile = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            int p = pixels[i];
            int b = i * 4;
            tile[b + 0] = (byte)(p & 0xFF);         // B
            tile[b + 1] = (byte)((p >> 8)  & 0xFF); // G
            tile[b + 2] = (byte)((p >> 16) & 0xFF); // R
            tile[b + 3] = (byte)((p >> 24) & 0xFF); // A
        }

        return tile;
    }

    /// <summary>
    /// Creates or updates the <see cref="MultiChunkMapComponent"/> for (cx, cz) by
    /// uploading the int[] pixel array directly. The int values are in BGRA layout
    /// (matching what vanilla and ColorUtil produce).
    /// </summary>
    private void UploadTileToComponent(int cx, int cz, int[] pixels)
    {
        if (_capi == null)
        {
            return;
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
    /// The stored byte[] tiles are BGRA; converted back to int[] for the component.
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
            var pixels = ByteTileToIntArray(tile);
            UploadTileToComponent(key.Cx, key.Cz, pixels);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Converts a byte[] BGRA tile (4 bytes per pixel) to int[] BGRA pixels.
    /// Inverse of <see cref="IntArrayToByteTile"/>.
    /// </summary>
    private static int[] ByteTileToIntArray(byte[] tile)
    {
        var pixels = new int[tile.Length / 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            int b = i * 4;
            pixels[i] = tile[b] | (tile[b + 1] << 8) | (tile[b + 2] << 16) | (tile[b + 3] << 24);
        }

        return pixels;
    }
}
