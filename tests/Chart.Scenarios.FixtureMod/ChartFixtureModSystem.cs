namespace ChartFixture;

using System.Text;
using Manifold.Api;
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

/// <summary>
/// Server-side fixture driven by the Chart.Scenarios suite. Chart itself is a
/// client-only mod and cannot load in Atlas's headless server; this fixture
/// exercises the engine and Manifold contracts Chart's renderer depends on.
/// All Manifold API calls live here because scenario code cannot share assembly
/// identity with the ModLoader-loaded Manifold.dll. Results are published
/// through SaveGame data.
/// </summary>
public sealed class ChartFixtureModSystem : ModSystem
{
    internal const string Domain = "chartfixture";

    private static readonly BlockPos FixedSpawn = new(512, 8, 512, 0);

    private ICoreServerAPI _sapi = null!;
    private IManifoldServer _manifold = null!;

    // Run after Manifold (0.05), like any consumer mod.
    public override double ExecuteOrder() => 0.5;

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        _sapi = api;
        _manifold = api.GetManifoldServer(this);

        IDimension slab = _manifold.Registry
            .Define(new AssetLocation(Domain, "slab"))
            .Persistent()
            .WithWorldgen(new SlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .RegisterStatic();
        PublishDimensionId("slab", slab.InternalId);
        PublishDimensionCode("slab", slab.Code.ToString());
        PregenerateSpawn(slab);

        // All-air dimension for the RainHeightMap pin: in a void dim there is no
        // solid block any height could legitimately point at, so the assertion
        // cannot pass by coincidence with the overworld's superflat surface.
        IDimension voidDim = _manifold.Registry
            .Define(new AssetLocation(Domain, "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(2)
            .RegisterStatic();
        PublishDimensionId("void", voidDim.InternalId);
        PregenerateSpawn(voidDim);

        // Chart purges a dimension's tile cache when the client mirror relays this
        // event; pin the server-side contract that feeds it.
        _manifold.Registry.Destroyed += (_, e) => _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:event:destroyed:{e.Dimension.Code.Path}", new[] { (byte)1 });

        RegisterCommands(api);
    }

    private void PublishDimensionId(string path, int internalId)
    {
        _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:dimid:{path}",
            BitConverter.GetBytes(internalId));
    }

    private void PublishDimensionCode(string path, string code)
    {
        _sapi.WorldManager.SaveGame.StoreData(
            $"{Domain}:dimcode:{path}",
            Encoding.UTF8.GetBytes(code));
    }

    /// <summary>
    /// RegisterStatic only records the dimension; it does not generate any terrain. Manifold's
    /// active worldgen driver only runs through Transitions (player transit / join), so nothing
    /// generates a boot-registered dimension's spawn region on its own. Force it here via a no-op
    /// TeleportBlock: this call exists purely for its generate-destination-region side effect via
    /// DimensionGenerator.EnsureRegion, not for the move itself. The source position must be air
    /// so the move is a guaranteed no-op; a near-ceiling position at the world origin is reliably
    /// air, unlike y=1 near bedrock, and using a non-air source would actually move a real
    /// overworld block.
    /// </summary>
    private void PregenerateSpawn(IDimension dimension)
    {
        var overworldAir = new BlockPos(0, _sapi.WorldManager.MapSizeY - 2, 0, 0);
        var target = new BlockPos(FixedSpawn.X, FixedSpawn.Y, FixedSpawn.Z, dimension.InternalId);
        try
        {
            _manifold.Transitions.TeleportBlock(overworldAir, dimension.Code, target);
        }
        catch (Exception ex)
        {
            Mod.Logger.Error(
                "Spawn pregeneration failed for dimension {0}: {1}. Scenarios probing this dimension's terrain will time out.",
                dimension.Code,
                ex);
        }
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create("chartfx")
            .WithDescription("Drives the Manifold API for Chart's Atlas assumption scenarios.")
            .RequiresPrivilege("controlserver")
            .BeginSubCommand("create-ephemeral")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnCreateEphemeral)
            .EndSubCommand()
            .BeginSubCommand("remove")
                .WithArgs(parsers.Word("dimpath"))
                .HandleWith(OnRemove)
            .EndSubCommand()
            .BeginSubCommand("pregen")
                .WithArgs(parsers.Word("dimpath"), parsers.Int("x"), parsers.Int("z"))
                .HandleWith(OnPregen)
            .EndSubCommand();
    }

    /// <summary>
    /// Forces region generation of a dimension at an arbitrary column, through the same
    /// no-op TeleportBlock trick as <see cref="PregenerateSpawn"/>. Scenarios use it to
    /// generate a dimension under the world spawn column, whose overworld map chunk is
    /// guaranteed loaded.
    /// </summary>
    private TextCommandResult OnPregen(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        var overworldAir = new BlockPos(0, _sapi.WorldManager.MapSizeY - 2, 0, 0);
        var target = new BlockPos((int)args[1], FixedSpawn.Y, (int)args[2], 0);
        _manifold.Transitions.TeleportBlock(overworldAir, new AssetLocation(Domain, path), target);
        return TextCommandResult.Success("ok");
    }

    private TextCommandResult OnCreateEphemeral(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        IDimension dimension = _manifold.Registry
            .Define(new AssetLocation(Domain, path))
            .Ephemeral()
            .WithWorldgen(new SlabWorldgen())
            .WithFixedSpawn(FixedSpawn)
            .WithGenerationRadius(1)
            .Create();
        PublishDimensionId(path, dimension.InternalId);

        // Same pregeneration problem as boot-time dimensions: Create() only registers the
        // dimension, it does not generate terrain. Force it here too, or scenarios probing
        // the dimension's terrain would time out.
        PregenerateSpawn(dimension);
        return TextCommandResult.Success($"created {dimension.InternalId}");
    }

    private TextCommandResult OnRemove(TextCommandCallingArgs args)
    {
        var path = (string)args[0];
        bool removed = _manifold.Registry.TryRemove(new AssetLocation(Domain, path));
        return TextCommandResult.Success(removed ? "removed" : "not-removed");
    }
}
