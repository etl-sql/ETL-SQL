using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class AssertTableStatementTests
{
    private static Evaluator NewEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static async Task ExecuteAsync(string sql)
    {
        var eval = NewEvaluator();
        await eval.Evaluate(new Lexer(sql).TokenizeToScript());
    }

    [Fact]
    public async Task AssertTable_MatchingTables_Passes()
    {
        var script = @"
            CREATE TABLE #actual (Id INT, Name VARCHAR(50), Amount DECIMAL(10,2));
            INSERT INTO #actual VALUES (1, 'Alice', 100.50), (2, 'Bob', 200.00);

            CREATE TABLE #expected (Id INT, Name VARCHAR(50), Amount DECIMAL(10,2));
            INSERT INTO #expected VALUES (1, 'Alice', 100.50), (2, 'Bob', 200.00);

            ASSERT TABLE #actual MATCHES #expected;
        ";

        await ExecuteAsync(script);
    }

    [Fact]
    public async Task AssertTable_ValueMismatch_ThrowsExecutionExceptionWithDiff()
    {
        var script = @"
            CREATE TABLE #actual (Id INT, Status VARCHAR(20));
            INSERT INTO #actual VALUES (1, 'ACTIVE'), (2, 'PENDING');

            CREATE TABLE #expected (Id INT, Status VARCHAR(20));
            INSERT INTO #expected VALUES (1, 'ACTIVE'), (2, 'COMPLETED');

            ASSERT TABLE #actual MATCHES #expected;
        ";

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ExecuteAsync(script));
        Assert.Contains("Status: actual='PENDING', expected='COMPLETED'", ex.Message);
    }

    [Fact]
    public async Task AssertTable_RowCountMismatch_ThrowsExecutionException()
    {
        var script = @"
            CREATE TABLE #actual (Id INT);
            INSERT INTO #actual VALUES (1), (2), (3);

            CREATE TABLE #expected (Id INT);
            INSERT INTO #expected VALUES (1), (2);

            ASSERT TABLE #actual MATCHES #expected;
        ";

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ExecuteAsync(script));
        Assert.Contains("Row count mismatch", ex.Message);
        Assert.Contains("actual has 3, expected has 2", ex.Message);
    }

    [Fact]
    public async Task AssertTable_SchemaMismatch_ThrowsExecutionException()
    {
        var script = @"
            CREATE TABLE #actual (Id INT, ExtraCol VARCHAR(10));
            INSERT INTO #actual VALUES (1, 'extra');

            CREATE TABLE #expected (Id INT, MissingCol VARCHAR(10));
            INSERT INTO #expected VALUES (1, 'missing');

            ASSERT TABLE #actual MATCHES #expected;
        ";

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ExecuteAsync(script));
        Assert.Contains("schema mismatch", ex.Message);
        Assert.Contains("Missing in actual: MissingCol", ex.Message);
        Assert.Contains("Extra in actual:   ExtraCol", ex.Message);
    }

    [Fact]
    public async Task AssertTable_WithIgnoreOrder_PassesWhenUnordered()
    {
        var script = @"
            CREATE TABLE #actual (Id INT, Tag VARCHAR(20));
            INSERT INTO #actual VALUES (1, 'ALPHA'), (2, 'BETA'), (3, 'GAMMA');

            CREATE TABLE #expected (Id INT, Tag VARCHAR(20));
            INSERT INTO #expected VALUES (3, 'GAMMA'), (1, 'ALPHA'), (2, 'BETA');

            ASSERT TABLE #actual MATCHES #expected WITH (IGNORE_ORDER = TRUE);
        ";

        await ExecuteAsync(script);
    }

    [Fact]
    public async Task AssertTable_WithTolerance_PassesWithinBound()
    {
        var script = @"
            CREATE TABLE #actual (MetricName VARCHAR(50), Value DECIMAL(10,4));
            INSERT INTO #actual VALUES ('score', 98.452);

            CREATE TABLE #expected (MetricName VARCHAR(50), Value DECIMAL(10,4));
            INSERT INTO #expected VALUES ('score', 98.459);

            ASSERT TABLE #actual MATCHES #expected WITH (TOLERANCE = 0.01);
        ";

        await ExecuteAsync(script);
    }

    [Fact]
    public async Task AssertTable_WithTolerance_FailsBeyondBound()
    {
        var script = @"
            CREATE TABLE #actual (MetricName VARCHAR(50), Value DECIMAL(10,4));
            INSERT INTO #actual VALUES ('score', 98.400);

            CREATE TABLE #expected (MetricName VARCHAR(50), Value DECIMAL(10,4));
            INSERT INTO #expected VALUES ('score', 98.450);

            ASSERT TABLE #actual MATCHES #expected WITH (TOLERANCE = 0.01);
        ";

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ExecuteAsync(script));
        Assert.Contains("Value: actual=98.400", ex.Message);
    }

    [Fact]
    public async Task AssertTable_WithIgnoreColumns_SkipsIgnoredColumn()
    {
        var script = @"
            CREATE TABLE #actual (Id INT, CreatedAt VARCHAR(50), Status VARCHAR(20));
            INSERT INTO #actual VALUES (1, '2026-08-14T20:00:00Z', 'OK');

            CREATE TABLE #expected (Id INT, CreatedAt VARCHAR(50), Status VARCHAR(20));
            INSERT INTO #expected VALUES (1, '2020-01-01T00:00:00Z', 'OK');

            ASSERT TABLE #actual MATCHES #expected WITH (IGNORE_COLUMNS = 'CreatedAt');
        ";

        await ExecuteAsync(script);
    }

    [Fact]
    public async Task AssertTable_WithMessage_IncludesCustomMessageInDiff()
    {
        var script = @"
            CREATE TABLE #actual (Id INT);
            INSERT INTO #actual VALUES (100);

            CREATE TABLE #expected (Id INT);
            INSERT INTO #expected VALUES (200);

            ASSERT TABLE #actual MATCHES #expected WITH (MESSAGE = 'User dimension transform verification failed');
        ";

        var ex = await Assert.ThrowsAsync<ExecutionException>(() => ExecuteAsync(script));
        Assert.Contains("User dimension transform verification failed", ex.Message);
    }
}
