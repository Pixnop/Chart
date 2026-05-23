using Vintagestory.API.Client;
using Vintagestory.API.Common;

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
        api.Logger.Notification("[Chart] Loaded (boot stub).");
    }
}
