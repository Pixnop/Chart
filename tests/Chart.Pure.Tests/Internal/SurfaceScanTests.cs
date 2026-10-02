using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class SurfaceScanTests
{
    [Fact]
    public void Find_Should_ReturnTheRoof_When_CeilingIsNotSkipped()
    {
        Assert.Equal(112, SurfaceScan.Find(Cavern, scanTop: 128, skipCeiling: false));
    }

    [Theory]
    [InlineData(112)] // declared on the roof's top block
    [InlineData(111)] // declared inside the roof, what Rift Traveler ships
    [InlineData(109)] // declared just under the roof
    public void Find_Should_ReturnTheCavernFloor_When_ScanStartsAtOrInsideTheCeiling(int scanTop)
    {
        Assert.Equal(40, SurfaceScan.Find(Cavern, scanTop, skipCeiling: true));
    }

    [Fact]
    public void Find_Should_ReturnThePillarTop_When_APillarStopsShortOfTheRoof()
    {
        static int Pillar(int y) => y <= 108 || y >= 110 ? 7 : 0;

        Assert.Equal(108, SurfaceScan.Find(Pillar, scanTop: 111, skipCeiling: true));
    }

    [Fact]
    public void Find_Should_ReturnTheScanTop_When_TheColumnIsSolidAllTheWayDown()
    {
        Assert.Equal(111, SurfaceScan.Find(_ => 7, scanTop: 111, skipCeiling: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Find_Should_ReturnNotFound_When_TheColumnIsEmpty(bool skipCeiling)
    {
        Assert.Equal(SurfaceScan.NotFound, SurfaceScan.Find(_ => 0, scanTop: 128, skipCeiling));
    }

    [Fact]
    public void Find_Should_ReturnTheRoof_When_TheScanStartsInTheAirAboveIt()
    {
        Assert.Equal(112, SurfaceScan.Find(Cavern, scanTop: 128, skipCeiling: true));
    }

    [Fact]
    public void Find_Should_ReturnNotFound_When_NothingLiesUnderTheCeiling()
    {
        static int RoofOverVoid(int y) => y >= 110 && y <= 112 ? 7 : 0;

        Assert.Equal(SurfaceScan.NotFound, SurfaceScan.Find(RoofOverVoid, scanTop: 111, skipCeiling: true));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(-5, true)]
    [InlineData(-5, false)]
    public void Find_Should_ReturnNotFoundWithoutReading_When_TheScanTopIsNotAboveZero(int scanTop, bool skipCeiling)
    {
        int reads = 0;

        int found = SurfaceScan.Find(_ => { reads++; return 7; }, scanTop, skipCeiling);

        Assert.Equal(SurfaceScan.NotFound, found);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData(111)] // starts inside the roof
    [InlineData(109)] // starts in the air under it
    public void Find_Should_ReadEachBlockOnce(int scanTop)
    {
        var read = new List<int>();

        SurfaceScan.Find(y => { read.Add(y); return Cavern(y); }, scanTop, skipCeiling: true);

        Assert.Equal(read.Distinct().Count(), read.Count);
    }

    [Fact]
    public void Find_Should_NeverReadBelowYOne()
    {
        int lowest = int.MaxValue;
        SurfaceScan.Find(y => { lowest = Math.Min(lowest, y); return 0; }, scanTop: 16, skipCeiling: true);

        Assert.Equal(1, lowest);
    }

    // A roofed cavern column: floor up to y=40, air 41..109, roof 110..112.
    private static int Cavern(int y) => y <= 40 || (y >= 110 && y <= 112) ? 7 : 0;
}
