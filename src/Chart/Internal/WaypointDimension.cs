using System;
using Vintagestory.API.MathTools;

namespace Chart.Internal;

/// <summary>
/// Decodes the dimension a waypoint belongs to from its stored position. Vanilla
/// waypoint creation stores <c>EntityPos.XYZ</c>, whose Y is <c>InternalY</c>
/// (<c>y + dimension * BlockPos.DimensionBoundary</c>), so the dimension is
/// intrinsic to every synced waypoint - including ones created before Chart was
/// installed. No per-waypoint bookkeeping is needed.
/// </summary>
internal static class WaypointDimension
{
    /// <summary>
    /// Returns the dimension index encoded in a waypoint's Y coordinate: the slice of the world
    /// the value is nearest to, not the one it falls in. A pin can sit under Y 0 of its dimension
    /// (a death in the void is stored around y -35), which puts its Y just under that dimension's
    /// slice. Worlds are at most 16384 blocks high, half a slice, so a real height always stays
    /// nearest to its own dimension.
    /// </summary>
    /// <param name="waypointY">The waypoint's stored (internal) Y.</param>
    /// <returns>The dimension index.</returns>
    public static int DimensionOf(double waypointY) => (int)Math.Round(waypointY / BlockPos.DimensionBoundary);

    /// <summary>
    /// The Y to store for a waypoint at height <paramref name="y"/> of a dimension, so that
    /// <see cref="DimensionOf"/> finds the dimension again.
    /// </summary>
    /// <param name="y">Height inside the dimension.</param>
    /// <param name="dimension">The dimension index.</param>
    /// <returns>The internal Y.</returns>
    public static double InternalY(double y, int dimension) => y + ((double)dimension * BlockPos.DimensionBoundary);

    /// <summary>Whether a waypoint pin belongs on the map of the given dimension.</summary>
    /// <param name="waypointY">The waypoint's stored (internal) Y.</param>
    /// <param name="dimension">The player's current dimension index.</param>
    /// <returns>True when the pin was created in that dimension.</returns>
    public static bool IsVisibleIn(double waypointY, int dimension) => DimensionOf(waypointY) == dimension;
}
