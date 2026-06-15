using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Practical HA P1.4/P1.5: the shared <see cref="IArtifactStorage"/> contract, run against every
/// implementation (the in-memory reference and the filesystem providers) so they all behave alike.
/// </summary>
public abstract class ArtifactStorageContractTests
{
    /// <summary>Creates a fresh, empty storage instance for a single test.</summary>
    protected abstract IArtifactStorage CreateStorage();

    [Fact]
    public async Task WriteThenRead_RoundTripsText()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "reports/daily.etlsql", "SELECT 1;");

        Assert.True(await s.ExistsAsync(ArtifactArea.Scripts, "reports/daily.etlsql"));
        Assert.Equal("SELECT 1;", await s.ReadAllTextAsync(ArtifactArea.Scripts, "reports/daily.etlsql"));
    }

    [Fact]
    public async Task SeparatorsAreNormalized_BackslashEqualsForwardSlash()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "a\\b\\c.etlsql", "x");

        Assert.True(await s.ExistsAsync(ArtifactArea.Scripts, "a/b/c.etlsql"));
        var info = await s.GetInfoAsync(ArtifactArea.Scripts, "a/b/c.etlsql");
        Assert.Equal("a/b/c.etlsql", info!.Value.Path);
    }

    [Fact]
    public async Task Areas_AreIsolated_SamePathDoesNotCollide()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "x.dat", "script");
        await s.WriteAllTextAsync(ArtifactArea.Maps, "x.dat", "map");

        Assert.Equal("script", await s.ReadAllTextAsync(ArtifactArea.Scripts, "x.dat"));
        Assert.Equal("map", await s.ReadAllTextAsync(ArtifactArea.Maps, "x.dat"));
        Assert.False(await s.ExistsAsync(ArtifactArea.Snapshots, "x.dat"));
    }

    [Fact]
    public async Task Write_NoOverwrite_ThrowsAndPreservesExisting()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet", "original");

        await Assert.ThrowsAsync<IOException>(() =>
            s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet", "replacement", overwrite: false));

        Assert.Equal("original", await s.ReadAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet"));
    }

    [Fact]
    public async Task Write_Overwrite_ReplacesContent()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet", "original");
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet", "replacement");

        Assert.Equal("replacement", await s.ReadAllTextAsync(ArtifactArea.Datasets, "cache_1.parquet"));
    }

    [Fact]
    public async Task GetInfo_ReportsLength_AndNullWhenAbsent()
    {
        var s = CreateStorage();
        await s.WriteAllBytesAsync(ArtifactArea.Snapshots, "snap.html", Encoding.UTF8.GetBytes("hello"));

        var info = await s.GetInfoAsync(ArtifactArea.Snapshots, "snap.html");
        Assert.Equal(5, info!.Value.Length);
        Assert.Null(await s.GetInfoAsync(ArtifactArea.Snapshots, "missing.html"));
    }

    [Fact]
    public async Task ReadMissing_Throws()
    {
        var s = CreateStorage();
        // IOException is the common base of FileNotFoundException (in-memory; file absent) and
        // DirectoryNotFoundException (filesystem; the area directory may not exist yet).
        await Assert.ThrowsAnyAsync<IOException>(() =>
            s.ReadAllBytesAsync(ArtifactArea.Scripts, "nope.etlsql"));
    }

    [Fact]
    public async Task OpenRead_StreamsContent()
    {
        var s = CreateStorage();
        await s.WriteAllBytesAsync(ArtifactArea.Snapshots, "s.bin", new byte[] { 1, 2, 3, 4 });

        await using var stream = await s.OpenReadAsync(ArtifactArea.Snapshots, "s.bin");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ms.ToArray());
    }

    [Fact]
    public async Task Enumerate_RespectsPrefixAndRecursion()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "team/a.etlsql", "1");
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "team/sub/b.etlsql", "2");
        await s.WriteAllTextAsync(ArtifactArea.Scripts, "other/c.etlsql", "3");

        var recursive = await ToPaths(s.EnumerateAsync(ArtifactArea.Scripts, "team", recursive: true));
        Assert.Equal(new[] { "team/a.etlsql", "team/sub/b.etlsql" }, recursive);

        var shallow = await ToPaths(s.EnumerateAsync(ArtifactArea.Scripts, "team", recursive: false));
        Assert.Equal(new[] { "team/a.etlsql" }, shallow);

        var all = await ToPaths(s.EnumerateAsync(ArtifactArea.Scripts));
        Assert.Equal(new[] { "other/c.etlsql", "team/a.etlsql", "team/sub/b.etlsql" }, all);
    }

    [Fact]
    public async Task Move_RenamesAndHonorsOverwrite()
    {
        var s = CreateStorage();
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
    public async Task Move_MissingSource_Throws()
    {
        var s = CreateStorage();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            s.MoveAsync(ArtifactArea.Datasets, "ghost", "dest"));
    }

    [Fact]
    public async Task Delete_ReturnsWhetherSomethingWasRemoved()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Maps, "lookup.csv", "k,v");

        Assert.True(await s.DeleteAsync(ArtifactArea.Maps, "lookup.csv"));
        Assert.False(await s.DeleteAsync(ArtifactArea.Maps, "lookup.csv"));
    }

    [Fact]
    public async Task LeaseLocalCopy_ExposesReadableContentDuringLease()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_2.parquet", "bytes");

        await using var lease = await s.LeaseLocalCopyAsync(ArtifactArea.Datasets, "cache_2.parquet");
        Assert.Equal("bytes", await File.ReadAllTextAsync(lease.LocalPath));
    }

    [Fact]
    public async Task Keys_CannotBeLeasedToDisk()
    {
        var s = CreateStorage();
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
        var s = CreateStorage();
        await Assert.ThrowsAsync<ArgumentException>(() => s.WriteAllTextAsync(ArtifactArea.Scripts, path, "x"));
    }

    [Fact]
    public async Task AbsolutePaths_AreRejected()
    {
        var s = CreateStorage();
        var rooted = OperatingSystem.IsWindows() ? @"C:\evil.txt" : "/etc/evil.txt";
        await Assert.ThrowsAsync<ArgumentException>(() => s.WriteAllTextAsync(ArtifactArea.Scripts, rooted, "x"));
    }

    private protected static async Task<string[]> ToPaths(IAsyncEnumerable<ArtifactInfo> items)
    {
        var list = new List<string>();
        await foreach (var i in items) list.Add(i.Path);
        return list.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
}
