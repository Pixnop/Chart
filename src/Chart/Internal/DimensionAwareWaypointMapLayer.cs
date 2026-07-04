using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Chart.Internal;

/// <summary>
/// Drop-in replacement for the vanilla <see cref="WaypointMapLayer"/> that only renders
/// the pins belonging to the player's current dimension. Registered client-side under
/// the same "waypoints" layer code before WorldMapManager instantiates layers, so the
/// vanilla layer type is never created on the client; the server keeps the vanilla
/// layer (commands, persistence and resend logic are untouched).
///
/// The dimension of a pin is intrinsic to its position: vanilla waypoint creation
/// stores <c>EntityPos.XYZ</c> whose Y is <c>InternalY</c> (see
/// <see cref="WaypointDimension"/>), so filtering needs no extra bookkeeping and is
/// retroactively correct for waypoints created before Chart was installed.
///
/// The base class's component list and rebuild method are private, so this subclass
/// maintains its own component list. Components are constructed with their ORIGINAL
/// index into <see cref="WaypointMapLayer.ownWaypoints"/> (kept unfiltered), because
/// the vanilla edit dialog turns that index into /waypoint modify|remove commands -
/// filtering the list itself would corrupt those indices and edit the wrong waypoint.
/// </summary>
public class DimensionAwareWaypointMapLayer : WaypointMapLayer
{
    // Temporary waypoints (AddTemporaryWaypoint is public but not virtual) land in the
    // base's private list; read it via reflection so third-party callers still get
    // their temporary pins rendered. Null when the field disappears in a game update.
    [SuppressMessage(
        "Major Code Smell",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "Read-only peek at the base layer's private temporary-pin list; the base offers no accessible alternative and a missing field degrades gracefully to an empty list.")]
    private static readonly FieldInfo? TmpComponentsField = typeof(WaypointMapLayer)
        .GetField("tmpWayPointComponents", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<MapComponent> _components = new();

    private readonly ICoreClientAPI? _capi;

    public DimensionAwareWaypointMapLayer(ICoreAPI api, IWorldMapManager mapSink)
        : base(api, mapSink)
    {
        _capi = api as ICoreClientAPI;
    }

    /// <inheritdoc/>
    public override void OnDataFromServer(byte[] data)
    {
        // Mirrors the base implementation (whose rebuild is private): ownWaypoints keeps
        // the FULL server list; filtering happens at component-build time only.
        ownWaypoints.Clear();
        ownWaypoints.AddRange(SerializerUtil.Deserialize<List<Waypoint>>(data));
        RebuildFilteredComponents();
    }

    /// <inheritdoc/>
    public override void OnMapOpenedClient()
    {
        reloadIconTextures();
        ensureIconTexturesLoaded();
        RebuildFilteredComponents();
    }

    // OnMapClosedClient is intentionally NOT overridden: the base clears its
    // temporary-waypoint bookkeeping there, and our own components are rebuilt on
    // every map open anyway.

    /// <inheritdoc/>
    public override void Render(GuiElementMap mapElem, float dt)
    {
        if (!Active)
        {
            return;
        }

        foreach (var comp in _components)
        {
            comp.Render(mapElem, dt);
        }

        foreach (var comp in TemporaryComponents())
        {
            comp.Render(mapElem, dt);
        }
    }

    /// <inheritdoc/>
    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (!Active)
        {
            return;
        }

        foreach (var comp in _components)
        {
            comp.OnMouseMove(args, mapElem, hoverText);
        }

        foreach (var comp in TemporaryComponents())
        {
            comp.OnMouseMove(args, mapElem, hoverText);
        }
    }

    /// <inheritdoc/>
    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (!Active)
        {
            return;
        }

        foreach (var comp in _components)
        {
            comp.OnMouseUpOnElement(args, mapElem);
            if (args.Handled)
            {
                return;
            }
        }

        foreach (var comp in TemporaryComponents())
        {
            comp.OnMouseUpOnElement(args, mapElem);
            if (args.Handled)
            {
                return;
            }
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        DisposeOwnComponents();
        base.Dispose();
    }

    /// <summary>
    /// Rebuilds the filtered component list for the player's current dimension. Called
    /// on server data, on map open, and by <see cref="ChartModSystem"/> when the player
    /// transits to another dimension while the map (or minimap) is open.
    /// </summary>
    public void OnPlayerDimensionChanged() => RebuildFilteredComponents();

    private void RebuildFilteredComponents()
    {
        if (!mapSink.IsOpened)
        {
            return;
        }

        DisposeOwnComponents();

        int currentDim = _capi?.World.Player?.Entity?.Pos.Dimension ?? 0;
        for (int i = 0; i < ownWaypoints.Count; i++)
        {
            if (!WaypointDimension.IsVisibleIn(ownWaypoints[i].Position.Y, currentDim))
            {
                continue;
            }

            _components.Add(new WaypointMapComponent(i, ownWaypoints[i], this, _capi!));
        }
    }

    private void DisposeOwnComponents()
    {
        foreach (var comp in _components)
        {
            comp.Dispose();
        }

        _components.Clear();
    }

    private IEnumerable<MapComponent> TemporaryComponents() =>
        TmpComponentsField?.GetValue(this) as List<MapComponent> ?? (IEnumerable<MapComponent>)System.Array.Empty<MapComponent>();
}
