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
    /// <summary>Returns the dimension index encoded in a waypoint's Y coordinate.</summary>
    /// <param name="waypointY">The waypoint's stored (internal) Y.</param>
    /// <returns>The dimension index; 0 for overworld heights, including slightly negative Y.</returns>
    public static int DimensionOf(double waypointY) => (int)(waypointY / BlockPos.DimensionBoundary);

    /// <summary>Whether a waypoint pin belongs on the map of the given dimension.</summary>
    /// <param name="waypointY">The waypoint's stored (internal) Y.</param>
    /// <param name="dimension">The player's current dimension index.</param>
    /// <returns>True when the pin was created in that dimension.</returns>
    public static bool IsVisibleIn(double waypointY, int dimension) => DimensionOf(waypointY) == dimension;
}
