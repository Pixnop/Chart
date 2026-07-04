namespace Chart.Scenarios;

using System.Text;
using Atlas.XUnit;

/// <summary>
/// Shared helpers for reading the results the chartfixture mod publishes through
/// SaveGame data. Command outcomes come back directly from ExecuteCommand's
/// CommandResult; this side channel carries boot-time state (dimension ids and
/// codes) and events (dimension destroyed), which cannot cross the assembly
/// identity boundary any other way.
/// </summary>
public abstract class ChartScenarioBase : AtlasScenarioBase
{
    protected async Task<int> DimensionId(string path)
    {
        await World.Until(() => ReadDimensionId(path) is not null, timeoutTicks: 200);
        return ReadDimensionId(path)!.Value;
    }

    protected int? ReadDimensionId(string path)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("chartfixture:dimid:" + path);
        return data is null ? null : BitConverter.ToInt32(data, 0);
    }

    protected string? ReadDimensionCode(string path)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData("chartfixture:dimcode:" + path);
        return data is null ? null : Encoding.UTF8.GetString(data);
    }

    protected bool FlagIsSet(string key)
    {
        byte[]? data = World.Api.WorldManager.SaveGame.GetData(key);
        return data is { Length: > 0 } && data[0] == 1;
    }
}
