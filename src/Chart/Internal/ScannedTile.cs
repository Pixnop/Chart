using System;

namespace Chart.Internal;

/// <summary>
/// The surface heights the column scan finds across one map tile, and the relief shading drawn from
/// them. Outside the overworld the engine's height map cannot give them (see
/// <see cref="SurfaceScan"/>), so each column is scanned once and its height kept for the pixels
/// south and east of it, whose relief compares with it.
/// </summary>
internal sealed class ScannedTile
{
    private const int Unscanned = int.MinValue;

    private readonly int _size;
    private readonly int _originX;
    private readonly int _originZ;
    private readonly int _scanTop;
    private readonly bool _skipCeiling;
    private readonly Func<int, int, int, int> _surfaceIdAt;

    // Heights found so far, with one extra row and column on the north and west sides for the relief
    // of the tile's edge. Local (lx, lz) lives at ((lz + 1) * (size + 1)) + lx + 1.
    private readonly int[] _heights;

    /// <summary>Creates a tile none of whose columns is scanned yet.</summary>
    /// <param name="size">Edge of the tile, in blocks.</param>
    /// <param name="originX">World X of the tile's north-west column.</param>
    /// <param name="originZ">World Z of the tile's north-west column.</param>
    /// <param name="scanTop">The Y each column scan starts at.</param>
    /// <param name="skipCeiling">Whether a scan steps through the ceiling it starts in.</param>
    /// <param name="surfaceIdAt">
    /// Block id at a world position (x, y, z), 0 where nothing counts as a surface.
    /// </param>
    public ScannedTile(int size, int originX, int originZ, int scanTop, bool skipCeiling, Func<int, int, int, int> surfaceIdAt)
    {
        _size = size;
        _originX = originX;
        _originZ = originZ;
        _scanTop = scanTop;
        _skipCeiling = skipCeiling;
        _surfaceIdAt = surfaceIdAt;
        _heights = new int[(size + 1) * (size + 1)];
        Array.Fill(_heights, Unscanned);
    }

    /// <summary>
    /// Scans the tile's own column at local (<paramref name="lx"/>, <paramref name="lz"/>) the first
    /// time it is asked, keeps what it found, or <see cref="SurfaceScan.NotFound"/>, and returns it.
    /// </summary>
    public int Scan(int lx, int lz)
    {
        int index = ((lz + 1) * (_size + 1)) + lx + 1;
        if (_heights[index] == Unscanned)
        {
            _heights[index] = ScanColumn(_originX + lx, _originZ + lz);
        }

        return _heights[index];
    }

    /// <summary>
    /// Relief of the pixel at local (<paramref name="lx"/>, <paramref name="lz"/>), whose surface is
    /// at <paramref name="surfaceY"/>, from how far that stands above or below the scanned surfaces
    /// of its north-west, west and north neighbours: the vanilla rule, on scanned heights. A
    /// neighbour outside the tile (local -1) is scanned on demand, once. One with nothing to draw
    /// counts as <paramref name="surfaceY"/>, so it casts no relief.
    /// </summary>
    public float Relief(int lx, int lz, int surfaceY) =>
        Hillshade.Factor(
            surfaceY - NeighbourHeight(lx - 1, lz - 1, surfaceY),
            surfaceY - NeighbourHeight(lx - 1, lz, surfaceY),
            surfaceY - NeighbourHeight(lx, lz - 1, surfaceY));

    private int NeighbourHeight(int lx, int lz, int fallback)
    {
        int height = Scan(lx, lz);
        return height == SurfaceScan.NotFound ? fallback : height;
    }

    // A method of its own: the compiler allocates a lambda's closure when the method that holds
    // the lambda is entered, so inside Scan it would cost every call, scanned or not.
    private int ScanColumn(int x, int z) =>
        SurfaceScan.Find(y => _surfaceIdAt(x, y, z), _scanTop, _skipCeiling);
}
