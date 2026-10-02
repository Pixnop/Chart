using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Chart.Internal;

/// <summary>
/// What counts as the surface of a scanned column: the rule the engine builds its rain height map
/// with, so that a custom dimension is mapped like the overworld.
/// </summary>
internal static class SurfaceRule
{
    /// <summary>
    /// The block a column scan sees at <paramref name="pos"/>. A fluid counts before the solid block
    /// it shares the position with, and a block that lets rain through (tall grass, a torch, a sign)
    /// is not there at all.
    /// </summary>
    /// <param name="accessor">The block accessor of the client or of the server.</param>
    /// <param name="pos">The position to read, with its dimension.</param>
    /// <param name="air">The air block, returned where nothing counts.</param>
    public static Block BlockAt(IBlockAccessor accessor, BlockPos pos, Block air)
    {
        var block = accessor.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
        return block is null || block.RainPermeable ? air : block;
    }
}
