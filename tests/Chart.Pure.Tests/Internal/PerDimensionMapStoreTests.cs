using System;
using System.IO;
using Chart.Internal;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace Chart.Pure.Tests.Internal;

public sealed class PerDimensionMapStoreTests
{
    [Fact]
    public void DeleteFor_Removes_The_Bin_File_For_The_Given_Dim()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);

        // Make sure a file exists on disk for the dim we are about to delete.
        store.LoadFor("mymod:vault");
        store.Active.SetTile(0, 0, new byte[16]);
        store.SaveCurrent();
        Assert.True(File.Exists(tmp.PathFor("mymod:vault")));

        store.DeleteFor("mymod:vault");

        Assert.False(File.Exists(tmp.PathFor("mymod:vault")));
    }

    [Fact]
    public void DeleteFor_Resets_Active_Store_When_The_Dim_Is_Currently_Active()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);

        store.LoadFor("mymod:active");
        store.Active.SetTile(0, 0, new byte[16]);
        Assert.Equal("mymod:active", store.CurrentDimCode);
        Assert.Equal(1, store.Active.Count);

        store.DeleteFor("mymod:active");

        Assert.Equal(string.Empty, store.CurrentDimCode);
        Assert.Equal(0, store.Active.Count);
    }

    [Fact]
    public void DeleteFor_Leaves_Active_Store_Untouched_When_Deleting_A_Different_Dim()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);

        // Build a file for a non-active dim by loading it, saving, then swapping to a different dim.
        store.LoadFor("mymod:other");
        store.Active.SetTile(0, 0, new byte[16]);
        store.SaveCurrent();
        store.LoadFor("mymod:active");
        store.Active.SetTile(0, 0, new byte[16]);
        Assert.Equal("mymod:active", store.CurrentDimCode);

        store.DeleteFor("mymod:other");

        Assert.Equal("mymod:active", store.CurrentDimCode);
        Assert.Equal(1, store.Active.Count);
        Assert.False(File.Exists(tmp.PathFor("mymod:other")));
    }

    [Fact]
    public void DeleteFor_Is_NoOp_When_The_Bin_File_Does_Not_Exist()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);

        // Should not throw; nothing on disk yet.
        store.DeleteFor("mymod:never-saved");

        Assert.False(File.Exists(tmp.PathFor("mymod:never-saved")));
    }

    private sealed class TempDataRoot : IDisposable
    {
        public TempDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "Chart-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);

            Capi = Substitute.For<ICoreClientAPI>();
            Capi.GetOrCreateDataPath(Arg.Any<string>())
                .Returns(call =>
                {
                    var sub = Path.Combine(Root, call.Arg<string>());
                    Directory.CreateDirectory(sub);
                    return sub;
                });
            Capi.World.SavegameIdentifier.Returns("test-save");
        }

        public string Root { get; }

        public ICoreClientAPI Capi { get; }

        public string DataRoot => Path.Combine(Root, Path.Combine("ModData", "Chart"));

        public string PathFor(string dimCode) =>
            DimensionMapPath.Compose(DataRoot, "test-save", dimCode);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best effort - leave the temp dir if it has been locked by another process.
            }
        }
    }
}
