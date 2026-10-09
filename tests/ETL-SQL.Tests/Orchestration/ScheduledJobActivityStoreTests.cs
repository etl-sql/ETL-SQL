using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class ScheduledJobActivityStoreTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"etlsql-activity-{Guid.NewGuid():N}.db");
    private readonly SQLiteJobHistoryStore store;
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-10-07T12:00:00Z");

    public ScheduledJobActivityStoreTests() => store = new SQLiteJobHistoryStore(databasePath);

    public void Dispose()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        SqliteConnection.ClearPool(connection);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
    }

    [Fact]
    public async Task ProjectionSeparatesTenantsAndUsesCurrentIdentityAfterNameReuse()
    {
        var oldJob = await AddJobAsync("same", "tenant-a");
        await AddOutcomeAsync(oldJob, "SUCCESS", "2026-10-07T01:00:00Z");
        await store.DeleteJobAsync(oldJob.Id);
        var replacement = await AddJobAsync("same", "tenant-a");
        var peer = await AddJobAsync("same", "tenant-b");
        await AddOutcomeAsync(peer, "FAILURE", "2026-10-07T01:00:00Z");
        await AddJobAsync("same", null);

        var result = Assert.Single(await store.GetScheduledJobActivityAsync("tenant-a", AsOf));
        Assert.Equal(replacement.Id, result.Job.Id);
        Assert.False(result.CompletedToday);
        Assert.False(result.FailedToday);
        var other = Assert.Single(await store.GetScheduledJobActivityAsync("tenant-b", AsOf));
        Assert.True(other.FailedToday);
        Assert.Single(await store.GetScheduledJobActivityAsync(null, AsOf));
    }

    [Theory]
    [InlineData("2026-10-06T20:00:00-04:00", true)]
    [InlineData("2026-10-07T14:00:00+14:00", true)]
    [InlineData("2026-10-07T00:00:00", true)]
    [InlineData("2026-10-07T00:00:00+01:00", false)]
    [InlineData("2026-10-07T12:00:00Z", true)]
    [InlineData("2026-10-07T12:00:00.001Z", false)]
    [InlineData("2026-10-08T00:00:00-14:00", false)]
    public async Task TodayUsesUtcInstantsIncludingMidnightAndExcludingFuture(string ended, bool expected)
    {
        var job = await AddJobAsync("offset", "tenant-a");
        await AddOutcomeAsync(job, "SUCCESS", ended);
        Assert.Equal(expected, Assert.Single(await store.GetScheduledJobActivityAsync("tenant-a", AsOf)).CompletedToday);
    }

    [Fact]
    public async Task MultipleOutcomesGiveDistinctMembershipAndLastRunDoesNotImplySuccess()
    {
        var job = await AddJobAsync("retry", "tenant-a");
        foreach (var status in new[] { "FAILURE", "BLOCKED", "SUCCESS", "SUCCESS" })
            await AddOutcomeAsync(job, status, "2026-10-07T03:00:00Z");
        var timestampOnly = await AddJobAsync("last-run-only", "tenant-a");
        await store.SaveJobAsync(timestampOnly with { LastRun = AsOf.UtcDateTime });
        var cancelled = await AddJobAsync("cancelled", "tenant-a");
        await AddOutcomeAsync(cancelled, "CANCELLED", "2026-10-07T03:00:00Z");
        var result = await store.GetScheduledJobActivityAsync("tenant-a", AsOf);
        Assert.True(result.Single(a => a.Job.Id == job.Id).CompletedToday);
        Assert.True(result.Single(a => a.Job.Id == job.Id).FailedToday);
        Assert.Single(result, a => a.CompletedToday);
        Assert.Single(result, a => a.FailedToday);
        Assert.False(result.Single(a => a.Job.Id == timestampOnly.Id).CompletedToday);
        Assert.False(result.Single(a => a.Job.Id == cancelled.Id).FailedToday);
    }

    [Fact]
    public async Task StateRequiresLiveOwnerAndFenceAndDefinitionSavePreservesIt()
    {
        var job = await AddJobAsync("state", "tenant-a");
        var token = (await store.AcquireJobLeaseAsync(job.Id, "owner-a", TimeSpan.FromDays(3650)))!.Value;
        Assert.True(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", token, ScheduledJobRunState.Queued));
        Assert.Equal(ScheduledJobRunState.Queued, await StateAsync(job));
        Assert.False(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-b", token, ScheduledJobRunState.Running));
        Assert.False(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", token + 1, ScheduledJobRunState.Running));
        Assert.True(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", token, ScheduledJobRunState.Running));
        await store.SaveJobAsync(job with { Description = "changed" });
        Assert.Equal(ScheduledJobRunState.Running, await StateAsync(job));
        await store.ReleaseJobLeaseAsync(job.Id, "stranger");
        Assert.Equal(ScheduledJobRunState.Running, await StateAsync(job));
        await store.ReleaseJobLeaseAsync(job.Id, "owner-a");
        Assert.Equal(ScheduledJobRunState.Idle, await StateAsync(job));
        Assert.False(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", token, ScheduledJobRunState.Running));
    }

    [Fact]
    public async Task ExpiryAndReclaimHideAbandonedStateAndFenceOutTheFormerOwner()
    {
        var job = await AddJobAsync("expired", "tenant-a");
        var first = (await store.AcquireJobLeaseAsync(job.Id, "owner-a", TimeSpan.FromDays(3650)))!.Value;
        Assert.True(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", first, ScheduledJobRunState.Running));
        await SqlAsync("UPDATE jobs SET lease_expires_at = '2000-01-01T00:00:00.0000000Z' WHERE id = $id", job.Id.Value);
        Assert.Equal(ScheduledJobRunState.Idle, await StateAsync(job));
        Assert.False(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", first, ScheduledJobRunState.Queued));
        var second = (await store.AcquireJobLeaseAsync(job.Id, "owner-b", TimeSpan.FromDays(3650)))!.Value;
        Assert.True(second > first);
        Assert.False(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-a", first, ScheduledJobRunState.Running));
        Assert.True(await store.TrySetScheduledJobRunStateAsync(job.Id, "owner-b", second, ScheduledJobRunState.Queued));
        await store.ReleaseJobLeaseAsync(job.Id, "owner-a");
        Assert.Equal(ScheduledJobRunState.Queued, await StateAsync(job));
    }

    [Fact]
    public async Task ProjectionHonorsCancellation()
    {
        await store.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetScheduledJobActivityAsync("tenant-a", AsOf, cancellation.Token));
    }

    [Fact]
    public async Task ExistingJobDatabaseGainsIdleStateWithoutLosingJobsOrHistory()
    {
        var job = await AddJobAsync("upgrade", "tenant-a");
        await AddOutcomeAsync(job, "SUCCESS", "2026-10-07T03:00:00Z");
        await SqlAsync("ALTER TABLE jobs DROP COLUMN run_state", job.Id.Value);
        var upgraded = new SQLiteJobHistoryStore(databasePath);
        await upgraded.InitializeAsync();
        var activity = Assert.Single(await upgraded.GetScheduledJobActivityAsync("tenant-a", AsOf));
        Assert.Equal(job.Id, activity.Job.Id);
        Assert.Equal(ScheduledJobRunState.Idle, activity.RunState);
        Assert.True(activity.CompletedToday);
    }

    private async Task<JobDefinition> AddJobAsync(string name, string? tenant)
    {
        await store.SaveJobAsync(new JobDefinition(name, "SELECT 1;", 0, "HOUR", null, null, null, true, TenantId: tenant));
        return (await store.GetJobAsync(tenant, name))!;
    }

    private async Task AddOutcomeAsync(JobDefinition job, string status, string ended)
    {
        var historyId = await store.LogJobStartAsync(job.Id);
        await SqlAsync("UPDATE job_history SET status = $status, end_time = $ended WHERE id = $id", historyId, status, ended);
    }

    private async Task<ScheduledJobRunState> StateAsync(JobDefinition job) =>
        (await store.GetScheduledJobActivityAsync(job.TenantId, DateTimeOffset.UtcNow)).Single(a => a.Job.Id == job.Id).RunState;

    private async Task SqlAsync(string sql, object id, string? status = null, string? ended = null)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        if (status is not null) command.Parameters.AddWithValue("$status", status);
        if (ended is not null) command.Parameters.AddWithValue("$ended", ended);
        await command.ExecuteNonQueryAsync();
    }
}
