using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

[Trait("CompatBreak", "0.23.0")]
public class ToolingReviewSqlPathTests
{
    private static Evaluator CreateEvaluator(int windowSpillThreshold = 10000)
    {
        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var ev = sp.GetRequiredService<Evaluator>();
        ev.WindowSpillThreshold = windowSpillThreshold;
        return ev;
    }

    private static async Task<List<DataTable>> RunSqlAsync(Evaluator ev, string sql)
    {
        var tables = new List<DataTable>();
        var prev = ev.OnResultSet;
        ev.OnResultSet = t => tables.Add(t);
        try
        {
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            Assert.Empty(script.Diagnostics);
            await ev.Evaluate(script);
            return tables;
        }
        finally
        {
            ev.OnResultSet = prev;
        }
    }

    [Fact]
    public async Task WherePushdown_WithSharedColumnAndUnknownSubquerySchema_PreservesUnambiguousSemantics()
    {
        var ev = CreateEvaluator();
        var results = await RunSqlAsync(ev, """
            CREATE TABLE #left (Id INT, Status VARCHAR(20));
            INSERT INTO #left VALUES (1, 'keep'), (2, 'drop');

            SELECT l.Id, r.RightVal
            FROM #left l
            INNER JOIN (
                SELECT 1 AS Id, 'yes' AS RightVal, 'keep' AS Status
                UNION ALL
                SELECT 2 AS Id, 'no' AS RightVal, 'keep' AS Status
            ) r ON l.Id = r.Id
            WHERE Status = 'keep'
            ORDER BY l.Id;
            """);

        var table = Assert.Single(results);
        Assert.Single(table.Rows);
        Assert.Equal(1m, Convert.ToDecimal(table.Rows[0]["Id"]));
    }

    [Fact]
    public async Task RightJoin_LeftSingleIsNullPredicate_IsNotPushedPreJoin()
    {
        var ev = CreateEvaluator();
        var results = await RunSqlAsync(ev, """
            CREATE TABLE #l (Id INT, Tag VARCHAR(20));
            INSERT INTO #l VALUES (1, 'present');
            CREATE TABLE #r (Id INT);
            INSERT INTO #r VALUES (1), (2);

            SELECT r.Id, l.Tag
            FROM #l l
            RIGHT JOIN #r r ON l.Id = r.Id
            WHERE l.Tag IS NULL
            ORDER BY r.Id;
            """);

        var table = Assert.Single(results);
        Assert.Single(table.Rows);
        Assert.Equal(2m, Convert.ToDecimal(table.Rows[0]["Id"]));
    }

