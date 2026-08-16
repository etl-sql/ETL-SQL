using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class OrchestratorCatalogScaleAssessmentTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(1000)]
    [Trait("Category", "ScaleAssessment")]
    public async Task SchedulerAndHistoryQueriesStayIndexedAndBounded(int objectCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"etlsql-scale-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SQLiteJobHistoryStore(path);
            await store.InitializeAsync();
            await SeedAsync(path, objectCount);

            var due = (await store.GetDueJobsAsync(DateTime.UtcNow)).ToList();
            var firstPage = (await store.GetJobsPageAsync(20, 0)).ToList();
            var secondPage = (await store.GetJobsPageAsync(20, 20)).ToList();
            var history = (await store.GetHistoryAsync(limit: 100)).ToList();
            var completed = (await store.GetCompletedHistoryAsync(
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), 100, 0)).ToList();

            Assert.Equal(objectCount, due.Count);
            Assert.Equal(Math.Min(20, objectCount), firstPage.Count);
            Assert.Equal(Math.Min(20, Math.Max(0, objectCount - 20)), secondPage.Count);
            Assert.Equal(Math.Min(100, objectCount), history.Count);
            Assert.Equal(Math.Min(100, objectCount), completed.Count);
            Assert.Empty(firstPage.Select(job => job.Name).Intersect(secondPage.Select(job => job.Name)));

            Assert.Contains("idx_jobs_sched", await QueryPlanAsync(path,
                "SELECT * FROM Jobs WHERE IsEnabled = 1 AND (NextRun IS NULL OR NextRun <= '9999')"));
            Assert.Contains("idx_jh_start", await QueryPlanAsync(path,
                "SELECT * FROM JobHistory ORDER BY StartTime DESC LIMIT 100"));
            Assert.Contains("idx_jh_end", await QueryPlanAsync(path,
                "SELECT * FROM JobHistory WHERE EndTime > '0000' AND EndTime <= '9999' ORDER BY EndTime, Id LIMIT 100"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
            try { File.Delete(path + "-wal"); } catch (IOException) { }
            try { File.Delete(path + "-shm"); } catch (IOException) { }
        }
    }

    private static async Task SeedAsync(string path, int count)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow;
        for (var i = 0; i < count; i++)
        {
            await using var job = connection.CreateCommand();
            job.Transaction = transaction;
            // Seeded with the surrogate identity the scheduler and history queries index on — the
            // point of this assessment is that those queries stay indexed, and a row with no identity
            // would not be reachable by the query being measured.
            var jobId = Guid.NewGuid().ToString("N");
            job.CommandText = "INSERT INTO Jobs (Id, Name, Script, Interval, Unit, NextRun, IsEnabled) VALUES ($id, $name, 'SELECT 1;', 1, 'HOUR', $next, 1)";
            job.Parameters.AddWithValue("$id", jobId);
            job.Parameters.AddWithValue("$name", $"job-{i:D4}");
            job.Parameters.AddWithValue("$next", now.AddMinutes(-1).ToString("O"));
            await job.ExecuteNonQueryAsync();

            await using var history = connection.CreateCommand();
            history.Transaction = transaction;
            history.CommandText = "INSERT INTO JobHistory (JobId, JobName, StartTime, EndTime, Status) VALUES ($id, $name, $start, $end, 'SUCCESS')";
            history.Parameters.AddWithValue("$id", jobId);
            history.Parameters.AddWithValue("$name", $"job-{i:D4}");
            history.Parameters.AddWithValue("$start", now.AddMinutes(-2).AddTicks(i).ToString("O"));
            history.Parameters.AddWithValue("$end", now.AddMinutes(-1).AddTicks(i).ToString("O"));
            await history.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task<string> QueryPlanAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) details.Add(reader.GetString(3));
        return string.Join(Environment.NewLine, details);
    }
}
