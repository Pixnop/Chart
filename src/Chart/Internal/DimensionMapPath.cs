using System;
using System.IO;
using System.Linq;

namespace Chart.Internal;

internal static class DimensionMapPath
{
    private static readonly char[] Reserved = { ':', '/', '\\', '?', '*', '<', '>', '|', '"' };

    /// <summary>
    /// Composes the full file path for a dimension's tile store.
    /// </summary>
    /// <param name="root">Root data directory, e.g. the Chart ModData folder.</param>
    /// <param name="savegameId">Savegame identifier (used as a subfolder name).</param>
    /// <param name="dimCode">Dimension code string; reserved characters are replaced with underscores.</param>
    /// <returns>An absolute path of the form <c>root/savegameId/dimCode.bin</c>.</returns>
    public static string Compose(string root, string savegameId, string dimCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(savegameId);
        ArgumentException.ThrowIfNullOrEmpty(dimCode);

        string safe = new string(dimCode.Select(c => Reserved.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(root, Sanitize(savegameId), safe + ".bin");
    }

    private static string Sanitize(string s) =>
        new string(s.Select(c => Reserved.Contains(c) ? '_' : c).ToArray());
}
