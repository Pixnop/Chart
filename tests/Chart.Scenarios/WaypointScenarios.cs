namespace Chart.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Chart.Internal;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Xunit;

/// <summary>
/// Chart's waypoint filter (DimensionAwareWaypointMapLayer) runs on the client, which Atlas
/// cannot boot. Everything it consumes comes from the server, though: these scenarios have a
/// real player create pins with the vanilla <c>/waypoint</c> command (or die, for the pin the
/// server makes itself), capture the waypoint list the server sends that player on the world
/// map channel, and run Chart's own <see cref="WaypointDimension"/> over it. Not covered: the
/// client-side layer swap itself and the rendering.
/// </summary>
[Trait("Category", "E2E")]
public class WaypointScenarios : ChartScenarioBase
{
    private const string MapChannel = "worldmap";

    // Matches the fixture's FixedSpawn.
    private const int SpawnX = 512;
    private const int SpawnZ = 512;

    // The engine's void damage starts under Y -30, whatever the dimension.
    private const int VoidY = -35;

    // The icon vanilla gives a death pin. Its title is translated, its icon is not.
    private const string DeathPinIcon = "gravestone";

    /// <summary>
    /// The whole filter rests on vanilla storing the player's internal Y (y + dim * 32768) in
    /// the pin, so the dimension travels with every synced waypoint. A pin made in the overworld
    /// must show only on the overworld map, one made in a Manifold dimension only on that
    /// dimension's map.
    /// </summary>
    [AtlasScenario]
    public async Task SyncedWaypoints_Should_ShowOnlyInTheirDimension_When_CreatedInOverworldAndCustomDimension()
    {
        int slabId = await DimensionId("slab");
        ITestPlayer player = await World.JoinPlayer("chart_wpshow");

        await AddWaypoint(player, "overworld-pin");
        await SendTo(player, "slab", slabId);
        await AddWaypoint(player, "slab-pin");

        List<Waypoint> synced = LatestWaypoints(player);
        Assert.Equal(new[] { "overworld-pin", "slab-pin" }, synced.Select(w => w.Title));
        Assert.Equal(0, WaypointDimension.DimensionOf(synced[0].Position.Y));
        Assert.Equal(slabId, WaypointDimension.DimensionOf(synced[1].Position.Y));

        Assert.Equal(new[] { "overworld-pin" }, VisibleTitles(synced, 0));
        Assert.Equal(new[] { "slab-pin" }, VisibleTitles(synced, slabId));
    }

    /// <summary>
    /// The vanilla edit dialog turns a pin's index into <c>/waypoint modify|remove &lt;index&gt;</c>,
    /// so Chart keeps each visible pin's index into the full list rather than into the filtered
    /// one. Removing the only pin visible on the dimension map, by the index Chart keeps, must
    /// delete that pin and leave the overworld one alone; index 0 in the filtered view would
    /// have deleted the overworld pin instead.
    /// </summary>
    [AtlasScenario]
    public async Task RemovingAFilteredPin_Should_DeleteThatPin_When_UsingTheIndexChartKeeps()
    {
        int slabId = await DimensionId("slab");
        ITestPlayer player = await World.JoinPlayer("chart_wpremove");

        await AddWaypoint(player, "overworld-pin");
        await SendTo(player, "slab", slabId);
        await AddWaypoint(player, "slab-pin");

        int index = Assert.Single(VisibleIndices(LatestWaypoints(player), slabId));
        CommandResult removed = await player.ExecuteCommand($"/waypoint remove {index}");
        Assert.True(removed.Ok, removed.Message);
        await World.Ticks(2);

        Assert.Equal(new[] { "overworld-pin" }, LatestWaypoints(player).Select(w => w.Title));
    }