    [Fact]
    public async Task SemiAndAntiJoins_AndMergeJoin_HandleNullKeysAndResidualOnTerms()
    {
        var ev = CreateEvaluator();
        var semi = await RunSqlAsync(ev, """
            CREATE TABLE #l (Id INT, Flag INT);
            INSERT INTO #l VALUES (1, 10), (2, 20), (NULL, 30);
            CREATE TABLE #r (Id INT, Active INT);
            INSERT INTO #r VALUES (1, 1), (1, 0), (2, 0), (NULL, 1);

            SELECT l.Id, l.Flag
            FROM #l l
            LEFT SEMI JOIN #r r ON l.Id = r.Id AND r.Active = 1
            ORDER BY l.Flag;
            """);

        var semiRows = Assert.Single(semi).Rows;
        Assert.Single(semiRows);
        Assert.Equal(10m, Convert.ToDecimal(semiRows[0]["Flag"]));

        var anti = await RunSqlAsync(ev, """
            SELECT l.Flag
            FROM #l l
            LEFT ANTI JOIN #r r ON l.Id = r.Id AND r.Active = 1
            ORDER BY l.Flag;
            """);

        var antiRows = Assert.Single(anti).Rows;
        Assert.Equal(2, antiRows.Count);
        Assert.Equal([20m, 30m], antiRows.Select(r => Convert.ToDecimal(r["Flag"])).ToArray());

        // Also exercise JoinEngine.PerformMergeJoin directly for LEFT SEMI / LEFT ANTI
        var joinEngine = new JoinEngine(ev, NullLogger.Instance);
        var lSchema = new TableSchema(["Id", "Flag"]);
        lSchema.AddAlias("l.Id", 0);
        lSchema.AddAlias("l.Flag", 1);
        Row MakeLeft(decimal? id, decimal flag) => new(lSchema, [id, flag]);
        IAsyncEnumerable<Row> LeftStream() => new[] { MakeLeft(1m, 10m), MakeLeft(2m, 20m), MakeLeft(null, 30m) }.ToAsyncEnumerable();

        var cond = new BinaryExpression(
            new BinaryExpression(new IdentifierExpression("l.Id"), TokenType.EQUALS, new IdentifierExpression("r.Id")),
            TokenType.AND,
            new BinaryExpression(new IdentifierExpression("r.Active"), TokenType.EQUALS, new LiteralExpression(1m, TokenType.NUMBER)));

        var fromRef = new TableReference("#l", alias: "l");
        var rightRef = new TableReference("#r", alias: "r");

        var semiMergeJoin = new JoinClause("LEFT SEMI JOIN", rightRef, cond, JoinHint.Merge);
        var semiStmt = new SelectStatement([new SelectColumn(new IdentifierExpression("*"))], null, fromRef, [semiMergeJoin], null);
        var semiMergeRows = await joinEngine.ApplyJoinsStreaming(LeftStream(), [semiMergeJoin], semiStmt).ToListAsync();
        Assert.Single(semiMergeRows);
        Assert.Equal(10m, Convert.ToDecimal(semiMergeRows[0]["Flag"]));

        var antiMergeJoin = new JoinClause("LEFT ANTI JOIN", rightRef, cond, JoinHint.Merge);
        var antiStmt = new SelectStatement([new SelectColumn(new IdentifierExpression("*"))], null, fromRef, [antiMergeJoin], null);
        var antiMergeRows = await joinEngine.ApplyJoinsStreaming(LeftStream(), [antiMergeJoin], antiStmt).ToListAsync();
        Assert.Equal([20m, 30m], antiMergeRows.Select(r => Convert.ToDecimal(r["Flag"])).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CompoundKey_CompareTo_ProvidesStrictTotalOrderAcrossMixedTypes()
    {
        // Previously 2m < 10m < "1a" < 2m formed an intransitive cycle that broke sorting at scale.
        var keys = new List<CompoundKey>();
        for (int i = 0; i < 1000; i++)
        {
            keys.Add(CompoundKey.CreateJoinKey(false, [2m, i]));
            keys.Add(CompoundKey.CreateJoinKey(false, [10m, i]));
            keys.Add(CompoundKey.CreateJoinKey(false, ["1a", i]));
        }

        keys.Sort();

        for (int i = 1; i < keys.Count; i++)
            Assert.True(keys[i - 1].CompareTo(keys[i]) <= 0);
    }

    [Fact]
    public async Task CompositeMergeJoin_At3000Rows_PreservesAllCompositeMatches()
    {
        var ev = CreateEvaluator();
        var setup = new System.Text.StringBuilder();
        setup.AppendLine("CREATE TABLE #a (k1 INT, k2 VARCHAR(20), aval INT);");
        setup.AppendLine("CREATE TABLE #b (k1 INT, k2 VARCHAR(20), bval INT);");
        for (int batch = 0; batch < 30; batch++)
        {
            setup.Append("INSERT INTO #a VALUES ");
            for (int i = 0; i < 100; i++)
            {
                int id = batch * 100 + i;
                if (i > 0) setup.Append(',');
                string k2 = id % 3 == 0 ? "1a" : (id % 2 == 0 ? "2" : "10");
                setup.Append($"({id % 50}, '{k2}', {id})");
            }
            setup.AppendLine(";");
        }
        setup.AppendLine("INSERT INTO #b VALUES (10, '10', 100), (12, '1a', 200), (14, '2', 300);");
        for (int batch = 0; batch < 30; batch++)
        {
            setup.Append("INSERT INTO #b VALUES ");
            for (int i = 0; i < 100; i++)
            {
                int id = batch * 100 + i;
                if (i > 0) setup.Append(',');
                string k2 = id % 3 == 0 ? "1a" : (id % 2 == 0 ? "2" : "10");
                setup.Append($"({50 + (id % 200)}, '{k2}', {id})");
            }
            setup.AppendLine(";");
        }

        await RunSqlAsync(ev, setup.ToString());

        var res = await RunSqlAsync(ev, """
            SELECT COUNT(*) AS MatchCount, SUM(a.aval + b.bval) AS TotalSum
            FROM #a a
            INNER MERGE JOIN #b b ON a.k1 = b.k1 AND a.k2 = b.k2;
            """);
        var mergeRow = Assert.Single(res.SelectMany(t => t.Rows));
        var mergeCount = Convert.ToDecimal(mergeRow["MatchCount"]);
        var mergeSum = Convert.ToDecimal(mergeRow["TotalSum"]);

        var oracle = await RunSqlAsync(ev, """
            SELECT COUNT(*) AS MatchCount, SUM(a.aval + b.bval) AS TotalSum
            FROM #a a
            INNER HASH JOIN #b b ON a.k1 = b.k1 AND a.k2 = b.k2;
            """);
        var hashRow = Assert.Single(oracle.SelectMany(t => t.Rows));
        Assert.True(mergeCount > 0);
        Assert.Equal(Convert.ToDecimal(hashRow["MatchCount"]), mergeCount);
        Assert.Equal(Convert.ToDecimal(hashRow["TotalSum"]), mergeSum);
    }

    [Fact]
    public async Task ExternalWindowEngine_DefaultRangeRunningSum_IncludesPeersOnTies()
    {
        var ev = CreateEvaluator(windowSpillThreshold: 2);
        var res = await RunSqlAsync(ev, """
            CREATE TABLE #w (k INT, v INT);
            INSERT INTO #w VALUES (1, 10), (1, 20), (2, 5), (2, 15), (3, 100);

            SELECT k, v,
                   SUM(v) OVER (ORDER BY k) AS RangeSum,
                   SUM(v) OVER (ORDER BY k ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RowsSum
            FROM #w
            ORDER BY k, v;
            """);

        var rows = Assert.Single(res).Rows;
        Assert.Equal(5, rows.Count);
        // RANGE peers for k=1: both rows get 30; for k=2: both rows get 50; for k=3: 150.
        Assert.Equal([30m, 30m, 50m, 50m, 150m], rows.Select(r => Convert.ToDecimal(r["RangeSum"])).ToArray());
        // ROWS frame accumulates strictly row-by-row.
        Assert.Equal([10m, 30m, 35m, 50m, 150m], rows.Select(r => Convert.ToDecimal(r["RowsSum"])).ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10000)]
    public async Task RangeAggregates_DescendingWithNullPeers_MatchExpectedResults(int threshold)
    {
        var ev = CreateEvaluator(windowSpillThreshold: threshold);
        var result = await RunSqlAsync(ev, """
            CREATE TABLE #w (k INT, v DECIMAL);
            INSERT INTO #w VALUES (1, 10), (1, 20), (2, 5), (2, 15), (3, NULL), (3, NULL);
            SELECT k,
                   SUM(v) OVER (ORDER BY k DESC) AS total,
                   COUNT(v) OVER (ORDER BY k DESC) AS n,
                   AVG(v) OVER (ORDER BY k DESC) AS mean,
                   MIN(v) OVER (ORDER BY k DESC) AS lo,
                   MAX(v) OVER (ORDER BY k DESC RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS hi
            FROM #w ORDER BY k DESC;
            """);
        var rows = Assert.Single(result).Rows;
        Assert.Equal(6, rows.Count);
        foreach (var row in rows)
        {
            var key = Convert.ToDecimal(row["k"]);
            Assert.Equal(key == 3m ? 0m : key == 2m ? 2m : 4m, row["n"]);
            if (key == 3m)
            {
                Assert.Null(row["total"]);
                Assert.Null(row["mean"]);
                Assert.Null(row["lo"]);
                Assert.Null(row["hi"]);
            }
            else
            {
                Assert.Equal(key == 2m ? 20m : 50m, row["total"]);
                Assert.Equal(key == 2m ? 10m : 12.5m, row["mean"]);
                Assert.Equal(5m, row["lo"]);
                Assert.Equal(key == 2m ? 15m : 20m, row["hi"]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalWindowEngine_LargeRangePeers_SpillAndCleanUp(bool stopEarly)
    {
        var ev = CreateEvaluator(windowSpillThreshold: 2);
        var aggregate = new AggregateEngine(ev, NullLogger.Instance);
        var engine = new ExternalWindowEngine(ev, new WindowEngine(ev, aggregate, NullLogger.Instance), NullLogger.Instance);
        const string sql = "SELECT SUM(v) OVER (PARTITION BY p ORDER BY k) AS total FROM #w;";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var stmt = Assert.IsType<SelectStatement>(Assert.Single(script.Statements));
        var function = Assert.IsType<FunctionCallExpression>(stmt.Columns[0].Expression);
        var resultKey = $"WINDOW_{function.ToSql().ToUpperInvariant()}";
        var schema = new TableSchema(["p", "k", "v"]);
        var input = (from partition in Enumerable.Range(1, 2)
                     from key in Enumerable.Range(1, 2)
                     from value in Enumerable.Range(1, 5)
                     select new Row(schema, [(decimal)partition, (decimal)key, (decimal)value])).ToAsyncEnumerable();

        var count = 0;
        await foreach (var row in engine.ApplyWindowFunctionsExternal(input, stmt))
        {
            Assert.Equal(Convert.ToDecimal(row["k"]) * 15m, Convert.ToDecimal(row[resultKey]));
            Assert.NotEmpty(Directory.EnumerateFiles(ev.SpillStore.RootPath, "*window_peers*", SearchOption.AllDirectories));
            count++;
            if (stopEarly) break;
        }
        Assert.Equal(stopEarly ? 1 : 20, count);
        Assert.Empty(Directory.EnumerateFiles(ev.SpillStore.RootPath, "*window_peers*", SearchOption.AllDirectories));
    }
}
