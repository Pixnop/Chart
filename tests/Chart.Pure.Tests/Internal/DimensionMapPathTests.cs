using Chart.Internal;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class DimensionMapPathTests
{
    [Fact]
    public void Compose_Builds_Joined_Path()
    {
        var path = DimensionMapPath.Compose("/data/Chart", "save123", "mymod:vault");
        Assert.EndsWith("save123/mymod_vault.bin", path.Replace('\\', '/'));
    }

    [Fact]
    public void Compose_Sanitizes_Slashes_And_Colons_In_Dim_Code()
    {
        var path = DimensionMapPath.Compose("/data/Chart", "save", "weird/dim:code");
        Assert.DoesNotContain(":", System.IO.Path.GetFileName(path));
        Assert.DoesNotContain("/", System.IO.Path.GetFileNameWithoutExtension(path));
    }
}
