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
    // CA1861: constant array arguments hoisted to static readonly fields.
    private static readonly string[] KeptDimOnly = { "mymod:kept" };
    private static readonly string[] KnownDims = { "mymod:dimA", "manifold:overworld" };
    private static readonly string[] AnyDim = { "anything" };

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

    [Fact]
    public void DeleteOrphans_Removes_Files_Whose_Dim_Is_Not_Known()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);

        // Three bin files on disk for three different dims.
        foreach (var dim in new[] { "mymod:kept", "mymod:orphan_a", "mymod:orphan_b" })
        {
            store.LoadFor(dim);
            store.Active.SetTile(0, 0, new byte[16]);
            store.SaveCurrent();
        }

        Assert.True(File.Exists(tmp.PathFor("mymod:kept")));
        Assert.True(File.Exists(tmp.PathFor("mymod:orphan_a")));
        Assert.True(File.Exists(tmp.PathFor("mymod:orphan_b")));

        int deleted = store.DeleteOrphans(KeptDimOnly);

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(tmp.PathFor("mymod:kept")));
        Assert.False(File.Exists(tmp.PathFor("mymod:orphan_a")));
        Assert.False(File.Exists(tmp.PathFor("mymod:orphan_b")));
    }

    [Fact]
    public void DeleteOrphans_Returns_Zero_When_All_Files_Match_Known_Dims()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);
        store.LoadFor("mymod:dimA");
        store.Active.SetTile(0, 0, new byte[16]);
        store.SaveCurrent();

        int deleted = store.DeleteOrphans(KnownDims);

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(tmp.PathFor("mymod:dimA")));
    }

    [Fact]
    public void DeleteOrphans_Returns_Zero_When_Cache_Dir_Does_Not_Exist()
    {
        using var tmp = new TempDataRoot();
        var store = new PerDimensionMapStore(tmp.Capi);

        // No LoadFor / SaveCurrent ever called, so the savegame subdir never gets created.
        int deleted = store.DeleteOrphans(AnyDim);

        Assert.Equal(0, deleted);
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
