using System.Linq;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class StatementMetricsPayloadTests
{
    private static StatementMetricsPayload Statement(long durationMs, bool failed = false) =>
        new() { Statement = $"SELECT {durationMs}", DurationMs = durationMs, Failed = failed };

    /// <summary>Projection must normalize; a caller must not be able to forget.</summary>
    [Fact]
    public void ProjectionNormalizesTheStatementText()
    {
        var payload = StatementMetricsPayload.From(new ExecutionMetrics
        {
            Sql = "SELECT * FROM t WHERE secret = 'hunter2'",
            DurationMs = 12
        });

        Assert.DoesNotContain("hunter2", payload.Statement);
        Assert.Equal(12, payload.DurationMs);
    }

    [Fact]
    public void ProjectionCarriesTheMeasurementsTriageAsksAbout()
    {
        var payload = StatementMetricsPayload.From(new ExecutionMetrics
        {
            Sql = "SELECT 1",
            DurationMs = 900,
            RowsProcessed = 12_000,
            CpuTimeMs = 400,
            SpilledBytes = 2048,
            DataQualityRowsQuarantined = 7
        });

        Assert.Equal(900, payload.DurationMs);
        Assert.Equal(12_000, payload.RowsProcessed);
        Assert.Equal(400, payload.CpuTimeMs);
        Assert.Equal(2048, payload.SpilledBytes);
        Assert.Equal(7, payload.DataQualityRowsQuarantined);
    }

    [Fact]
    public void RunProjectionAppliesFailureMarkingTextLimitAndCountCapTogether()
    {
        var metrics = Enumerable.Range(1, 5).Select(i => new ExecutionMetrics
        {
            Sql = $"SELECT '{new string('x', 80)}' AS secret_{i}",
            DurationMs = i
        }).ToList();

        var payload = StatementMetricsPayload.FromRun(
            metrics, runFailed: true, maxStatements: 2, maxTextLength: 24);

        Assert.Equal(2, payload.Count);
        Assert.All(payload, statement => Assert.True(statement.Statement.Length <= 24));
        Assert.True(payload[^1].Failed);
        Assert.DoesNotContain(payload, statement => statement.Statement.Contains(new string('x', 10)));
    }

    /// <summary>
    /// The wire names are the same names an operator sees in eng.profile, so one query shape reads
    /// both the live session and durable history.
    /// </summary>
    [Fact]
    public void WireNamesMatchTheProfileColumns()
    {
        var json = JsonSerializer.Serialize(Statement(5));

        Assert.Contains("\"duration_ms\"", json);
        Assert.Contains("\"rows_processed\"", json);
        Assert.Contains("\"queue_wait_ms\"", json);
        Assert.Contains("\"index_used\"", json);
        Assert.DoesNotContain("\"DurationMs\"", json);
    }

    [Fact]
    public void OneShotEnvelopeParserReadsTheSharedStatementPayload()
    {
        using var document = JsonDocument.Parse("""
            {"statementMetrics":[{"statement":"SELECT ?","duration_ms":17,"failed":true}]}
            """);

        var payload = ProcessJobExecutor.ParseStatementMetrics(document.RootElement);

        var statement = Assert.Single(payload);
        Assert.Equal("SELECT ?", statement.Statement);
        Assert.Equal(17, statement.DurationMs);
        Assert.True(statement.Failed);
    }

    // ── Capping ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ARunWithinBudgetIsCarriedWhole()
    {
        var statements = Enumerable.Range(1, 5).Select(i => Statement(i)).ToList();

        Assert.Equal(5, StatementMetricsPayload.Cap(statements, 25).Count);
    }

    [Fact]
    public void TheSlowestStatementsFillTheBudget()
    {
        var statements = Enumerable.Range(1, 100).Select(i => Statement(i)).ToList();

        var capped = StatementMetricsPayload.Cap(statements, 3);

        Assert.Equal(3, capped.Count);
        Assert.All(capped, s => Assert.True(s.DurationMs >= 98));
    }

    /// <summary>The failed statement is what an operator opens the run to find.</summary>
    [Fact]
    public void AFailedStatementIsKeptEvenWhenItIsFast()
    {
        var statements = Enumerable.Range(1, 100).Select(i => Statement(i)).ToList();
        statements.Add(Statement(1, failed: true));

        var capped = StatementMetricsPayload.Cap(statements, 3);

        Assert.Contains(capped, s => s.Failed);
    }

    /// <summary>
    /// Dropping a failure to respect the budget would hide the thing being looked for; an oversized
    /// envelope is the lesser problem.
    /// </summary>
    [Fact]
    public void FailuresBeyondTheBudgetAreAllStillKept()
    {
        var statements = Enumerable.Range(1, 10).Select(i => Statement(i, failed: true)).ToList();

        var capped = StatementMetricsPayload.Cap(statements, 3);

        Assert.Equal(10, capped.Count);
    }

    /// <summary>Output is a timeline, not a leaderboard — order must survive selection.</summary>
    [Fact]
    public void OriginalOrderIsPreserved()
    {
        var statements = new[] { Statement(10), Statement(90), Statement(20), Statement(80) };

        var capped = StatementMetricsPayload.Cap(statements, 2);

        Assert.Equal([90, 80], capped.Select(s => s.DurationMs).ToArray());
    }

    /// <summary>Two statements can measure identically and must not collapse into one.</summary>
    [Fact]
    public void IdenticalMeasurementsAreNotDeduplicated()
    {
        var statements = new[] { Statement(50), Statement(50), Statement(50) };

        Assert.Equal(3, StatementMetricsPayload.Cap(statements, 25).Count);
    }

    [Fact]
    public void ABudgetOfZeroCarriesNothing() =>
        Assert.Empty(StatementMetricsPayload.Cap([Statement(1)], 0));
}