    /// <summary>
    /// The map's "add waypoint" dialog sends <c>/waypoint addati</c> with an explicit position,
    /// and the Y it computes is an overworld height with no dimension in it. Chart rewrites that
    /// Y before the dialog sends; this replays the dialog's command with the Y Chart produces
    /// and checks the server stores it as given, so the pin lands in the player's dimension.
    /// The rewrite of the dialog itself is client-side and not covered.
    /// </summary>
    [AtlasScenario]
    public async Task MapDialogWaypoint_Should_LandInThePlayersDimension_When_SentWithChartsInternalY()
    {
        int slabId = await DimensionId("slab");
        ITestPlayer player = await World.JoinPlayer("chart_wpdialog");
        await SendTo(player, "slab", slabId);

        double y = WaypointDimension.InternalY(player.Position.Y, slabId);
        CommandResult added = await player.ExecuteCommand(
            FormattableString.Invariant($"/waypoint addati circle ={SpawnX} ={y} ={SpawnZ} false #3fa7d6 map-pin"));
        Assert.True(added.Ok, added.Message);
        await World.Ticks(2);

        Waypoint pin = Assert.Single(LatestWaypoints(player));
        Assert.Equal(slabId, WaypointDimension.DimensionOf(pin.Position.Y));
        Assert.Equal(y, pin.Position.Y);
    }

    /// <summary>
    /// The server makes a death pin where the player dies, with the same internal Y as any other
    /// pin. The engine hurts whatever is under Y -30, so a player who falls out of a void
    /// dimension dies under Y 0 of it and the pin is stored just under the dimension's own
    /// slice of the world, which a truncating decode files in the dimension before. This puts a
    /// survival player under the slab and lets the engine's void damage kill it, then checks
    /// that the pin the server syncs is stored under the slab's slice and still decodes to the
    /// slab. The player is placed under the world rather than dropped: a test player runs no
    /// client physics, so the fall itself is not replayed.
    /// </summary>
    [AtlasScenario]
    public async Task DeathPin_Should_BelongToTheDimension_When_ThePlayerDiesUnderItsY0()
    {
        int slabId = await DimensionId("slab");
        ITestPlayer player = await World.JoinPlayer("chart_wpdeath");
        await SendTo(player, "slab", slabId);

        // A test player joins in creative mode, which takes no damage.
        CommandResult mode = await player.ExecuteCommand("/gamemode survival");
        Assert.True(mode.Ok, mode.Message);

        await player.TeleportTo(new BlockPos(SpawnX, VoidY, SpawnZ, slabId));
        await World.Until(() => !player.Entity.Alive);
        await World.Ticks(2);

        Waypoint pin = Assert.Single(LatestWaypoints(player), w => w.Icon == DeathPinIcon);

        // Under the slab's own slice: the case a truncating decode got wrong.
        double sliceStart = (double)slabId * BlockPos.DimensionBoundary;
        Assert.True(
            pin.Position.Y < sliceStart,
            FormattableString.Invariant($"The death pin's Y {pin.Position.Y} is not under the slab's slice, which starts at {sliceStart}."));
        Assert.Equal(slabId, WaypointDimension.DimensionOf(pin.Position.Y));
    }

    private static IEnumerable<int> VisibleIndices(List<Waypoint> waypoints, int dimension) =>
        Enumerable.Range(0, waypoints.Count)
            .Where(i => WaypointDimension.IsVisibleIn(waypoints[i].Position.Y, dimension));

    private static IEnumerable<string> VisibleTitles(List<Waypoint> waypoints, int dimension) =>
        VisibleIndices(waypoints, dimension).Select(i => waypoints[i].Title);

    private static List<Waypoint> LatestWaypoints(ITestPlayer player)
    {
        MapLayerData layer = player.Client.Packets<MapLayerUpdate>(MapChannel)
            .SelectMany(p => p.Maplayers)
            .Last(l => l.ForMapLayer == "waypoints");
        return SerializerUtil.Deserialize<List<Waypoint>>(layer.Data);
    }

    private async Task AddWaypoint(ITestPlayer player, string title)
    {
        CommandResult added = await player.ExecuteCommand($"/waypoint add #3fa7d6 {title}");
        Assert.True(added.Ok, added.Message);
        await World.Ticks(2);
    }

    private async Task SendTo(ITestPlayer player, string dimPath, int dimId)
    {
        CommandResult sent = await World.ExecuteCommand($"/chartfx send {player.Player.PlayerName} {dimPath}");
        Assert.True(sent.Ok, sent.Message);
        await World.Until(
            () => player.Position.dimension == dimId
                && Math.Abs(player.Position.X - SpawnX) <= 1
                && Math.Abs(player.Position.Z - SpawnZ) <= 1,
            timeoutTicks: 600);
    }
}
