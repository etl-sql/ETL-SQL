using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Storage;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Practical HA P1.5: the local-disk provider under the shared storage contract, plus filesystem-
/// specific behavior (a lease hands back the artifact's real, persistent path) and the
/// <see cref="ArtifactStorageFactory"/> / SMB-root validation.
/// </summary>
public sealed class FileSystemArtifactStorageTests : ArtifactStorageContractTests, IDisposable
{
    private readonly string _base;

    public FileSystemArtifactStorageTests()
    {
        _base = Path.Combine(Path.GetTempPath(), $"etlsql-artifacts-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); } catch { /* best-effort */ }
    }

    protected override IArtifactStorage CreateStorage()
    {
        // Each area gets its own directory under a fresh temp base; New() per test keeps them isolated.
        var sub = Path.Combine(_base, Guid.NewGuid().ToString("N"));
        var roots = new Dictionary<ArtifactArea, string>
        {
            [ArtifactArea.Scripts] = Path.Combine(sub, "scripts"),
            [ArtifactArea.Snapshots] = Path.Combine(sub, "snapshots"),
            [ArtifactArea.Datasets] = Path.Combine(sub, "datasets"),
            [ArtifactArea.Maps] = Path.Combine(sub, "maps"),
            [ArtifactArea.Keys] = Path.Combine(sub, "keys"),
        };
        return new LocalArtifactStorage(roots);
    }

    [Fact]
    public async Task LeaseLocalCopy_ReturnsRealPath_ThatSurvivesDispose()
    {
        var s = CreateStorage();
        await s.WriteAllTextAsync(ArtifactArea.Datasets, "cache_3.parquet", "data");

        string leasedPath;
        await using (var lease = await s.LeaseLocalCopyAsync(ArtifactArea.Datasets, "cache_3.parquet"))
        {
            leasedPath = lease.LocalPath;
            Assert.True(File.Exists(leasedPath));
        }
        // A filesystem lease hands back the artifact's own path — it must NOT be deleted on release.
        Assert.True(File.Exists(leasedPath));
        Assert.Equal("data", await s.ReadAllTextAsync(ArtifactArea.Datasets, "cache_3.parquet"));
    }

    [Fact]
    public void Factory_DefaultsToLocal_AndRejectsUnknown()
    {
        var roots = new Dictionary<ArtifactArea, string> { [ArtifactArea.Scripts] = _base };
        Assert.IsType<LocalArtifactStorage>(ArtifactStorageFactory.Create(null, roots));
        Assert.IsType<LocalArtifactStorage>(ArtifactStorageFactory.Create("local", roots));
        Assert.Throws<ArgumentException>(() => ArtifactStorageFactory.Create("mysql-fs", roots));
    }

    [Fact]
    public void Smb_RejectsNonUncRoots()
    {
        var roots = new Dictionary<ArtifactArea, string> { [ArtifactArea.Scripts] = _base }; // local path
        Assert.Throws<ArgumentException>(() => ArtifactStorageFactory.Create("smb", roots));
    }

    [Fact]
    public void Smb_AcceptsUncRoots_WhenReachabilityCheckSkipped()
    {
        var roots = new Dictionary<ArtifactArea, string>
        {
            [ArtifactArea.Scripts] = @"\\nas\etlsql\scripts",
            [ArtifactArea.Datasets] = @"\\nas\etlsql\datasets",
        };
        // verifyReachable: false so the test doesn't depend on a live share.
        var storage = ArtifactStorageFactory.Create("smb", roots, verifyReachable: false);
        Assert.IsType<SmbArtifactStorage>(storage);
    }

    [Fact]
    public void Smb_UnreachableShare_FailsFast()
    {
        var roots = new Dictionary<ArtifactArea, string>
        {
            [ArtifactArea.Scripts] = @"\\nonexistent-host-" + Guid.NewGuid().ToString("N") + @"\share",
        };
        Assert.Throws<InvalidOperationException>(() => ArtifactStorageFactory.Create("smb", roots, verifyReachable: true));
    }
}
