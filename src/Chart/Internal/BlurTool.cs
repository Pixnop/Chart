namespace Chart.Internal;

/// <summary>
/// Separable box-blur applied to a flat byte array. Ported from
/// Vintagestory.GameContent.BlurTool (VSEssentials.dll, internal class).
/// Used to smooth the shadow map produced by the hillshading pass before
/// it is combined with the raw shadow values and applied to tile colours.
/// </summary>
internal static class BlurTool
{
    /// <summary>
    /// Applies a separable box blur of the given <paramref name="radius"/> to
    /// <paramref name="pixels"/> in place. Pixels are single bytes (one byte per
    /// pixel, NOT 4-byte RGBA). The array must be exactly
    /// <paramref name="width"/> * <paramref name="height"/> elements long.
    /// </summary>
    /// <param name="pixels">Input/output byte array (1 byte per pixel).</param>
    /// <param name="width">Width of the image in pixels.</param>
    /// <param name="height">Height of the image in pixels.</param>
    /// <param name="radius">Blur radius (kernel half-width). Vanilla uses 2.</param>
    public static void Blur(byte[] pixels, int width, int height, int radius)
    {
        var tmp = new byte[pixels.Length];

        // Two separable passes: rows into tmp, then columns of tmp back into pixels.
        BlurAxis(pixels, tmp, width, height, radius, horizontal: true);
        BlurAxis(tmp, pixels, width, height, radius, horizontal: false);
    }

    /// <summary>
    /// Blurs <paramref name="src"/> into <paramref name="dst"/> along one axis (rows when
    /// <paramref name="horizontal"/> is true, columns otherwise), averaging the in-bounds
    /// samples within <paramref name="radius"/>.
    /// </summary>
    private static void BlurAxis(byte[] src, byte[] dst, int width, int height, int radius, bool horizontal)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                int count = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = horizontal ? x + k : x;
                    int sy = horizontal ? y : y + k;
                    if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                    {
                        continue;
                    }

                    sum += src[(sy * width) + sx];
                    count++;
                }

                dst[(y * width) + x] = (byte)(sum / count);
            }
        }
    }
}
