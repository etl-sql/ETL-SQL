using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// An outer join returns a row with no match carrying NULL for the other side. When that side had
/// no rows, nothing named its columns, so any name read NULL on the row and a misspelt one passed.
/// The side's columns now come from the table reference: a table function's result, a
/// <c>PIVOT</c> or <c>UNPIVOT</c> list, <c>SELECT *</c> over a join, a <c>UNION</c>, or a file's
/// header.
/// </summary>
public sealed class EmptyOuterJoinSideTests
{
    private const string Setup =
        "CREATE TABLE #a (id INT, x INT); CREATE TABLE #b (id INT, y INT); CREATE TABLE #e (id INT, k VARCHAR, x INT); ";

    private const string Pivot = "(SELECT id, k, x FROM #e) s PIVOT (MAX(x) FOR k IN ([q1], [q2])) AS p ON 1 = 1";
    private const string Unpivot = "(SELECT id, x FROM #a) s UNPIVOT (val FOR nm IN (x)) AS u ON 1 = 1";
    private const string StarJoin = "(SELECT * FROM #a JOIN #b ON #a.id = #b.id) s ON s.id = o.id";
    private const string AliasStar = "(SELECT a.* FROM #a a JOIN #b b ON a.id = b.id) s ON s.id = o.id";
    private const string Union = "(SELECT id, x FROM #a UNION ALL SELECT id, y FROM #b) s ON s.id = o.id";

