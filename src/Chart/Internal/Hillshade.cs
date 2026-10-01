using System;

namespace Chart.Internal;

/// <summary>The vanilla map's relief shading for one pixel, from its height above three neighbours.</summary>
internal static class Hillshade
{
    /// <summary>
    /// Brightness factor of a pixel given how far it stands above (positive) or below (negative)
    /// its north-west, west and north neighbours. 1 is flat; a pixel above its neighbours is
    /// lit, one below is shaded, more so as the step gets steeper.
    /// </summary>
    public static float Factor(int northWest, int west, int north)
    {
        float slopedir = Math.Sign(northWest) + Math.Sign(west) + Math.Sign(north);
        float steepness = Math.Max(Math.Max(Math.Abs(northWest), Math.Abs(west)), Math.Abs(north));
        float magnitude = Math.Min(0.3f, steepness / 12f) / 1.25f;

        if (slopedir > 0f)
        {
            return 1.08f + magnitude;
        }

        return slopedir < 0f ? 0.92f - magnitude : 1f;
    }
}
