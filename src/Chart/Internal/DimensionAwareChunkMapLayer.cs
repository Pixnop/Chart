using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Chart.Internal;

/// <summary>
/// Map layer skeleton that registers with WorldMapManager.
/// Renders nothing yet; full tile painting comes in a later task.
/// </summary>
internal sealed class DimensionAwareChunkMapLayer : RGBMapLayer
{
    // MapLayer.api is ICoreAPI; cached cast to client side for all client operations.
    private readonly ICoreClientAPI? _capi;

    /// <summary>
    /// VS instantiates map layers via Activator.CreateInstance(typeof(T), api, mapSink).
    /// The constructor must be exactly (ICoreAPI, IWorldMapManager) - no extra parameters.
    /// </summary>
    public DimensionAwareChunkMapLayer(ICoreAPI api, IWorldMapManager mapSink)
        : base(api, mapSink)
    {
        _capi = api as ICoreClientAPI;
    }

    // --- MapLayer abstract properties ---

    /// <inheritdoc/>
    public override string Title => "Chart";

    /// <inheritdoc/>
    public override string LayerGroupCode => "chart";

    /// <inheritdoc/>
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

    // --- RGBMapLayer abstract properties ---

    /// <inheritdoc/>
    public override MapLegendItem[] LegendItems => System.Array.Empty<MapLegendItem>();

    /// <inheritdoc/>
    public override EnumMinMagFilter MagFilter => EnumMinMagFilter.Nearest;

    /// <inheritdoc/>
    public override EnumMinMagFilter MinFilter => EnumMinMagFilter.Nearest;

    // --- Lifecycle overrides ---

    /// <inheritdoc/>
    public override void OnMapOpenedClient()
    {
        _capi?.Logger.Notification("[Chart] map opened");
        base.OnMapOpenedClient();
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
        // No-op stub - tile generation wired in a later task.
    }

    /// <inheritdoc/>
    public override void Render(GuiElementMap mapElem, float dt)
    {
        // No-op stub - GPU blit wired in a later task.
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        base.Dispose();
    }
}
