using Chart.Internal;
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

        var mapManager = api.ModLoader.GetModSystem<WorldMapManager>();

        // Position 0.5 places this layer between the terrain layer (0.0) and marker layers (1.0).
        mapManager.RegisterMapLayer<DimensionAwareChunkMapLayer>("chart", 0.5);
        api.Logger.Notification("[Chart] Registered DimensionAwareChunkMapLayer.");
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
