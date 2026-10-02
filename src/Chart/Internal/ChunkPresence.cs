using Vintagestory.API.Common;

namespace Chart.Internal;

/// <summary>
/// What the map layer needs from the engine before it reads a chunk column, by dimension. Map
/// chunks, which carry the height map, are not per dimension: there is one per (x, z) and it belongs
/// to the overworld column. The client receives it with the overworld slices of that column and drops
/// it when it unloads the last of them, and the slices a mod force-sends into a dimension, as
/// Manifold does, do not bring one, so a custom dimension's column can be fully loaded without it.
/// Only the overworld needs a map chunk.
/// </summary>
internal static class ChunkPresence
{
    /// <summary>
    /// Whether a column must have its map chunk to be drawn: in the overworld, where the height map
    /// is the column's own.
    /// </summary>
    /// <param name="dimension">The dimension the map is drawn for.</param>
    public static bool NeedsMapChunk(int dimension) => dimension == 0;

    /// <summary>Whether the client holds the slice, fully loaded from the server.</summary>
    /// <param name="slice">The chunk slice the client returns, or null where it holds none.</param>
    public static bool IsLoaded(IWorldChunk? slice) => slice is IClientChunk { LoadedFromServer: true };

    /// <summary>
    /// Whether the column next to the one drawn can be read: in the overworld its map chunk is
    /// there, elsewhere its slice at the height read is loaded.
    /// </summary>
    /// <param name="dimension">The dimension the map is drawn for.</param>
    /// <param name="mapChunk">The map chunk at the neighbour's (x, z), or null where the client holds none.</param>
    /// <param name="slice">The neighbour's slice of the dimension at the height read, or null.</param>
    public static bool IsNeighbourPresent(int dimension, IMapChunk? mapChunk, IWorldChunk? slice) =>
        NeedsMapChunk(dimension) ? mapChunk != null : IsLoaded(slice);
}
