using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Storage;
using ETL_SQL.Services;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Practical HA P1.6: the SecurityService-backed guardrails enforced at the storage boundary —
/// no executables in any area, application-logic files only in the Scripts area, and traversal
/// rejection — proven by wrapping the in-memory reference store with <see cref="GuardedArtifactStorage"/>.
/// </summary>
public class GuardedArtifactStorageTests : ArtifactStorageContractTests
{
    // The guard must be transparent to all legitimate operations, so it also runs the full shared
    // contract. The contract only uses safe extensions (.parquet/.html/.csv/.etlsql-in-Scripts/...).
    protected override IArtifactStorage CreateStorage() =>
        new GuardedArtifactStorage(new InMemoryArtifactStorage(), new SecurityService(NullLogger.Instance));

    private static GuardedArtifactStorage New() =>
        new(new InMemoryArtifactStorage(), new SecurityService(NullLogger.Instance));

    [Theory]
    [InlineData(ArtifactArea.Scripts, "payload.dll")]
    [InlineData(ArtifactArea.Snapshots, "evil.exe")]
    [InlineData(ArtifactArea.Datasets, "run.bat")]
    [InlineData(ArtifactArea.Maps, "trust.pfx")]
    [InlineData(ArtifactArea.Keys, "lib.so.cmd")]
    public async Task Executables_AreBlocked_InEveryArea(ArtifactArea area, string path)
    {
        var s = New();
        await Assert.ThrowsAsync<SecurityException>(() => s.WriteAllTextAsync(area, path, "x"));
    }

    [Theory]
    [InlineData("report.etlsql")]
    [InlineData("query.sql")]
    [InlineData("macro.py")]
    [InlineData("hook.sh")]
    public async Task ApplicationLogicFiles_AreBlocked_OutsideScriptsArea(string path)
    {
        var s = New();
        await Assert.ThrowsAsync<SecurityException>(() => s.WriteAllTextAsync(ArtifactArea.Datasets, path, "x"));
        await Assert.ThrowsAsync<SecurityException>(() => s.WriteAllTextAsync(ArtifactArea.Snapshots, path, "x"));
    }

    [Theory]
    [InlineData("report.etlsql")]
    [InlineData("dashboard.rptsql")]
    public async Task ApplicationLogicFiles_AreAllowed_InScriptsArea(string path)
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, path, "SELECT 1;");
        Assert.Equal("SELECT 1;", await s.ReadAllTextAsync(ArtifactArea.Scripts, path));
    }

    [Fact]
    public async Task LegitimateArtifacts_PassThrough()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Snapshots, "report.html", "<html/>");
        await s.WriteAllBytesAsync(ArtifactArea.Datasets, "sales_1.parquet", new byte[] { 1, 2 });
        await s.WriteAllTextAsync(ArtifactArea.Maps, "lookup.csv", "k,v");

        Assert.True(await s.ExistsAsync(ArtifactArea.Snapshots, "report.html"));
        Assert.True(await s.ExistsAsync(ArtifactArea.Datasets, "sales_1.parquet"));
        Assert.True(await s.ExistsAsync(ArtifactArea.Maps, "lookup.csv"));
    }

    [Fact]
    public async Task Move_GuardsDestinationArea()
    {
        var s = New();
        // Staging a .parquet then renaming to a logic-file name in a non-Scripts area is blocked.
        await s.WriteAllTextAsync(ArtifactArea.Datasets, ".staging", "x");
        await Assert.ThrowsAsync<SecurityException>(() =>
            s.MoveAsync(ArtifactArea.Datasets, ".staging", "smuggled.etlsql"));
    }

    [Fact]
    public async Task Move_AllowsLogicFile_IntoScriptsArea()
    {
        var s = New();
        await s.WriteAllTextAsync(ArtifactArea.Scripts, ".staging-script", "SELECT 1;");
        await s.MoveAsync(ArtifactArea.Scripts, ".staging-script", "published.etlsql");
        Assert.True(await s.ExistsAsync(ArtifactArea.Scripts, "published.etlsql"));
    }
}
