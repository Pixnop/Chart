using System;
using System.IO;
using Vintagestory.API.Client;

namespace Chart.Internal;

/// <summary>
/// Holds the in-memory <see cref="MapTileStore"/> for the active dimension and handles
/// loading/saving per-dimension tile files to disk.
/// </summary>
internal sealed class PerDimensionMapStore : IDisposable
{
    private readonly ICoreClientAPI _capi;

    private readonly string _root;

    private readonly string _savegameId;

    private MapTileStore _active = new();

    private string _activeDimCode = string.Empty;

    /// <param name="capi">Client API - used to resolve the data path and savegame identifier.</param>
    public PerDimensionMapStore(ICoreClientAPI capi)
    {
        ArgumentNullException.ThrowIfNull(capi);
        _capi = capi;
        _root = capi.GetOrCreateDataPath(Path.Combine("ModData", "Chart"));

        var id = capi.World.SavegameIdentifier;
        _savegameId = string.IsNullOrEmpty(id) ? capi.World.Seed.ToString() : id;
    }

    /// <summary>The active tile store for the current dimension.</summary>
    public MapTileStore Active => _active;

    /// <summary>Dimension code of the currently loaded store, or empty string before any load.</summary>
    public string CurrentDimCode => _activeDimCode;

    /// <summary>
    /// Switches the active store to <paramref name="dimCode"/>.
    /// Saves the current store first unless no dimension is loaded yet.
    /// No-op if <paramref name="dimCode"/> is already active.
    /// </summary>
    /// <param name="dimCode">Dimension code to load.</param>
    public void LoadFor(string dimCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(dimCode);

        if (_activeDimCode == dimCode)
        {
            return;
        }

        SaveCurrent();

        var path = DimensionMapPath.Compose(_root, _savegameId, dimCode);
        byte[]? data = null;
        if (File.Exists(path))
        {
            data = File.ReadAllBytes(path);
        }

        _active = MapTileStore.FromBytes(data);
        _activeDimCode = dimCode;

        _capi.Logger.Notification(
            "[Chart] Loaded dim '{0}' from '{1}' ({2} tiles).",
            dimCode,
            path,
            _active.Count);
    }

    /// <summary>
    /// Writes the active store to disk. No-op if no dimension is loaded yet.
    /// Creates parent directories as needed.
    /// </summary>
    public void SaveCurrent()
    {
        if (string.IsNullOrEmpty(_activeDimCode))
        {
            return;
        }

        var path = DimensionMapPath.Compose(_root, _savegameId, _activeDimCode);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(path, _active.ToBytes());
        _capi.Logger.Notification(
            "[Chart] Saved dim '{0}' to '{1}' ({2} tiles).",
            _activeDimCode,
            path,
            _active.Count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SaveCurrent();
    }
}
