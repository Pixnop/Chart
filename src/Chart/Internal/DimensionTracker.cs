using System;
using System.Linq;
using Manifold.Api.Helpers;
using Vintagestory.API.Client;

namespace Chart.Internal;

/// <summary>
/// Detects when the local player moves to a different dimension and triggers a
/// <see cref="PerDimensionMapStore.LoadFor"/> call, which saves the previous dimension's
/// data before activating the new one.
/// </summary>
internal sealed class DimensionTracker
{
    private readonly ICoreClientAPI _capi;

    private readonly PerDimensionMapStore _store;

    private long _listenerId;

    private Action? _onStoreSwapped;

    /// <param name="capi">Client API.</param>
    /// <param name="store">The store to notify on dimension change.</param>
    public DimensionTracker(ICoreClientAPI capi, PerDimensionMapStore store)
    {
        ArgumentNullException.ThrowIfNull(capi);
        ArgumentNullException.ThrowIfNull(store);
        _capi = capi;
        _store = store;
    }

    /// <summary>
    /// Optional callback invoked on the main thread immediately after the active store
    /// is swapped to a new dimension. Used by <see cref="ChartModSystem"/> to notify
    /// <see cref="DimensionAwareChunkMapLayer"/> so it can rebuild its GPU components.
    /// </summary>
    public Action? OnStoreSwapped
    {
        get => _onStoreSwapped;
        set => _onStoreSwapped = value;
    }

    /// <summary>
    /// Subscribes to a per-second tick listener and registers a shutdown handler
    /// that saves the current store.
    /// </summary>
    public void Start()
    {
        _listenerId = _capi.Event.RegisterGameTickListener(OnTick, 1000);
        _capi.Event.LeaveWorld += OnLeaveWorld;
    }

    /// <summary>Unregisters the tick listener.</summary>
    public void Stop()
    {
        if (_listenerId != 0)
        {
            _capi.Event.UnregisterGameTickListener(_listenerId);
            _listenerId = 0;
        }

        _capi.Event.LeaveWorld -= OnLeaveWorld;
    }

    private void OnLeaveWorld()
    {
        _store.SaveCurrent();
    }

    private void OnTick(float dt)
    {
        var entity = _capi.World.Player?.Entity;
        if (entity is null)
        {
            return;
        }

        var pos = EntityPosAccess.PosOrNull(entity);
        if (pos is null)
        {
            return;
        }

        var dimCode = ResolveDimCode(pos.Dimension);
        if (dimCode == _store.CurrentDimCode)
        {
            return;
        }

        _store.LoadFor(dimCode);
        _capi.Logger.Notification("[Chart] swapped to dim '{0}'", dimCode);

        // Notify the map layer so it can rebuild its GPU components from the new store.
        _onStoreSwapped?.Invoke();
    }

    private string ResolveDimCode(int dimensionId)
    {
        try
        {
            var manifold = ManifoldAccess.GetClient(_capi);
            if (manifold is not null)
            {
                var dim = manifold.Dimensions.FirstOrDefault(d => d.InternalId == dimensionId);
                if (dim is not null)
                {
                    return dim.Code.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _capi.Logger.Warning("[Chart] ManifoldAccess.GetClient failed: {0}", ex.Message);
        }

        return dimensionId.ToString();
    }
}
