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

        // Horizontal pass: blur each row.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                int count = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int nx = x + k;
                    if (nx < 0 || nx >= width)
                    {
                        continue;
                    }

                    sum += pixels[(y * width) + nx];
                    count++;
                }

                tmp[(y * width) + x] = (byte)(sum / count);
            }
        }

        // Vertical pass: blur each column of the horizontally-blurred result.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                int count = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int ny = y + k;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    sum += tmp[(ny * width) + x];
                    count++;
                }

                pixels[(y * width) + x] = (byte)(sum / count);
            }
        }
    }
}
