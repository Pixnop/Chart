using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class ScannedTileTests
{
    private const int Size = 8;

    // Where the scans start: above every height the fields below reach.
    private const int Top = 40;

    [Theory]
    [InlineData(true, 4)] // the cavern floor, under the ceiling the scan starts in
    [InlineData(false, 21)] // the roof
    public void Scan_Should_ReturnWhatSurfaceScanFinds_When_TheColumnIsRoofed(bool skipCeiling, int expected)
    {
        var tile = new ScannedTile(Size, 0, 0, scanTop: 21, skipCeiling, (x, y, z) => Cavern(y));

        int found = tile.Scan(2, 3);

        Assert.Equal(expected, found);
        Assert.Equal(SurfaceScan.Find(Cavern, scanTop: 21, skipCeiling), found);
    }

    [Fact]
    public void Scan_Should_ReturnNotFound_When_TheColumnIsEmpty()
    {
        var tile = new ScannedTile(Size, 0, 0, Top, skipCeiling: false, (x, y, z) => 0);

        Assert.Equal(SurfaceScan.NotFound, tile.Scan(1, 1));
    }

    [Fact]
    public void Scan_Should_ReadTheWorldColumnOfThePixel_When_TheTileLiesAtNegativeCoordinates()
    {
        var columns = new HashSet<(int X, int Z)>();
        var tile = new ScannedTile(Size, -64, -32, Top, skipCeiling: false, (x, y, z) =>
        {
            columns.Add((x, z));
            return 0;
        });

        tile.Scan(3, 5);

        Assert.Equal(new[] { (-61, -27) }, columns);
    }

    [Fact]
    public void Relief_Should_BeNeutral_When_TheGroundIsFlatAcrossTheTileAndItsNeighbours()
    {
        var tile = new ScannedTile(Size, 40, -24, Top, skipCeiling: false, Columns((x, z) => 7));

        foreach (var (lx, lz) in Grid(Size))
        {
            Assert.Equal(1f, tile.Relief(lx, lz, tile.Scan(lx, lz)));
        }
    }

    [Theory]
    [InlineData(3, 4)] // inside the tile
    [InlineData(3, 0)] // north row: the two columns it reads north of the tile belong to the tile to the north
    [InlineData(0, 4)] // west column
    [InlineData(0, 0)] // north-west corner
    [InlineData(Size - 1, Size - 1)] // the last pixel
    public void Relief_Should_BeTheHillshadeOfTheThreeHeightDifferences_When_TheNeighboursAreAtOtherHeights(int lx, int lz)
    {
        const int originX = -48;
        const int originZ = 64;
        int pixelX = originX + lx;
        int pixelZ = originZ + lz;

        // Flat ground at 20, but the pixel's three neighbours each sit at another height: it stands one
        // block above the north-west one, four below the west one and two above the north one. Reading
        // the columns outside the tile as empty would give another factor, on every edge.
        int HeightAt(int x, int z) => (x - pixelX, z - pixelZ) switch
        {
            (-1, -1) => 19,
            (-1, 0) => 24,
            (0, -1) => 18,
            _ => 20,
        };
        var tile = new ScannedTile(Size, originX, originZ, Top, skipCeiling: false, Columns(HeightAt));

        Assert.Equal(Hillshade.Factor(1, -4, 2), tile.Relief(lx, lz, 20));
    }

    [Fact]
    public void Relief_Should_ScanOnlyTheNorthWestWestAndNorthColumns_When_NothingWasScannedYet()
    {
        var reads = new List<(int X, int Y, int Z)>();
        var tile = new ScannedTile(Size, -64, -32, Top, skipCeiling: false, Recording(reads, Columns((x, z) => 7)));

        tile.Relief(3, 5, 7);

        var columns = reads.Select(r => (r.X, r.Z)).Distinct().OrderBy(c => c.X).ThenBy(c => c.Z);
        Assert.Equal(new[] { (-62, -28), (-62, -27), (-61, -28) }, columns);
    }

    [Fact]
    public void Relief_Should_CastNoRelief_When_ANeighbourHasNothingToDraw()
    {
        // The pixel at (3, 3) stands at 7. The columns north-west and north of it hold nothing; the one
        // west of it is two blocks lower. Only that one may count.
        static int HeightAt(int x, int z) => (x, z) switch
        {
            (2, 2) or (3, 2) => 0,
            (2, 3) => 5,
            _ => 7,
        };
        var tile = new ScannedTile(Size, 0, 0, Top, skipCeiling: false, Columns(HeightAt));

        Assert.Equal(Hillshade.Factor(0, 2, 0), tile.Relief(3, 3, 7));
    }

    [Fact]
    public void Relief_Should_NotScanAColumnAgain_When_ItWasScannedAndHeldNothing()
    {
        var reads = new List<(int X, int Y, int Z)>();
        var tile = new ScannedTile(Size, 0, 0, Top, skipCeiling: false, Recording(reads, Columns((x, z) => x == 2 && z == 2 ? 0 : 7)));
        Assert.Equal(SurfaceScan.NotFound, tile.Scan(2, 2));
        reads.Clear();

        // The empty column is the north-west neighbour of this pixel.
        tile.Relief(3, 3, 7);

        Assert.DoesNotContain(reads, r => r.X == 2 && r.Z == 2);
    }

    [Fact]
    public void Scan_Should_ScanEveryColumnOfTheTileAndItsBorderOnce_When_EveryPixelIsScannedThenShaded()
    {
        var reads = new List<(int X, int Y, int Z)>();
        var tile = new ScannedTile(Size, 16, -24, Top, skipCeiling: false, Recording(reads, Columns(Bumpy)));

        foreach (var (lx, lz) in Grid(Size))
        {
            tile.Scan(lx, lz);
        }

        foreach (var (lx, lz) in Grid(Size))
        {
            tile.Relief(lx, lz, Bumpy(16 + lx, -24 + lz));
        }

        // A scan reads each block of a column once, so a column scanned twice shows as a block read twice.
        Assert.Equal(reads.Count, reads.Distinct().Count());

        // The tile, plus one row and one column on its north and west sides.
        Assert.Equal((Size + 1) * (Size + 1), reads.Select(r => (r.X, r.Z)).Distinct().Count());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-8, -16)]
    [InlineData(-100, 36)]
    public void Relief_Should_MatchTheReliefOfTheWholeField_When_EachTileIsShadedOnItsOwn(int fieldX, int fieldZ)
    {
        // A field three tiles wide and deep with nothing around it: the tiles on its edge have empty
        // neighbours on the outside, the one in the middle has none.
        int HeightAt(int x, int z) =>
            x < fieldX || x >= fieldX + (3 * Size) || z < fieldZ || z >= fieldZ + (3 * Size) ? 0 : Holey(x, z);

        foreach (var (tileX, tileZ) in Grid(3))
        {
            int originX = fieldX + (tileX * Size);
            int originZ = fieldZ + (tileZ * Size);
            var tile = new ScannedTile(Size, originX, originZ, Top, skipCeiling: false, Columns(HeightAt));

            foreach (var (lx, lz) in Grid(Size))
            {
                int found = tile.Scan(lx, lz);
                if (found != SurfaceScan.NotFound)
                {
                    Assert.Equal(WholeFieldRelief(HeightAt, originX + lx, originZ + lz), tile.Relief(lx, lz, found));
                }
            }
        }
    }

    // A roofed cavern column: floor up to y=4, air 5..19, roof 20..22.
    private static int Cavern(int y) => y <= 4 || (y >= 20 && y <= 22) ? 7 : 0;

    // The blocks a tile reads: solid up to the height the field gives a column, air above, so that a
    // scan finds exactly that height. A height of 0 leaves the column empty.
    private static Func<int, int, int, int> Columns(Func<int, int, int> heightAt) =>
        (x, y, z) => y <= heightAt(x, z) ? 1 : 0;

    private static Func<int, int, int, int> Recording(List<(int X, int Y, int Z)> reads, Func<int, int, int, int> inner) =>
        (x, y, z) =>
        {
            reads.Add((x, y, z));
            return inner(x, y, z);
        };

    // Every (x, z) pair of an edge by edge square, row by row.
    private static IEnumerable<(int X, int Z)> Grid(int edge) =>
        from z in Enumerable.Range(0, edge)
        from x in Enumerable.Range(0, edge)
        select (x, z);

    // The relief of a column from the height field alone, no tile involved: the vanilla rule on the
    // heights around it, a neighbour with nothing to draw counting as the column's own height.
    private static float WholeFieldRelief(Func<int, int, int> heightAt, int x, int z)
    {
        int height = heightAt(x, z);
        int Above(int neighbourX, int neighbourZ) =>
            heightAt(neighbourX, neighbourZ) == 0 ? 0 : height - heightAt(neighbourX, neighbourZ);

        return Hillshade.Factor(Above(x - 1, z - 1), Above(x - 1, z), Above(x, z - 1));
    }

    // Deterministic pseudo-random heights, 1 to 6 so that the steps between neighbours vary in sign
    // and in steepness.
    private static int Bumpy(int x, int z) => 1 + (Hash(x, z) % 6);

    // The same, with about one column in eight holding nothing.
    private static int Holey(int x, int z) => Hash(x, z) % 8 == 0 ? 0 : Bumpy(x, z);

    private static int Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (z * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            return (int)((h ^ (h >> 16)) & 0x7FFFFFFF);
        }
    }
}
