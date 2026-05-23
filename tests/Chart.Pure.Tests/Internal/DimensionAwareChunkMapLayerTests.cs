using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class DimensionAwareChunkMapLayerTests
{
    [Fact]
    public void AbgrToPackedRgba_Should_Extract_Red_Correctly()
    {
        // Pure red: r=0xFF, g=0x00, b=0x00, a=0xFF - ABGR = 0xFF0000FF
        int abgr = unchecked((int)0xFF0000FF);
        uint result = ColorConvert.AbgrToPackedRgba(abgr);

        Assert.Equal(0xFF, (int)((result >> 24) & 0xFF)); // R
        Assert.Equal(0x00, (int)((result >> 16) & 0xFF)); // G
        Assert.Equal(0x00, (int)((result >> 8) & 0xFF));  // B
        Assert.Equal(0xFF, (int)(result & 0xFF));          // A
    }

    [Fact]
    public void AbgrToPackedRgba_Should_Extract_Blue_Correctly()
    {
        // Pure blue: r=0x00, g=0x00, b=0xFF, a=0xFF - ABGR = 0xFFFF0000
        int abgr = unchecked((int)0xFFFF0000);
        uint result = ColorConvert.AbgrToPackedRgba(abgr);

        Assert.Equal(0x00, (int)((result >> 24) & 0xFF)); // R
        Assert.Equal(0x00, (int)((result >> 16) & 0xFF)); // G
        Assert.Equal(0xFF, (int)((result >> 8) & 0xFF));  // B
        Assert.Equal(0xFF, (int)(result & 0xFF));          // A
    }

    [Fact]
    public void AbgrToPackedRgba_Should_Force_Alpha_Opaque()
    {
        // Semi-transparent grey, a=0x40 - alpha must be forced to 0xFF in output.
        int abgr = 0x40808080;
        uint result = ColorConvert.AbgrToPackedRgba(abgr);

        Assert.Equal(0xFF, (int)(result & 0xFF));
    }
}
