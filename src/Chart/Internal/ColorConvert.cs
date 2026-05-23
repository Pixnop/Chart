namespace Chart.Internal;

/// <summary>
/// Colour-channel conversion helpers used by the Chart map layer.
/// </summary>
internal static class ColorConvert
{
    /// <summary>
    /// Converts a VS ABGR-packed int (the layout returned by <c>Block.GetColor</c> and
    /// <c>ColorUtil.ColorFromRgba</c>) to the <c>0xRRGGBBAA</c> uint that
    /// <see cref="ChunkSampler"/> expects from its palette delegate.
    ///
    /// VS stores colour as <c>(a&lt;&lt;24)|(b&lt;&lt;16)|(g&lt;&lt;8)|r</c> (low byte = R).
    /// ChunkSampler expects <c>(r&lt;&lt;24)|(g&lt;&lt;16)|(b&lt;&lt;8)|a</c>.
    /// Alpha is always forced to 0xFF (fully opaque) for map tiles.
    /// </summary>
    /// <param name="abgr">ABGR-packed int from Block.GetColor.</param>
    /// <returns>0xRRGGBBAA uint suitable for ChunkSampler.</returns>
    internal static uint AbgrToPackedRgba(int abgr)
    {
        int r = abgr & 0xFF;
        int g = (abgr >> 8) & 0xFF;
        int b = (abgr >> 16) & 0xFF;

        // Alpha is always 0xFF - map tiles are fully opaque.
        return (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);
    }
}
