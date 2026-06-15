using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Practical HA P1.4: contract tests for <see cref="IArtifactStorage"/>, exercised against the
/// in-memory reference implementation. The filesystem providers (P1.5) must satisfy the same contract.
/// </summary>
public class InMemoryArtifactStorageTests
{
    private static InMemoryArtifactStorage New() => new();

    [Fact]
    public async Task WriteThenRead_RoundTripsText()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "reports/daily.etlsql", "SELECT 1;");

        Assert.True(await s.ExistsAsync(ArtifactArea.Scripts, "reports/daily.etlsql"));
        Assert.Equal("SELECT 1;", await s.ReadAllTextAsync(ArtifactArea.Scripts, "reports/daily.etlsql"));
    }

    [Fact]
    public async Task SeparatorsAreNormalized_BackslashEqualsForwardSlash()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "a\\b\\c.etlsql", "x");

        Assert.True(await s.ExistsAsync(ArtifactArea.Scripts, "a/b/c.etlsql"));
        var info = await s.GetInfoAsync(ArtifactArea.Scripts, "a/b/c.etlsql");
        Assert.Equal("a/b/c.etlsql", info!.Value.Path);
    }

    [Fact]
    public async Task Areas_AreIsolated_SamePathDoesNotCollide()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "x.dat", "script");
        await s.WriteAllTextAsync(ArtifactArea.Maps, "x.dat", "map");

        Assert.Equal("script", await s.ReadAllTextAsync(ArtifactArea.Scripts, "x.dat"));
        Assert.Equal("map", await s.ReadAllTextAsync(ArtifactArea.Maps, "x.dat"));
        Assert.False(await s.ExistsAsync(ArtifactArea.Snapshots, "x.dat"));
    }

    [Fact]
    public async Task Write_NoOverwrite_ThrowsAndPreservesExisting()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet", "original");

        await Assert.ThrowsAsync<IOException>(() =>
            s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet", "replacement", overwrite: false));

        Assert.Equal("original", await s.ReadAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet"));
    }

    [Fact]
    public async Task GetInfo_ReportsLength_AndNullWhenAbsent()
    {
        var s = New();
        await s.WriteAllBytesAsync(ArtifactArea.Snapshots, "snap.html", Encoding.UTF8.GetBytes("hello"));

        var info = await s.GetInfoAsync(ArtifactArea.Snapshots, "snap.html");
        Assert.Equal(5, info!.Value.Length);
        Assert.Null(await s.GetInfoAsync(ArtifactArea.Snapshots, "missing.html"));
    }

    [Fact]
    public async Task Enumerate_RespectsPrefixAndRecursion()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "team/a.etlsql", "1");
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "team/sub/b.etlsql", "2");
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "other/c.etlsql", "3");

        var recursive = await ToPaths(s.EnumerateAsync(ArtifactArea.Scripts, "team", recursive: true));
        Assert.Equal(new[] { "team/a.etlsql", "team/sub/b.etlsql" }, recursive.OrderBy(p => p));

        var shallow = await ToPaths(s.EnumerateAsync(ArtifactArea.Scripts, "team", recursive: false));
        Assert.Equal(new[] { "team/a.etlsql" }, shallow);
    }

    [Fact]
    public async Task Move_RenamesAndHonorsOverwrite()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, ".tmp-staging", "payload");

        await s.MoveAsync(ArtifactArea.Datasets, ".tmp-staging", "final_1.parquet");
        Assert.False(await s.ExistsAsync(ArtifactArea.Datasets, ".tmp-staging"));
        Assert.Equal("payload", await s.ReadAllTextAsync(ArtifactArea.Datasets, "final_1.parquet"));

        await s.WriteAllTextAsync(ArtifactArea.Datasets, ".tmp-staging2", "newer");
        await Assert.ThrowsAsync<IOException>(() =>
            s.MoveAsync(ArtifactArea.Datasets, ".tmp-staging2", "final_1.parquet", overwrite: false));
        await s.MoveAsync(ArtifactArea.Datasets, ".tmp-staging2", "final_1.parquet", overwrite: true);
        Assert.Equal("newer", await s.ReadAllTextAsync(ArtifactArea.Datasets, "final_1.parquet"));
    }

    [Fact]
    public async Task Delete_ReturnsWhetherSomethingWasRemoved()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Maps, "lookup.csv", "k,v");

        Assert.True(await s.DeleteAsync(ArtifactArea.Maps, "lookup.csv"));
        Assert.False(await s.DeleteAsync(ArtifactArea.Maps, "lookup.csv"));
    }

    [Fact]
    public async Task LeaseLocalCopy_ExposesReadablePath_ThenCleansUp()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_2.parquet", "bytes");

        string leasedPath;
        await using (var lease = await s.LeaseLocalCopyAsync(ArtifactArea.Datasets, "cache_2.parquet"))
        {
            leasedPath = lease.LocalPath;
            Assert.Equal("bytes", await File.ReadAllTextAsync(leasedPath));
        }
        Assert.False(File.Exists(leasedPath)); // disposed lease removes the temp copy
    }

    [Fact]
    public async Task Keys_CannotBeLeasedToDisk()
    {
        var s = New();
        await s.WriteAllBytesAsync(ArtifactArea.Keys, "atrest.key", new byte[] { 1, 2, 3 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.LeaseLocalCopyAsync(ArtifactArea.Keys, "atrest.key"));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../b.txt")]
    [InlineData("")]
    public async Task TraversalAndEmptyPaths_AreRejected(string path)
    {
        var s = New();
        await Assert.ThrowsAsync<ArgumentException>(() => s.WriteAllTextAsync(ArtifactArea.Scripts, path, "x"));
    }

    [Fact]
    public async Task AbsolutePaths_AreRejected()
    {
        var s = New();
        var rooted = OperatingSystem.IsWindows() ? @"C:\evil.txt" : "/etc/evil.txt";
        await Assert.ThrowsAsync<ArgumentException>(() => s.WriteAllTextAsync(ArtifactArea.Scripts, rooted, "x"));
    }

    private static async Task<string[]> ToPaths(IAsyncEnumerable<ArtifactInfo> items)
    {
        var list = new System.Collections.Generic.List<string>();
        await foreach (var i in items) list.Add(i.Path);
        return list.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
}
