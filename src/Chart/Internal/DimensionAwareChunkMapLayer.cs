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
    /// Samples one chunk column and pushes the result into the tile store and the GPU
    /// component. Orchestrates the vanilla ChunkMapLayer pipeline by delegating each phase
    /// (slice prefetch, per-pixel sampling, snow peek-down, hillshade, colour assignment,
    /// shadow blur, tile upload) to a named helper. Must run on the main thread (GPU upload).
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

        if (!TryPrefetchSlices(cx, cz, currentDim, numChunkSlices, out var chunkSlices))
        {
            return;
        }

        // Neighbour map chunks for the cross-chunk slope calculation.
        var mcNW = _capi.World.BlockAccessor.GetMapChunk(cx - 1, cz - 1);
        var mcW = _capi.World.BlockAccessor.GetMapChunk(cx - 1, cz);
        var mcN = _capi.World.BlockAccessor.GetMapChunk(cx, cz - 1);

        int pixCount = cs * cs;
        var tintedImage = new int[pixCount];
        var shadowMap = new byte[pixCount];
        Array.Fill(shadowMap, (byte)128);

        int playerY = (int)(_capi.World.Player?.Entity?.Pos.Y ?? 128.0);
        int scanTop = Math.Min(mapSizeY - 1, playerY + 64);

        for (int i = 0; i < pixCount; i++)
        {
            int lx = i % cs;
            int lz = i / cs;

            if (!TrySamplePixel(mc, chunkSlices, numChunkSlices, scanTop, cs, cx, cz, lx, lz, out int y, out var block, out bool usedFallback))
            {
                continue;
            }

            PeekDownThroughSnow(chunkSlices, numChunkSlices, cs, lx, lz, ref y, ref block);

            // Skip hillshade when the fallback was used: neighbour mc.RainHeightMap is
            // stale (overworld values in custom dims), so the slope delta would be garbage.
            float b = usedFallback ? 1f : ComputeShadowFactor(mc, mcNW, mcN, mcW, cs, lx, lz, y);

            ApplyPixelColor(i, block, b, cs, cx, cz, lx, lz, y, chunkSlices, numChunkSlices, tintedImage, shadowMap);
        }

        ApplyShadowMap(shadowMap, tintedImage, cs, pixCount);

        var tile = IntArrayToByteTile(tintedImage);
        store.SetTile(cx, cz, tile);
        UploadTileToComponent(cx, cz, tintedImage);

        ReEnqueueCardinalNeighbours(cx, cz);
    }

    /// <summary>
    /// Prefetches all vertical chunk slices for column (cx, cz) in the player's current
    /// dimension. Returns false if a low slice (lower half of the world) is missing or not
    /// yet loaded from the server - the column is deferred to the next tick. Upper slices may
    /// legitimately be absent above build height; they are stored as null and guarded by the
    /// per-pixel sampler.
    /// </summary>
    private bool TryPrefetchSlices(int cx, int cz, int currentDim, int numChunkSlices, out IWorldChunk[] slices)
    {
        // VS encodes the dimension into chunk Y as `cy + dim * 1024`, so we must offset here
        // or we read the overworld's slice even when the player is in a custom dim.
        slices = new IWorldChunk[numChunkSlices];
        int dimChunkYOffset = currentDim * 1024;
        for (int cy = 0; cy < numChunkSlices; cy++)
        {
            var slice = _capi!.World.BlockAccessor.GetChunk(cx, cy + dimChunkYOffset, cz);
            bool loaded = slice is IClientChunk clientSlice && clientSlice.LoadedFromServer;
            if (loaded)
            {
                slices[cy] = slice!;
                continue;
            }

            if (cy < numChunkSlices / 2)
            {
                // Low portion of the world is not loaded yet - defer this column.
                return false;
            }

            // Upper slice may not exist above build height; leave null and guard below.
            slices[cy] = null!;
        }

        return true;
    }

    /// <summary>
    /// Resolves the surface block at local (lx, lz). Reads <c>RainHeightMap</c> first; if
    /// that height is air (the heightmap is stale, common in Manifold custom dims where
    /// vanilla worldgen does not populate it), falls back to a top-down scan from
    /// <paramref name="scanTop"/> down to y=1. Returns false when no surface could be found
    /// (the pixel is left transparent).
    /// </summary>
    private bool TrySamplePixel(
        IMapChunk mc,
        IWorldChunk[] slices,
        int numSlices,
        int scanTop,
        int cs,
        int cx,
        int cz,
        int lx,
        int lz,
        out int y,
        out Block block,
        out bool usedFallback)
    {
        int i = (lz * cs) + lx;
        y = mc.RainHeightMap[i];
        int cy = y / cs;
        usedFallback = false;
        block = null!;

        if (cy >= numSlices)
        {
            return false;
        }

        int blockId;
        var currentSlice = slices[cy];
        if (currentSlice != null)
        {
            blockId = currentSlice.UnpackAndReadBlock(
                MapUtil.Index3d(lx, y % cs, lz, cs, cs),
                BlockLayersAccess.FluidOrSolid);
            block = _capi!.World.Blocks[blockId];
        }
        else
        {
            blockId = 0;
            block = _capi!.World.Blocks[0];
        }

        if (blockId != 0 && block != null && block.Id != 0)
        {
            return true;
        }

        // Fallback scan: vanilla worldgen populates RainHeightMap; Manifold custom dims do
        // not, so we scan downward from (player.Y + 64) until we hit a non-air block.
        usedFallback = true;
        _samplePos!.Set((cx * cs) + lx, 0, (cz * cs) + lz);
        for (int yy = scanTop; yy > 0; yy--)
        {
            _samplePos.Set((cx * cs) + lx, yy, (cz * cs) + lz);
            var fb = _capi.World.BlockAccessor.GetBlock(_samplePos);
            if (fb != null && fb.Id != 0)
            {
                y = yy;
                block = fb;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// If the resolved surface block is snow, peeks one block lower to use the underlying
    /// terrain for colour and shading (vanilla default-mode behaviour - hide the snow layer
    /// on the map).
    /// </summary>
    private void PeekDownThroughSnow(
        IWorldChunk[] slices, int numSlices, int cs, int lx, int lz, ref int y, ref Block block)
    {
        if (block.BlockMaterial != EnumBlockMaterial.Snow || y <= 0)
        {
            return;
        }

        int yBelow = y - 1;
        int cyBelow = yBelow / cs;
        if (cyBelow >= numSlices || slices[cyBelow] == null)
        {
            return;
        }

        int belowId = slices[cyBelow].UnpackAndReadBlock(
            MapUtil.Index3d(lx, yBelow % cs, lz, cs, cs),
            BlockLayersAccess.FluidOrSolid);
        if (belowId == 0)
        {
            return;
        }

        block = _capi!.World.Blocks[belowId];
        y = yBelow;
    }

    /// <summary>
    /// Computes the per-pixel slope/shadow brightness factor using the vanilla 3-neighbour
    /// (NW, N, W) pattern. Returns 1 for flat pixels, slightly above 1 for upslope and
    /// below 1 for downslope. Cap and divisor are softened from vanilla (0.3 cap / 12 div /
    /// 1.25 dampening) because our tiles are 32x32, not 96x96, so seams show more.
    /// </summary>
    private static float ComputeShadowFactor(
        IMapChunk mc, IMapChunk? mcNW, IMapChunk? mcN, IMapChunk? mcW,
        int cs, int lx, int lz, int y)
    {
        int topX = lx - 1;
        int leftZ = lz - 1;
        int rightZ = lz;
        int botX = lx;

        IMapChunk? leftTopMc = mc;
        IMapChunk? rightTopMc = mc;
        IMapChunk? leftBotMc = mc;

        if (topX < 0 && leftZ < 0)
        {
            leftTopMc = mcNW;
            rightTopMc = mcW;
            leftBotMc = mcN;
        }
        else
        {
            if (topX < 0)
            {
                leftTopMc = mcW;
                rightTopMc = mcW;
            }

            if (leftZ < 0)
            {
                leftTopMc = mcN;
                leftBotMc = mcN;
            }
        }

        int topXMod = GameMath.Mod(topX, cs);
        int leftZMod = GameMath.Mod(leftZ, cs);

        int leftTop = leftTopMc == null ? 0 : (y - leftTopMc.RainHeightMap[(leftZMod * cs) + topXMod]);
        int rightTop = rightTopMc == null ? 0 : (y - rightTopMc.RainHeightMap[(rightZ * cs) + topXMod]);
        int leftBot = leftBotMc == null ? 0 : (y - leftBotMc.RainHeightMap[(leftZMod * cs) + botX]);

        float slopedir = Math.Sign(leftTop) + Math.Sign(rightTop) + Math.Sign(leftBot);
        float steepness = Math.Max(Math.Max(Math.Abs(leftTop), Math.Abs(rightTop)), Math.Abs(leftBot));
        float magnitude = Math.Min(0.3f, steepness / 12f) / 1.25f;

        if (slopedir > 0f)
        {
            return 1.08f + magnitude;
        }

        if (slopedir < 0f)
        {
            return 0.92f - magnitude;
        }

        return 1f;
    }

    /// <summary>
    /// Assigns the final colour for one pixel (palette lookup, water-edge detection, and
    /// shadow-map accumulation). Non-lake pixels write a brightness factor into the shadow
    /// map for the post-pass blur+combine; lake pixels skip the shadow factor and are
    /// coloured as either interior lake or shoreline.
    /// </summary>
    private void ApplyPixelColor(
        int i, Block block, float b, int cs, int cx, int cz, int lx, int lz, int y,
        IWorldChunk[] slices, int numSlices,
        int[] tintedImage, byte[] shadowMap)
    {
        if (VanillaMapPalette.IsLake(block))
        {
            // Water/ice edge: if any cardinal neighbour is non-lake, render as wateredge.
            // Shadow factor is NOT applied to water pixels.
            var ctx = new PixelContext(cx, cz, lx, lz, y, cs, slices, numSlices);
            bool allLake = IsNeighbourLake(in ctx);
            tintedImage[i] = allLake ? _palette.GetColor(block.Id) : _palette.WaterEdgeColor;
            return;
        }

        shadowMap[i] = (byte)Math.Max(0, Math.Min(255, shadowMap[i] * b));
        tintedImage[i] = _palette.GetColor(block.Id);
    }

    /// <summary>
    /// Box-blurs the shadow map (r=1) then combines blurred + raw shadow with the vanilla
    /// quantisation formula and applies it to each pixel of <paramref name="tintedImage"/>.
    /// </summary>
    private static void ApplyShadowMap(byte[] shadowMap, int[] tintedImage, int cs, int pixCount)
    {
        // Keep a copy of the raw pre-blur shadow values.
        var rawShadow = new byte[shadowMap.Length];
        Array.Copy(shadowMap, rawShadow, shadowMap.Length);

        // Vanilla uses radius 2 but operates on 3x3-grouped 96x96 tiles where the blur stays
        // inside one big tile; our tiles are 32x32 so a larger blur creates visible seams.
        BlurTool.Blur(shadowMap, cs, cs, 1);

        for (int i = 0; i < pixCount; i++)
        {
            // Blurred component (quantised brightness step).
            float bVal = (int)(((shadowMap[i] / 128f) - 1f) * 5) / 5f;

            // Raw component (fractional sharp-edge detail added back).
            bVal += (((rawShadow[i] / 128f) - 1f) * 5 % 1) / 5f;

            // Apply to pixel colour and force alpha = 255.
            tintedImage[i] = ColorUtil.ColorMultiply3Clamped(tintedImage[i], bVal + 1f) | (255 << 24);
        }
    }

    /// <summary>
    /// Re-enqueues the 4 cardinal neighbours so their edges resample using our now-loaded
    /// <c>RainHeightMap</c>. Reduces visible seams at chunk boundaries when chunks load in a
    /// staggered order. Only neighbours already rendered (present in <c>_components</c>) are
    /// enqueued, to avoid expanding the working set unbounded.
    /// </summary>
    private void ReEnqueueCardinalNeighbours(int cx, int cz)
    {
        if (_components.ContainsKey((cx - 1, cz)))
        {
            _dirtyQueue.Enqueue((cx - 1, cz));
        }

        if (_components.ContainsKey((cx + 1, cz)))
        {
            _dirtyQueue.Enqueue((cx + 1, cz));
        }

        if (_components.ContainsKey((cx, cz - 1)))
        {
            _dirtyQueue.Enqueue((cx, cz - 1));
        }

        if (_components.ContainsKey((cx, cz + 1)))
        {
            _dirtyQueue.Enqueue((cx, cz + 1));
        }
    }

    /// <summary>
    /// Per-pixel sampling state used by the neighbour-lookup helpers, collected so the
    /// helpers keep a small parameter list.
    /// </summary>
    private readonly record struct PixelContext(
        int Cx, int Cz, int Lx, int Lz, int Y, int Cs,
        IWorldChunk[] Slices, int NumSlices);

    /// <summary>
    /// Checks whether all 4 cardinal neighbours (N, S, E, W) at the same Y are lake blocks.
    /// Returns false if any neighbour is non-lake, which signals that this pixel should be
    /// rendered as a shoreline ("wateredge"). Cross-chunk neighbours whose map chunk is not
    /// loaded yet are skipped (they do not force an edge).
    /// </summary>
    private bool IsNeighbourLake(in PixelContext ctx)
    {
        int cy = ctx.Y / ctx.Cs;
        int yLocal = ctx.Y % ctx.Cs;
        if (cy >= ctx.NumSlices || ctx.Slices[cy] == null)
        {
            return true; // can't tell; treat as interior water
        }

        var currentSlice = ctx.Slices[cy];
        Span<(int Dx, int Dz)> offsets = stackalloc (int, int)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
        foreach (var (dx, dz) in offsets)
        {
            bool? lake = SampleNeighbourIsLake(in ctx, currentSlice, yLocal, dx, dz);
            if (lake == false)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tristate lake check for the neighbour at offset (<paramref name="dx"/>,
    /// <paramref name="dz"/>) from the pixel in <paramref name="ctx"/>:
    /// <c>true</c> = lake, <c>false</c> = not lake, <c>null</c> = unknown (the neighbour's
    /// map chunk is not loaded; the caller should not treat this as an edge).
    /// </summary>
    private bool? SampleNeighbourIsLake(in PixelContext ctx, IWorldChunk currentSlice, int yLocal, int dx, int dz)
    {
        int nx = ctx.Lx + dx;
        int nz = ctx.Lz + dz;

        Block? nb;
        if (nx >= 0 && nx < ctx.Cs && nz >= 0 && nz < ctx.Cs)
        {
            int nId = currentSlice.UnpackAndReadBlock(
                MapUtil.Index3d(nx, yLocal, nz, ctx.Cs, ctx.Cs),
                BlockLayersAccess.FluidOrSolid);
            nb = _capi!.World.Blocks[nId];
        }
        else
        {
            nb = SampleCrossChunkNeighbour(in ctx, nx, nz);
            if (nb == null)
            {
                return null; // neighbour map chunk not loaded - skip
            }
        }

        return nb != null && VanillaMapPalette.IsLake(nb);
    }

    /// <summary>
    /// Resolves the block at neighbour-chunk-local (<paramref name="nx"/>, <paramref name="nz"/>),
    /// where one or both coordinates lie outside the current chunk's range. Returns null when
    /// the neighbour map chunk is not yet loaded (caller treats as a skip).
    /// </summary>
    private Block? SampleCrossChunkNeighbour(in PixelContext ctx, int nx, int nz)
    {
        int ncx = ctx.Cx + CardinalSign(nx, ctx.Cs);
        int ncz = ctx.Cz + CardinalSign(nz, ctx.Cs);
        var nmc = _capi!.World.BlockAccessor.GetMapChunk(ncx, ncz);
        if (nmc == null)
        {
            return null;
        }

        int nnx = ((nx % ctx.Cs) + ctx.Cs) % ctx.Cs;
        int nnz = ((nz % ctx.Cs) + ctx.Cs) % ctx.Cs;
        _samplePos!.Set((ncx * ctx.Cs) + nnx, ctx.Y, (ncz * ctx.Cs) + nnz);
        return _capi.World.BlockAccessor.GetBlock(_samplePos);
    }

    /// <summary>
    /// Returns -1 when <paramref name="v"/> is below 0, +1 when it is at or past
    /// <paramref name="cs"/>, and 0 otherwise. Used to step into the correct neighbour chunk.
    /// </summary>
    private static int CardinalSign(int v, int cs) => v < 0 ? -1 : (v >= cs ? 1 : 0);

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
            tile[b + 0] = (byte)(p & 0xFF); // B
            tile[b + 1] = (byte)((p >> 8) & 0xFF); // G
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
