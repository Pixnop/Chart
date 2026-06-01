using System.Linq;
using Chart.Internal;
using Manifold.Api.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Chart;

/// <summary>Client-only entry point for the Chart companion mod.</summary>
public sealed class ChartModSystem : ModSystem
{
    private PerDimensionMapStore? _store;

    private DimensionTracker? _tracker;

    // Cached reference to the map layer so OnStoreSwapped can call OnActiveStoreSwapped.
    // Null until the WorldMapManager instantiates the layer via Activator.CreateInstance.
    private DimensionAwareChunkMapLayer? _layer;

    /// <summary>
    /// The tile store for the dimension the local player is currently in.
    /// Returns null until the first dimension swap or StartClientSide completes.
    /// The map layer reads this via <c>capi.ModLoader.GetModSystem&lt;ChartModSystem&gt;()?.ActiveStore</c>.
    /// </summary>
    internal MapTileStore? ActiveStore => _store?.Active;

    /// <inheritdoc/>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <inheritdoc/>
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        _store = new PerDimensionMapStore(api);
        _tracker = new DimensionTracker(api, _store);

        // Wire the dim-swap callback: look up the layer from WorldMapManager and notify it.
        _tracker.OnStoreSwapped = () =>
        {
            // Lazily resolve the layer reference after WorldMapManager has instantiated it.
            _layer ??= FindLayer(api);
            _layer?.OnActiveStoreSwapped();
        };

        _tracker.Start();

        // Drop the on-disk tile cache and in-memory tiles when an owning Manifold dimension is
        // destroyed (typically an ephemeral dim). Keeps disk + VRAM bounded over long sessions.
        // If the destroyed dim is the one the player is currently in, also clear the GPU
        // components so the map does not show stale tiles for a dimension that no longer exists.
        var manifoldClient = ManifoldAccess.GetClient(api);
        if (manifoldClient is not null)
        {
            manifoldClient.Destroyed += (_, e) =>
            {
                string dimCode = e.Dimension.Code.ToString();
                bool wasActive = _store.CurrentDimCode == dimCode;
                _store.DeleteFor(dimCode);

                if (wasActive)
                {
                    _layer ??= FindLayer(api);
                    _layer?.OnActiveStoreSwapped();
                }
            };
        }

        var mapManager = api.ModLoader.GetModSystem<WorldMapManager>();

        // Position 0.5 places this layer between the terrain layer (0.0) and marker layers (1.0).
        mapManager.RegisterMapLayer<DimensionAwareChunkMapLayer>("chart", 0.5);
        api.Logger.Notification("[Chart] Registered DimensionAwareChunkMapLayer.");

        // The vanilla MapLayers are instantiated by WorldMapManager AFTER all ModSystems'
        // StartClientSide complete, so the MapLayers collection is empty at this point.
        // Defer the enumeration + vanilla-hide to LevelFinalize, which fires once the world
        // is fully loaded and every MapLayer (ours included) has been instantiated.
        api.Event.LevelFinalize += () =>
        {
            foreach (var layer in mapManager.MapLayers)
            {
                api.Logger.Notification(
                    "[Chart] MapLayer present: {0} code={1}",
                    layer.GetType().FullName,
                    layer.LayerGroupCode);
            }

            var vanilla = mapManager.MapLayers.FirstOrDefault(
                l => l.GetType().Name == "ChunkMapLayer");
            if (vanilla is not null)
            {
                vanilla.Active = false;
                bool removed = mapManager.MapLayers.Remove(vanilla);
                api.Logger.Notification(
                    "[Chart] Vanilla ChunkMapLayer Active=false, Removed={0} (type={1}).",
                    removed,
                    vanilla.GetType().FullName);
            }
            else
            {
                api.Logger.Warning("[Chart] Vanilla ChunkMapLayer not found - may render on top.");
            }

            // Startup orphan scan: drop .bin caches for dimensions that no longer exist in the
            // client mirror. Defends against ephemeral dims whose Destroyed event did not reach
            // the client last session (server crash, ungraceful disconnect, or an older Manifold
            // without the shutdown cleanup). By LevelFinalize the manifest mirror has been
            // populated by the server's ManifestSnapshotPacket, so its dim list is authoritative.
            if (manifoldClient is not null && _store is not null)
            {
                var knownCodes = manifoldClient.Dimensions.Select(d => d.Code.ToString());
                int orphans = _store.DeleteOrphans(knownCodes);
                if (orphans > 0)
                {
                    api.Logger.Notification(
                        "[Chart] Startup orphan scan: deleted {0} stale dim cache file(s).",
                        orphans);
                }
            }
        };
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _tracker?.Stop();
        _store?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Finds the <see cref="DimensionAwareChunkMapLayer"/> instance that WorldMapManager
    /// created via Activator.CreateInstance. Returns null if the map manager or layer
    /// are not yet available.
    /// </summary>
    private static DimensionAwareChunkMapLayer? FindLayer(ICoreClientAPI capi)
    {
        var wmm = capi.ModLoader.GetModSystem<WorldMapManager>();
        if (wmm?.MapLayers == null)
        {
            return null;
        }

        foreach (var layer in wmm.MapLayers)
        {
            if (layer is DimensionAwareChunkMapLayer chartLayer)
            {
                return chartLayer;
            }
        }

        return null;
    }
}
