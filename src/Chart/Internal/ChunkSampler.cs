using System;

namespace Chart.Internal;

/// <summary>
/// Pure RGBA tile painter. Given a height map and per-column top block ids plus a palette
/// function, produces a 32 by 32 pixel RGBA tile (4 bytes per pixel, row-major).
/// </summary>
internal static class ChunkSampler
{
    /// <summary>Tile width and height in pixels (one pixel per column of the 32x32 chunk).</summary>
    public const int TileEdge = 32;

    /// <summary>Number of bytes in one tile (32 * 32 * 4 RGBA).</summary>
    public const int TileBytes = TileEdge * TileEdge * 4;

    /// <summary>
    /// Sample one tile. <paramref name="heights"/> and <paramref name="topBlockIds"/> are
    /// row-major 32 by 32 arrays addressed as <c>z * 32 + x</c>. <paramref name="palette"/>
    /// returns an RGBA value in the order (red, green, blue, alpha) packed in a uint as
    /// <c>0xRRGGBBAA</c>.
    /// </summary>
    /// <param name="heights">Top block Y per column.</param>
    /// <param name="topBlockIds">Top block id per column (0 = air / no surface).</param>
    /// <param name="palette">Block id to packed RGBA.</param>
    /// <returns>A 4096-byte RGBA tile, row-major.</returns>
    public static byte[] Sample(int[] heights, int[] topBlockIds, Func<int, uint> palette)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(topBlockIds);
        ArgumentNullException.ThrowIfNull(palette);

        if (heights.Length != TileEdge * TileEdge)
        {
            throw new ArgumentException("heights must be 32*32");
        }

        if (topBlockIds.Length != TileEdge * TileEdge)
        {
            throw new ArgumentException("topBlockIds must be 32*32");
        }

        var tile = new byte[TileBytes];
        for (int z = 0; z < TileEdge; z++)
        {
            for (int x = 0; x < TileEdge; x++)
            {
                int i = (z * TileEdge) + x;
                uint rgba = topBlockIds[i] == 0 ? 0u : palette(topBlockIds[i]);
                int p = i * 4;
                tile[p + 0] = (byte)((rgba >> 24) & 0xFF); // R
                tile[p + 1] = (byte)((rgba >> 16) & 0xFF); // G
                tile[p + 2] = (byte)((rgba >> 8) & 0xFF);  // B
                tile[p + 3] = (byte)(rgba & 0xFF);          // A
            }
        }

        return tile;
    }
}
