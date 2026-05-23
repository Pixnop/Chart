using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Chart.Internal;

/// <summary>
/// One dimension's collection of RGBA tiles, keyed by (chunkX, chunkZ). Serialised to a
/// deflate-compressed binary stream (header + variable count of fixed-size tiles).
/// </summary>
/// <remarks>In-memory only; <see cref="PerDimensionMapStore"/> handles disk IO.</remarks>
internal sealed class MapTileStore
{
    private const uint Magic = 0x43485254u; // 'CHRT' big-endian

    private const int Version = 1;

    private readonly Dictionary<(int Cx, int Cz), byte[]> _tiles = new();

    /// <summary>Whether a tile exists for the given chunk coordinates.</summary>
    /// <param name="cx">Chunk X.</param>
    /// <param name="cz">Chunk Z.</param>
    /// <returns>True if a tile is stored.</returns>
    public bool HasTile(int cx, int cz) => _tiles.ContainsKey((cx, cz));

    /// <summary>Returns the tile for the given chunk, or null if absent.</summary>
    /// <param name="cx">Chunk X.</param>
    /// <param name="cz">Chunk Z.</param>
    /// <returns>The tile bytes or null.</returns>
    public byte[]? GetTile(int cx, int cz) => _tiles.TryGetValue((cx, cz), out var t) ? t : null;

    /// <summary>Stores a tile for the given chunk, overwriting any previous one.</summary>
    /// <param name="cx">Chunk X.</param>
    /// <param name="cz">Chunk Z.</param>
    /// <param name="tile">Tile bytes (must be <see cref="ChunkSampler.TileBytes"/> long).</param>
    public void SetTile(int cx, int cz, byte[] tile) => _tiles[(cx, cz)] = tile;

    /// <summary>Serialises the store to a deflate-compressed byte stream.</summary>
    /// <returns>The serialised bytes.</returns>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            using var w = new BinaryWriter(deflate);
            w.Write(Magic);
            w.Write(Version);
            w.Write(_tiles.Count);
            foreach (var ((cx, cz), tile) in _tiles)
            {
                w.Write(cx);
                w.Write(cz);
                w.Write(tile);
            }
        }

        return ms.ToArray();
    }

    /// <summary>Deserialises a store. Returns an empty store on null, empty or corrupt input.</summary>
    /// <param name="data">Bytes previously produced by <see cref="ToBytes"/>, or null.</param>
    /// <returns>A store with the recovered tiles, or empty.</returns>
    public static MapTileStore FromBytes(byte[]? data)
    {
        var store = new MapTileStore();
        if (data is not { Length: > 0 })
        {
            return store;
        }

        try
        {
            using var ms = new MemoryStream(data);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            using var r = new BinaryReader(deflate);
            if (r.ReadUInt32() != Magic)
            {
                return new MapTileStore();
            }

            int version = r.ReadInt32();
            if (version != Version)
            {
                return new MapTileStore();
            }

            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int cx = r.ReadInt32();
                int cz = r.ReadInt32();
                var tile = r.ReadBytes(ChunkSampler.TileBytes);
                if (tile.Length == ChunkSampler.TileBytes)
                {
                    store._tiles[(cx, cz)] = tile;
                }
            }
        }
        catch
        {
            return new MapTileStore();
        }

        return store;
    }
}
