using Chart.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Chart;

/// <summary>Client-only entry point for the Chart companion mod.</summary>
public sealed class ChartModSystem : ModSystem
{
    /// <inheritdoc/>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <inheritdoc/>
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        var mapManager = api.ModLoader.GetModSystem<WorldMapManager>();

        // Position 0.5 places this layer between the terrain layer (0.0) and marker layers (1.0).
        mapManager.RegisterMapLayer<DimensionAwareChunkMapLayer>("chart", 0.5);
        api.Logger.Notification("[Chart] Registered DimensionAwareChunkMapLayer.");
    }
}
