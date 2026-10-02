namespace ChartFixture;

using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

/// <summary>
/// A roofed dimension: a granite floor (y 1..4), air, then a granite roof (y 20..22). The shape
/// a cavern dimension has, small enough to live in one chunk slice.
/// </summary>
internal sealed class CavernWorldgen : IWorldgenStrategy
{
    private int _graniteBlockId;

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        _graniteBlockId = ctx.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;
    }

    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        var pos = new BlockPos(0, 0, 0, ctx.DimensionId);
        for (int localX = 0; localX < 32; localX++)
        {
            for (int localZ = 0; localZ < 32; localZ++)
            {
                for (int y = 1; y <= 22; y++)
                {
                    if (y > 4 && y < 20)
                    {
                        continue;
                    }

                    pos.Set((ctx.ChunkX * 32) + localX, y, (ctx.ChunkZ * 32) + localZ);
                    ctx.BlockAccessor.SetBlock(_graniteBlockId, pos);
                }
            }
        }
    }
}
