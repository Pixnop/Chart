namespace ChartFixture;

using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

/// <summary>Fills y 1..4 of every column with granite. Deterministic by construction.</summary>
internal sealed class SlabWorldgen : IWorldgenStrategy
{
    private int _graniteBlockId;

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        _graniteBlockId = ctx.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))!.BlockId;
    }

    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        for (int localX = 0; localX < 32; localX++)
        {
            for (int localZ = 0; localZ < 32; localZ++)
            {
                for (int y = 1; y <= 4; y++)
                {
                    var pos = new BlockPos(
                        (ctx.ChunkX * 32) + localX,
                        y,
                        (ctx.ChunkZ * 32) + localZ,
                        ctx.DimensionId);
                    ctx.BlockAccessor.SetBlock(_graniteBlockId, pos);
                }
            }
        }
    }
}
