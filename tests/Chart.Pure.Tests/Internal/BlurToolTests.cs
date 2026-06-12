using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class BlurToolTests
{
    [Fact]
    public void Blur_Radius1_Row_Averages_InBounds_Samples()
    {
        // Single row [0, 90, 0], radius 1. Edges average 2 samples, middle averages 3.
        // Vertical pass on a height-1 image is a no-op (only ny == y is in bounds).
        var pixels = new byte[] { 0, 90, 0 };
        BlurTool.Blur(pixels, width: 3, height: 1, radius: 1);
        Assert.Equal(new byte[] { 45, 30, 45 }, pixels);
    }

    [Fact]
    public void Blur_Radius1_Column_Averages_InBounds_Samples()
    {
        // Single column [0, 90, 0] (width 1, height 3) - symmetric to the row case.
        var pixels = new byte[] { 0, 90, 0 };
        BlurTool.Blur(pixels, width: 1, height: 3, radius: 1);
        Assert.Equal(new byte[] { 45, 30, 45 }, pixels);
    }

    [Fact]
    public void Blur_Uniform_Image_Is_Unchanged()
    {
        var pixels = new byte[9];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 100;
        }

        BlurTool.Blur(pixels, width: 3, height: 3, radius: 1);
        Assert.All(pixels, b => Assert.Equal(100, b));
    }
}
