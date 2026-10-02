using System.Linq;
using Chart.Internal;
using Manifold.Api.Client;
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

    // Cached reference to the waypoint layer so OnStoreSwapped can refilter pins.
    private DimensionAwareWaypointMapLayer? _waypointLayer;

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

        // Wire the dim-swap callback: look up the layers from WorldMapManager and notify them.
        _tracker.OnStoreSwapped = () =>
        {
            // Lazily resolve the layer references after WorldMapManager has instantiated them.
            _layer ??= FindLayer<DimensionAwareChunkMapLayer>(api);
            _layer?.OnActiveStoreSwapped();

            _waypointLayer ??= FindLayer<DimensionAwareWaypointMapLayer>(api);
            _waypointLayer?.OnPlayerDimensionChanged();
        };

        _tracker.Start();

        // Drop the on-disk tile cache and in-memory tiles when an owning Manifold dimension is
        // destroyed (typically an ephemeral dim). Keeps disk + VRAM bounded over long sessions.
        // If the destroyed dim is the one the player is currently in, also clear the GPU
        // components so the map does not show stale tiles for a dimension that no longer exists.
        var manifoldClient = ManifoldAccess.GetClient(api);
        if (manifoldClient is not null)
        {
            manifoldClient.Destroyed += (_, e) => OnDimensionDestroyed(api, e.Dimension.Code.ToString());
        }

        var mapManager = api.ModLoader.GetModSystem<WorldMapManager>();

        // Both vanilla layers are replaced by registering our TYPES under their codes before
        // WorldMapManager instantiates layers at LevelFinalize: re-registering a code overwrites
        // its registry entry, so the vanilla layer is never created on the client.
        //
        // For the terrain layer that is a requirement, not a nicety. The vanilla layer opens the
        // savegame's map database in its constructor, and only the manager's LeaveWorld handler
        // closes it, for the layers still in its list. Created and then taken out of the list,
        // it kept the database open: the next world loaded in the same client session could not
        // open it, no layer was created at all and opening the map crashed.
        mapManager.RegisterMapLayer<DimensionAwareChunkMapLayer>("chunks", 0.0);
        api.Logger.Notification("[Chart] Registered DimensionAwareChunkMapLayer (replaces vanilla terrain layer).");

        // The server keeps the vanilla waypoint layer and its data still routes to ours through
        // the shared "waypoints" code.
        mapManager.RegisterMapLayer<DimensionAwareWaypointMapLayer>("waypoints", 1.0);
        api.Logger.Notification("[Chart] Registered DimensionAwareWaypointMapLayer (replaces vanilla waypoints layer).");

        // The vanilla MapLayers are instantiated by WorldMapManager AFTER all ModSystems'
        // StartClientSide complete, so the MapLayers collection is empty at this point.
        // Defer the enumeration to LevelFinalize, which fires once the world is fully loaded
        // and every MapLayer (ours included) has been instantiated.
        api.Event.LevelFinalize += () => OnLevelFinalize(api, mapManager, manifoldClient);
    }

    /// <summary>
    /// Handles a Manifold dimension being destroyed: wipes its tile cache and, when it was
    /// the active dimension, clears the GPU components so the map shows no stale tiles.
    /// </summary>
    private void OnDimensionDestroyed(ICoreClientAPI api, string dimCode)
    {
        bool wasActive = _store!.CurrentDimCode == dimCode;
        _store.DeleteFor(dimCode);

        if (wasActive)
        {
            _layer ??= FindLayer<DimensionAwareChunkMapLayer>(api);
            _layer?.OnActiveStoreSwapped();
        }
    }

    /// <summary>
    /// LevelFinalize work: verifies that our layers replaced the vanilla ones, and runs the
    /// startup orphan-cache scan.
    /// </summary>
    private void OnLevelFinalize(ICoreClientAPI api, WorldMapManager mapManager, IManifoldClient? manifoldClient)
    {
        foreach (var layer in mapManager.MapLayers)
        {
            api.Logger.Notification(
                "[Chart] MapLayer present: {0} code={1}",
                layer.GetType().FullName,
                layer.LayerGroupCode);
        }

        // The layers are replaced by type re-registration, so just verify ours were created:
        // another mod re-registering "chunks" or "waypoints" after us would bring the vanilla
        // behaviour back without this trace.
        if (FindLayer<DimensionAwareChunkMapLayer>(api) is null)
        {
            api.Logger.Warning(
                "[Chart] DimensionAwareChunkMapLayer was not instantiated - another mod likely re-registered the 'chunks' layer. Custom dimensions will not be mapped.");
        }

        if (FindLayer<DimensionAwareWaypointMapLayer>(api) is null)
        {
            api.Logger.Warning(
                "[Chart] DimensionAwareWaypointMapLayer was not instantiated - another mod likely re-registered the 'waypoints' layer. Waypoint pins will not be dimension-filtered.");
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
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _tracker?.Stop();
        _store?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Finds the Chart-owned map layer instance of type <typeparamref name="T"/> that
    /// WorldMapManager created via Activator.CreateInstance. Returns null if the map
    /// manager or layer are not yet available.
    /// </summary>
    private static T? FindLayer<T>(ICoreClientAPI capi)
        where T : MapLayer
    {
        var wmm = capi.ModLoader.GetModSystem<WorldMapManager>();
        if (wmm?.MapLayers == null)
        {
            return null;
        }

        foreach (var layer in wmm.MapLayers)
        {
            if (layer is T chartLayer)
            {
                return chartLayer;
            }
        }

        return null;
    }
}
