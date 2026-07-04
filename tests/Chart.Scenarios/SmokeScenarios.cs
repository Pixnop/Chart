namespace Chart.Scenarios;

using Atlas.XUnit;
using Xunit;

/// <summary>
/// Boot-level smoke checks: the embedded server starts with the published Manifold
/// release zip and the chartfixture mod staged, and the ModLoader accepts both.
/// Chart itself is client-only and cannot load here; these scenarios validate the
/// runtime foundation Chart sits on.
/// </summary>
[Trait("Category", "E2E")]
public class SmokeScenarios : ChartScenarioBase
{
    [AtlasScenario]
    public async Task Server_Should_LoadManifoldReleaseAndFixture_When_Booting()
    {
        Assert.True(
            World.Api.ModLoader.IsModEnabled("manifold"),
            "The manifold mod (published release zip) is not enabled in the embedded server.");

        Assert.True(
            World.Api.ModLoader.IsModEnabled("chartfixture"),
            "The chartfixture test mod is not enabled in the embedded server.");

        // Looked up by name: scenario code must not reference Manifold types
        // (the ModLoader loads its own copy of Manifold.dll).
        Assert.NotNull(World.Api.ModLoader.GetModSystem("Manifold.ManifoldModSystem"));
        Assert.NotNull(World.Api.ModLoader.GetModSystem("ChartFixture.ChartFixtureModSystem"));

        await World.Ticks(1);
    }
}
