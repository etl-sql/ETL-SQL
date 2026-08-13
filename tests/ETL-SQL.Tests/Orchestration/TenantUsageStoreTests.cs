using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class TenantUsageStoreTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"etlsql_tenant_usage_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task UsageIsTenantPartitionedIdempotentAndDurable()
    {
        var recordedAt = DateTime.UtcNow.AddMinutes(-1);
        var store = new SQLiteJobHistoryStore(_dbPath);
        await store.InitializeAsync();

        await store.SaveTenantUsageAsync(Usage("tenant-alpha", 41, recordedAt, rows: 12));
        await store.SaveTenantUsageAsync(Usage("tenant-beta", 41, recordedAt.AddSeconds(1), rows: 99));
        await store.SaveTenantUsageAsync(Usage("tenant-alpha", 41, recordedAt.AddSeconds(2), rows: 777));

        var reopened = new SQLiteJobHistoryStore(_dbPath);
        var alpha = await reopened.GetTenantUsageAsync("tenant-alpha");
        var beta = await reopened.GetTenantUsageAsync("tenant-beta");

        var alphaRow = Assert.Single(alpha);
        Assert.Equal(12, alphaRow.RowsProcessed);
        Assert.Equal("tenant-alpha", alphaRow.TenantId);
        Assert.Equal(41, alphaRow.JobHistoryId);
        Assert.Equal(99, Assert.Single(beta).RowsProcessed);
        Assert.DoesNotContain(alpha, row => row.TenantId == "tenant-beta");
    }

    [Fact]
    public async Task QueryAppliesTenantAndTimeBoundary()
    {
        var store = new SQLiteJobHistoryStore(_dbPath);
        await store.InitializeAsync();
        var cutoff = DateTime.UtcNow.AddMinutes(-5);

        await store.SaveTenantUsageAsync(Usage("tenant-alpha", 1, cutoff.AddMinutes(-1), rows: 1));
        await store.SaveTenantUsageAsync(Usage("tenant-alpha", 2, cutoff.AddMinutes(1), rows: 2));
        await store.SaveTenantUsageAsync(Usage("tenant-beta", 3, cutoff.AddMinutes(2), rows: 3));

        var rows = await store.GetTenantUsageAsync("tenant-alpha", cutoff);

        Assert.Equal([2L], rows.Select(row => row.JobHistoryId).ToArray());
    }

    [Theory]
    [InlineData("", 1, 0, 0, 0, 0)]
    [InlineData("tenant-alpha", 0, 0, 0, 0, 0)]
    [InlineData("tenant-alpha", 1, -1, 0, 0, 0)]
    [InlineData("tenant-alpha", 1, 0, -1, 0, 0)]
    [InlineData("tenant-alpha", 1, 0, 0, -1, 0)]
    [InlineData("tenant-alpha", 1, 0, 0, 0, -1)]
    public async Task InvalidAuthorityOrMeasuresAreRejected(
        string tenantId,
        long historyId,
        long rows,
        long memory,
        double cpu,
        long duration)
    {
        var store = new SQLiteJobHistoryStore(_dbPath);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SaveTenantUsageAsync(
            new TenantUsageRecord(0, tenantId, historyId, "Script", "SUCCESS",
                rows, memory, cpu, duration, DateTime.UtcNow)));
    }

    private static TenantUsageRecord Usage(
        string tenantId,
        long historyId,
        DateTime recordedAt,
        long rows) =>
        new(0, tenantId, historyId, "Script", "SUCCESS", rows, 2048, 0.25, 15, recordedAt);
}