    [Theory]
    [InlineData("g.valeu", "GENERATE_SERIES(1, 0) g ON g.value = o.id")]
    [InlineData("s.bogus", Pivot)]
    // PIVOT drops the pivot column and the aggregated column.
    [InlineData("s.k", Pivot)]
    [InlineData("s.x", Pivot)]
    [InlineData("s.vall", Unpivot)]
    // UNPIVOT drops the columns it turns into rows.
    [InlineData("s.x", Unpivot)]
    [InlineData("s.bogus", StarJoin)]
    // `a.*` returns the columns of a only.
    [InlineData("s.y", AliasStar)]
    [InlineData("s.bogus", Union)]
    // A UNION takes its names from its first query.
    [InlineData("s.y", Union)]
    public async Task AMisspeltNameOnAnEmptyRightSideFails(string column, string rightSide)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            Run(Setup + $"SELECT {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN {rightSide};"));
        Assert.Contains($"Unknown column '{column}'", failure.ToString());
    }

    [Theory]
    [InlineData("g.value", "GENERATE_SERIES(1, 0) g ON g.value = o.id")]
    [InlineData("s.q1", Pivot)]
    [InlineData("s.id", Pivot)]
    [InlineData("s.val", Unpivot)]
    [InlineData("s.nm", Unpivot)]
    [InlineData("s.y", StarJoin)]
    [InlineData("s.x", StarJoin)]
    [InlineData("s.x", AliasStar)]
    [InlineData("s.x", Union)]
    public async Task ARealColumnOfAnEmptyRightSideReadsNull(string column, string rightSide)
    {
        var rows = await Run(Setup + $"SELECT o.id AS id, {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN {rightSide};");
        Assert.True(Assert.Single(rows)["v"] is null or DBNull);
    }

    [Theory]
    [InlineData("RIGHT JOIN", "l.nmae", false)]
    [InlineData("RIGHT JOIN", "l.name", true)]
    [InlineData("FULL JOIN", "l.nmae", false)]
    [InlineData("FULL JOIN", "l.name", true)]
    public async Task AnEmptyFileOnTheLeftNamesItsColumns(string joinType, string column, bool known)
    {
        await WithHeaderOnlyFile(async connection =>
        {
            var sql = connection + $"SELECT {column} AS v FROM f.FILE l {joinType} (SELECT 1 AS id) r ON l.id = r.id;";
            if (known)
                Assert.True(Assert.Single(await Run(sql))["v"] is null or DBNull);
            else
                Assert.Contains($"Unknown column '{column}'", (await Assert.ThrowsAnyAsync<Exception>(() => Run(sql))).ToString());
        });
    }

    [Theory]
    [InlineData("RIGHT JOIN", "g.valeu", false)]
    [InlineData("RIGHT JOIN", "g.value", true)]
    [InlineData("FULL JOIN", "g.valeu", false)]
    [InlineData("FULL JOIN", "g.value", true)]
    public async Task AnEmptyTableFunctionOnTheLeftNamesItsColumns(string joinType, string column, bool known)
    {
        var sql = $"SELECT {column} AS v FROM GENERATE_SERIES(1, 0) g {joinType} (SELECT 1 AS id) r ON g.value = r.id;";
        if (known)
            Assert.True(Assert.Single(await Run(sql))["v"] is null or DBNull);
        else
            Assert.Contains($"Unknown column '{column}'", (await Assert.ThrowsAnyAsync<Exception>(() => Run(sql))).ToString());
    }

    [Theory]
    [InlineData("s.nmae", false)]
    [InlineData("s.name", true)]
    public async Task AnEmptyFileBehindSelectStarNamesItsColumns(string column, bool known)
    {
        await WithHeaderOnlyFile(async connection =>
        {
            var sql = connection + $"SELECT {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN (SELECT * FROM f.FILE) s ON s.id = o.id;";
            if (known)
                Assert.True(Assert.Single(await Run(sql))["v"] is null or DBNull);
            else
                Assert.Contains($"Unknown column '{column}'", (await Assert.ThrowsAnyAsync<Exception>(() => Run(sql))).ToString());
        });
    }

    // The sides whose names depend on what they read (SQ03): a pivot whose values are a variable,
    // and MATCH_RECOGNIZE over no rows or with no match. Its names answer to its own alias.
    private const string VariablePivot = "(SELECT id, k, x FROM #e) s PIVOT (MAX(x) FOR k IN (@q)) AS p ON 1 = 1";
    private const string MatchNoRows =
        "#e MATCH_RECOGNIZE (ORDER BY id MEASURES A.x AS ax ONE ROW PER MATCH PATTERN (A) DEFINE A AS A.x > 0) AS s ON 1 = 1";
    private const string MatchNoMatch =
        "#m MATCH_RECOGNIZE (ORDER BY id MEASURES A.x AS ax ALL ROWS PER MATCH PATTERN (A) DEFINE A AS A.x > 100) AS s ON 1 = 1";
    private const string DynamicSetup =
        "DECLARE @q VARCHAR = 'q1'; CREATE TABLE #m (id INT, x INT); INSERT INTO #m VALUES (1, 5); ";

    [Theory]
    [InlineData("s.bogus", VariablePivot)]
    [InlineData("s.axx", MatchNoRows)]
    // ONE ROW PER MATCH returns the measures only.
    [InlineData("s.x", MatchNoRows)]
    [InlineData("s.axx", MatchNoMatch)]
    public async Task AMisspeltNameOnAnEmptyRowDependentSideFails(string column, string rightSide)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            Run(Setup + DynamicSetup + $"SELECT {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN {rightSide};"));
        Assert.Contains($"Unknown column '{column}'", failure.ToString());
    }

    [Theory]
    [InlineData("s.q1", VariablePivot)]
    [InlineData("p.q1", VariablePivot)]
    [InlineData("s.ax", MatchNoRows)]
    [InlineData("s.ax", MatchNoMatch)]
    // ALL ROWS PER MATCH returns the source's columns and the match columns too.
    [InlineData("s.x", MatchNoMatch)]
    [InlineData("s.CLASSIFIER", MatchNoMatch)]
    public async Task ARealColumnOfAnEmptyRowDependentSideReadsNull(string column, string rightSide)
    {
        var rows = await Run(Setup + DynamicSetup + $"SELECT o.id AS id, {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN {rightSide};");
        Assert.True(Assert.Single(rows)["v"] is null or DBNull);
    }

    [Theory]
    [InlineData("s.axx", false)]
    [InlineData("s.ax", true)]
    public async Task AnEmptyMatchRecognizeOnTheLeftNamesItsColumns(string column, bool known)
    {
        var sql = Setup + $"SELECT {column} AS v FROM #e MATCH_RECOGNIZE (ORDER BY id MEASURES A.x AS ax ONE ROW PER MATCH PATTERN (A) DEFINE A AS A.x > 0) AS s RIGHT JOIN (SELECT 1 AS id) r ON 1 = 1;";
        if (known)
            Assert.True(Assert.Single(await Run(sql))["v"] is null or DBNull);
        else
            Assert.Contains($"Unknown column '{column}'", (await Assert.ThrowsAnyAsync<Exception>(() => Run(sql))).ToString());
    }

    [Theory]
    [InlineData("LEFT", "j.kye", false)]
    [InlineData("LEFT", "j.key", true)]
    [InlineData("RIGHT", "j.kye", false)]
    [InlineData("RIGHT", "j.key", true)]
    public async Task AnEmptyDatabaseTableFunctionNamesItsColumns(string side, string column, bool known)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sq03-fn-{Guid.NewGuid():N}.db");
        try
        {
            var connection = $"CREATE CONNECTION db AS SQLITE(DATABASE='{path.Replace('\\', '/')}'); ";
            // SQLite's json_each is a table function: an empty array returns no rows.
            var sql = side == "LEFT"
                ? connection + $"SELECT {column} AS v FROM (SELECT 1 AS id) o LEFT JOIN db.json_each('[]') j ON 1 = 1;"
                : connection + $"SELECT {column} AS v FROM db.json_each('[]') j RIGHT JOIN (SELECT 1 AS id) o ON 1 = 1;";
            if (known)
                Assert.True(Assert.Single(await Run(sql))["v"] is null or DBNull);
            else
                Assert.Contains($"Unknown column '{column}'", (await Assert.ThrowsAnyAsync<Exception>(() => Run(sql))).ToString());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public async Task ASideWithRowsIsNotAskedAgain()
    {
        // The matched row and the unmatched row both read the pivot's columns.
        var rows = await Run(
            "CREATE TABLE #d (id INT, k VARCHAR, x INT); INSERT INTO #d VALUES (1, 'q1', 5), (1, 'q2', 6); " +
            "SELECT o.id AS id, s.q1 AS v FROM (SELECT 1 AS id UNION ALL SELECT 2 AS id) o " +
            "LEFT JOIN (SELECT id, k, x FROM #d) s PIVOT (MAX(x) FOR k IN ([q1], [q2])) AS p ON s.id = o.id ORDER BY o.id;");

        Assert.Equal(2, rows.Length);
        Assert.Equal(5m, Convert.ToDecimal(rows[0]["v"]));
        Assert.True(rows[1]["v"] is null or DBNull);
    }

    private static async Task WithHeaderOnlyFile(Func<string, Task> body)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sq03-empty-{Guid.NewGuid():N}.csv");
        try
        {
            await System.IO.File.WriteAllTextAsync(path, "id|name\n");
            await body($"CREATE CONNECTION f AS FLATFILE('{path}', HEADER=ON, DELIMITER=PIPE); ");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
