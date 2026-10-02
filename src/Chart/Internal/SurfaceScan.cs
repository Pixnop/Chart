using System;

namespace Chart.Internal;

/// <summary>
/// Finds the block the map should draw for one column of a custom dimension, where the engine's
/// rain height map cannot be used: it belongs to the overworld column at the same X/Z.
/// </summary>
internal static class SurfaceScan
{
    /// <summary>Returned when the column holds nothing to draw.</summary>
    public const int NotFound = -1;

    /// <summary>
    /// Scans down from <paramref name="scanTop"/> to y = 1 and returns the Y of the first block
    /// that counts as a surface, or <see cref="NotFound"/>.
    /// </summary>
    /// <param name="blockIdAt">
    /// Block id at a given Y of the column, 0 where nothing counts as a surface: air, or a block
    /// the caller sees through.
    /// </param>
    /// <param name="scanTop">The Y the scan starts at.</param>
    /// <param name="skipCeiling">
    /// When true and the scan starts inside solid blocks, that run is the dimension's ceiling
    /// and is stepped through first, so a roofed dimension shows its floor instead of its roof.
    /// A column solid all the way down has no floor: it is drawn as its top block, which reads
    /// as a wall on the map.
    /// </param>
    public static int Find(Func<int, int> blockIdAt, int scanTop, bool skipCeiling)
    {
        ArgumentNullException.ThrowIfNull(blockIdAt);

        int y = scanTop;
        if (skipCeiling)
        {
            while (y > 0 && blockIdAt(y) != 0)
            {
                y--;
            }

            if (y <= 0)
            {
                return scanTop > 0 ? scanTop : NotFound;
            }

            // The block that ended the ceiling is known to be empty.
            y--;
        }

        for (; y > 0; y--)
        {
            if (blockIdAt(y) != 0)
            {
                return y;
            }
        }

        return NotFound;
    }
}
