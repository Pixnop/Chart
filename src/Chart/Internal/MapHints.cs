using Manifold.Api;

namespace Chart.Internal;

/// <summary>
/// Hints a dimension's owner mod gives Chart through Manifold metadata
/// (<c>IDimensionBuilder.WithMetadata</c>), replicated to clients since Manifold 0.6.0.
/// </summary>
internal static class MapHints
{
    /// <summary>
    /// Integer Y the map's surface scan starts at. A roofed dimension sets it to its ceiling
    /// (or just under it) so the map shows the cavern floor instead of the roof.
    /// </summary>
    public const string ScanTopKey = "chart:scanTopY";

    /// <summary>The key Rift Traveler shipped before <see cref="ScanTopKey"/> was defined.</summary>
    public const string LegacyScanTopKey = "chartScanTopY";

    /// <summary>
    /// The scan top a dimension declares, or null when it declares none (or a value that is not
    /// a positive integer).
    /// </summary>
    public static int? ScanTopY(IDimension? dimension)
    {
        if (dimension is null)
        {
            return null;
        }

        if (!dimension.Metadata.TryGetValue(ScanTopKey, out object? value)
            && !dimension.Metadata.TryGetValue(LegacyScanTopKey, out value))
        {
            return null;
        }

        long? y = value switch
        {
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            _ => null,
        };
        return y is > 0 and <= int.MaxValue ? (int)y : null;
    }
}
