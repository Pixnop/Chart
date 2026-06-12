using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Chart.Internal;

/// <summary>
/// Replicates the block-to-colour palette that vanilla <c>ChunkMapLayer</c> builds in its
/// <c>OnLoaded</c> method. All colours are stored as BGRA ints (the layout produced by
/// <c>ColorUtil.ReverseColorBytes(ColorUtil.Hex2Int(hex))</c>, i.e. low byte = B).
/// This matches what <c>ColorUtil.ColorMultiply3Clamped</c> expects.
/// </summary>
internal sealed class VanillaMapPalette
{
    // ---------- Palette colour codes and hex values ----------
    // Source: ChunkMapLayer.hexColorsByCode (public static field, VSEssentials).
    // 13 named codes. Order matches the OrderedDictionary so that palette indices are stable.
    private static readonly string[] PaletteCodes =
    {
        "ink",          // 0
        "settlement",   // 1
        "wateredge",    // 2
        "land",         // 3
        "desert",       // 4
        "forest",       // 5
        "road",         // 6
        "plant",        // 7
        "lake",         // 8
        "lava",         // 9
        "ocean",        // 10
        "glacier",      // 11
        "devastation",  // 12
    };

    private static readonly string[] PaletteHex =
    {
        "#483018",  // ink
        "#856844",  // settlement
        "#483018",  // wateredge  (same as ink - dark shoreline)
        "#AC8858",  // land
        "#C4A468",  // desert
        "#98844C",  // forest
        "#805030",  // road
        "#808650",  // plant
        "#CCC890",  // lake
        "#CCC890",  // lava       (same as lake - placeholder)
        "#CCC890",  // ocean      (same as lake)
        "#E0E0C0",  // glacier
        "#755c3c",  // devastation
    };

    // ---------- Material -> code fallback table ----------
    // Source: ChunkMapLayer.defaultMapColorCodes (public static field, VSEssentials).
    private static readonly Dictionary<EnumBlockMaterial, string> DefaultMaterialCodes =
        new()
        {
            { EnumBlockMaterial.Soil, "land" },
            { EnumBlockMaterial.Sand, "desert" },
            { EnumBlockMaterial.Ore, "land" },
            { EnumBlockMaterial.Gravel, "desert" },
            { EnumBlockMaterial.Stone, "land" },
            { EnumBlockMaterial.Leaves, "forest" },
            { EnumBlockMaterial.Plant, "plant" },
            { EnumBlockMaterial.Wood, "forest" },
            { EnumBlockMaterial.Snow, "glacier" },
            { EnumBlockMaterial.Water, "lake" },
            { EnumBlockMaterial.Ice, "glacier" },
            { EnumBlockMaterial.Lava, "lava" },
        };

    // ---------- Runtime lookups ----------

    /// <summary>
    /// Maps block id -> palette index (byte). Index into <see cref="Colors"/>.
    /// Length = number of registered blocks.
    /// </summary>
    public byte[] Block2Color { get; private set; } = System.Array.Empty<byte>();

    /// <summary>
    /// Flat palette: palette index -> BGRA int.
    /// Length = <see cref="PaletteCodes"/>.Length.
    /// Low byte is B (not R) so <c>ColorUtil.ColorMultiply3Clamped</c> works correctly.
    /// </summary>
    public int[] Colors { get; private set; } = System.Array.Empty<int>();

    // ---------- Well-known palette indices (precomputed for hot paths) ----------

    /// <summary>Palette index for the "wateredge" code (used for shoreline pixels).</summary>
    public int WaterEdgeColor { get; private set; }

    // ---------- Construction ----------

    /// <summary>
    /// Builds the palette. Must be called once, after all blocks are registered
    /// (e.g. from <c>MapLayer.OnLoaded</c>).
    /// </summary>
    /// <param name="blocks">The block registry from <c>api.World.Blocks</c>.</param>
    public void Build(System.Collections.Generic.IList<Block> blocks)
    {
        // Step 1: convert hex strings to BGRA ints.
        // ColorUtil.ReverseColorBytes swaps R and B, giving BGRA layout (low byte = B).
        Colors = new int[PaletteCodes.Length];
        var codeToIndex = new Dictionary<string, int>(PaletteCodes.Length);
        for (int i = 0; i < PaletteCodes.Length; i++)
        {
            int rgba = ColorUtil.Hex2Int(PaletteHex[i]);
            Colors[i] = ColorUtil.ReverseColorBytes(rgba);
            codeToIndex[PaletteCodes[i]] = i;
        }

        // Precompute well-known colours.
        WaterEdgeColor = Colors[codeToIndex["wateredge"]];

        // Step 2: for every block, determine which palette index it should use.
        Block2Color = new byte[blocks.Count];
        for (int id = 0; id < Block2Color.Length; id++)
        {
            var block = blocks[id];
            string code = "land";
            if (block?.Attributes != null)
            {
                string? mapCode = block.Attributes["mapColorCode"].AsString();
                code = mapCode
                    ?? (DefaultMaterialCodes.TryGetValue(block.BlockMaterial, out string? matCode) ? matCode : "land");
            }

            Block2Color[id] = (byte)(codeToIndex.TryGetValue(code, out int idx) ? idx : codeToIndex["land"]);
        }
    }

    /// <summary>
    /// Returns the BGRA colour int for the given block id.
    /// Falls back to the "land" colour (index 3) if the id is out of range.
    /// </summary>
    public int GetColor(int blockId)
    {
        if ((uint)blockId < (uint)Block2Color.Length)
        {
            return Colors[Block2Color[blockId]];
        }

        return Colors[3]; // land fallback
    }

    /// <summary>
    /// Returns true if the block material indicates a lake (water or non-glacier ice).
    /// Matches the vanilla <c>isLake</c> helper in ChunkMapLayer.
    /// </summary>
    public static bool IsLake(Block block) =>
        block.BlockMaterial == EnumBlockMaterial.Water ||
        (block.BlockMaterial == EnumBlockMaterial.Ice && block.Code.Path != "glacierice");
}
