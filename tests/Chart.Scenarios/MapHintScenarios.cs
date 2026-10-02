namespace Chart.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Chart.Internal;
using Manifold.Internal.Networking;
using Xunit;

/// <summary>
/// Chart reads a dimension's map hints on the client, from the mirror Manifold builds out of
/// what the server sends. Atlas runs no client, so the mirror itself is out of reach; what a
/// scenario can pin is that the hint is in the manifest a joining player receives. Without that
/// the cavern scan would silently fall back to the default on every real server.
/// </summary>
[Trait("Category", "E2E")]
public class MapHintScenarios : ChartScenarioBase
{
    [AtlasScenario]
    public async Task JoinManifest_Should_CarryTheScanTopHint_When_ADimensionDeclaresIt()
    {
        await DimensionId("cavern");

        ITestPlayer player = await World.JoinPlayer("chart_hint");
        await World.Ticks(2);

        ManifestSnapshotPacket manifest = Assert.Single(player.Client.Packets<ManifestSnapshotPacket>("manifold:dims"));
        DimensionDescriptor cavern = Assert.Single(manifest.Dimensions, d => d.Code == "chartfixture:cavern");
        MetadataEntry hint = Assert.Single(cavern.Metadata, m => m.Key == MapHints.ScanTopKey);
        Assert.Equal(21, hint.IntegerValue);
    }
}
