using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// The flight recorder's durable half: a run's statement timeline, keyed on its job-history id.
/// </summary>
public sealed class JobStatementMetricsStoreTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"etlsql_stmt_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private SQLiteJobHistoryStore NewStore() => new(_dbPath);

    private static StatementMetricsPayload Statement(string sql, long durationMs, bool failed = false) => new()
    {
        Statement = sql,
        DurationMs = durationMs,
        RowsProcessed = 100,
        CpuTimeMs = durationMs / 2,
        SpilledBytes = 4096,
        Failed = failed
    };

    [Fact]
    public async Task StatementsRoundTripInExecutionOrder()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var historyId = await store.LogJobStartAsync("nightly");

        await store.SaveJobStatementMetricsAsync(historyId,
        [
            Statement("SELECT ?", 10),
            Statement("INSERT INTO t SELECT ?", 90),
            Statement("UPDATE t SET x = ?", 20)
        ]);

        var read = await store.GetJobStatementMetricsAsync(historyId);

        Assert.Equal(3, read.Count);
        Assert.Equal(["SELECT ?", "INSERT INTO t SELECT ?", "UPDATE t SET x = ?"],
            read.Select(s => s.Statement).ToArray());
    }

    [Fact]
    public async Task MeasurementsSurviveTheRoundTrip()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var historyId = await store.LogJobStartAsync("nightly");

        await store.SaveJobStatementMetricsAsync(historyId, [Statement("SELECT ?", 1234, failed: true)]);

        var read = (await store.GetJobStatementMetricsAsync(historyId)).Single();

        Assert.Equal(1234, read.DurationMs);
        Assert.Equal(100, read.RowsProcessed);
        Assert.Equal(617, read.CpuTimeMs);
        Assert.Equal(4096, read.SpilledBytes);
        Assert.True(read.Failed);
    }

    /// <summary>Byte and duration counters must not be capped at 32 bits.</summary>
    [Fact]
    public async Task LargeCountersAreNotTruncated()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var historyId = await store.LogJobStartAsync("nightly");
        const long huge = 8L * 1024 * 1024 * 1024;

        await store.SaveJobStatementMetricsAsync(historyId,
            [new StatementMetricsPayload { Statement = "SELECT ?", SpilledBytes = huge, DurationMs = huge }]);

        var read = (await store.GetJobStatementMetricsAsync(historyId)).Single();

        Assert.Equal(huge, read.SpilledBytes);
        Assert.Equal(huge, read.DurationMs);
    }

    [Fact]
    public async Task ARunWithNoStatementsReadsBackEmptyRatherThanThrowing()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var historyId = await store.LogJobStartAsync("nightly");

        await store.SaveJobStatementMetricsAsync(historyId, []);

        Assert.Empty(await store.GetJobStatementMetricsAsync(historyId));
    }

    [Fact]
    public async Task StatementsAreScopedToTheirOwnRun()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var first = await store.LogJobStartAsync("nightly");
        var second = await store.LogJobStartAsync("nightly");

        await store.SaveJobStatementMetricsAsync(first, [Statement("SELECT ?", 10)]);
        await store.SaveJobStatementMetricsAsync(second, [Statement("UPDATE t SET x = ?", 20)]);

        Assert.Equal("SELECT ?", (await store.GetJobStatementMetricsAsync(first)).Single().Statement);
        Assert.Equal("UPDATE t SET x = ?", (await store.GetJobStatementMetricsAsync(second)).Single().Statement);
    }

    /// <summary>
    /// Statement detail is the bulk of a run's rows. Orphaning it when history is pruned is how the
    /// flight recorder would grow without bound on a 200-jobs-a-day estate.
    /// </summary>
    [Fact]
    public async Task PruningHistoryTakesTheStatementDetailWithIt()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var historyId = await store.LogJobStartAsync("nightly");
        await store.LogJobEndAsync(historyId, "SUCCESS", null);
        await store.SaveJobStatementMetricsAsync(historyId, [Statement("SELECT ?", 10)]);

        Assert.NotEmpty(await store.GetJobStatementMetricsAsync(historyId));

        // Everything before "now" is past the cutoff.
        await store.PruneHistoryAsync(TimeSpan.Zero);

        Assert.Empty(await store.GetJobStatementMetricsAsync(historyId));
    }

    [Fact]
    public async Task StatementRetentionKeepsFailedRunsLongerThanSuccessfulRuns()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var successful = await store.LogJobStartAsync("success");
        var failed = await store.LogJobStartAsync("failure");
        await store.LogJobEndAsync(successful, "SUCCESS", null);
        await store.LogJobEndAsync(failed, "FAILURE", "boom");
        await store.SaveJobStatementMetricsAsync(successful, [Statement("SELECT ?", 10)]);
        await store.SaveJobStatementMetricsAsync(failed, [Statement("THROW ?", 10, failed: true)]);

        var removed = await store.PruneStatementMetricsAsync(
            successMaxAge: TimeSpan.Zero,
            failedMaxAge: TimeSpan.FromDays(30));

        Assert.Equal(1, removed);
        Assert.Empty(await store.GetJobStatementMetricsAsync(successful));
        Assert.Single(await store.GetJobStatementMetricsAsync(failed));
    }

    [Fact]
    public async Task FailedStatementRetentionDoesNotDeleteSuccessfulRunsEarly()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var successful = await store.LogJobStartAsync("success");
        var failed = await store.LogJobStartAsync("failure");
        await store.LogJobEndAsync(successful, "SUCCESS", null);
        await store.LogJobEndAsync(failed, "FAILURE", "boom");
        await store.SaveJobStatementMetricsAsync(successful, [Statement("SELECT ?", 10)]);
        await store.SaveJobStatementMetricsAsync(failed, [Statement("THROW ?", 10, failed: true)]);

        var removed = await store.PruneStatementMetricsAsync(
            successMaxAge: TimeSpan.FromDays(30),
            failedMaxAge: TimeSpan.Zero);

        Assert.Equal(1, removed);
        Assert.Single(await store.GetJobStatementMetricsAsync(successful));
        Assert.Empty(await store.GetJobStatementMetricsAsync(failed));
    }

    /// <summary>Re-writing the same run must not duplicate its timeline.</summary>
    [Fact]
    public async Task SavingTwiceDoesNotDuplicateTheTimeline()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var historyId = await store.LogJobStartAsync("nightly");

        await store.SaveJobStatementMetricsAsync(historyId, [Statement("SELECT ?", 10)]);
        await store.SaveJobStatementMetricsAsync(historyId, [Statement("SELECT ?", 10)]);

        Assert.Single(await store.GetJobStatementMetricsAsync(historyId));
    }
}
