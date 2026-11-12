using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// <c>ORDER BY ... NULLS FIRST | NULLS LAST</c>. Without either, <c>NULL</c> sorts lowest: first
/// for <c>ASC</c> and last for <c>DESC</c>.
/// </summary>
public sealed class NullsOrderingTests
{
    // a = 2, NULL, 1, NULL, 3
    private const string Setup =
        "CREATE TABLE #t (g VARCHAR, a INT); INSERT INTO #t VALUES ('x', 2), ('x', NULL), ('y', 1), ('y', NULL), ('x', 3); ";

    [Theory]
    [InlineData("ORDER BY a", "N,N,1,2,3")]
    [InlineData("ORDER BY a DESC", "3,2,1,N,N")]
    [InlineData("ORDER BY a NULLS FIRST", "N,N,1,2,3")]
    [InlineData("ORDER BY a NULLS LAST", "1,2,3,N,N")]
    [InlineData("ORDER BY a ASC NULLS LAST", "1,2,3,N,N")]
    [InlineData("ORDER BY a DESC NULLS FIRST", "N,N,3,2,1")]
    [InlineData("ORDER BY a DESC NULLS LAST", "3,2,1,N,N")]
    [InlineData("order by a desc nulls first", "N,N,3,2,1")]
    // Each key has its own placement.
    [InlineData("ORDER BY g DESC, a NULLS LAST", "1,N,2,3,N")]
    [InlineData("ORDER BY g NULLS LAST, a DESC NULLS FIRST", "N,3,2,N,1")]
    // An expression key.
    [InlineData("ORDER BY a * 2 DESC NULLS FIRST", "N,N,3,2,1")]
    public async Task AQuerySortPlacesNullWhereItIsTold(string orderBy, string expected)
    {
        var rows = await Run(Setup + $"SELECT a FROM #t {orderBy};");
        Assert.Equal(expected, Join(rows, "a"));
    }

    [Theory]
    [InlineData("SELECT TOP 2 a FROM #t ORDER BY a NULLS LAST", "1,2")]
    [InlineData("SELECT TOP 3 a FROM #t ORDER BY a DESC NULLS FIRST", "N,N,3")]
    [InlineData("SELECT TOP 4 a FROM #t ORDER BY a DESC", "3,2,1,N")]
    [InlineData("SELECT a FROM #t ORDER BY a NULLS LAST LIMIT 2 OFFSET 2", "3,N")]
    public async Task TopNKeepsTheRowsOfThePlacedOrder(string query, string expected)
    {
        var rows = await Run(Setup + query + ";");
        Assert.Equal(expected, Join(rows, "a"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASpilledSortPlacesNullTheSameWay(bool descending)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.ExternalSortChunkSize = 16;
        var values = string.Join(", ", Enumerable.Range(1, 200).Select(n => n % 5 == 0 ? "(NULL)" : $"({n})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #big (a INT); INSERT INTO #big VALUES {values};").TokenizeToScript());
        // DESC NULLS FIRST and ASC NULLS LAST are the two orders the default does not give.
        await evaluator.Evaluate(new Lexer(
            $"SELECT a FROM #big ORDER BY a {(descending ? "DESC NULLS FIRST" : "ASC NULLS LAST")};").TokenizeToScript());

        var keys = evaluator.LastResult!.Rows.Select(r => r["a"]).ToArray();
        Assert.Equal(200, keys.Length);
        var nulls = descending ? keys.Take(40) : keys.Skip(160);
        var values2 = (descending ? keys.Skip(40) : keys.Take(160)).Select(Convert.ToDecimal).ToArray();
        Assert.All(nulls, k => Assert.True(k is null or DBNull));
        Assert.Equal(descending ? values2.OrderByDescending(v => v) : values2.OrderBy(v => v), values2);
    }

    [Theory]
    [InlineData("ROW_NUMBER() OVER (ORDER BY a NULLS LAST)", "4,5,1,2,3")]
    [InlineData("ROW_NUMBER() OVER (ORDER BY a DESC NULLS FIRST)", "1,2,5,4,3")]
    [InlineData("RANK() OVER (ORDER BY a NULLS LAST)", "4,4,1,2,3")]
    [InlineData("ROW_NUMBER() OVER (PARTITION BY g ORDER BY a NULLS LAST)", "3,2,1,1,2")]
    [InlineData("SUM(a) OVER (ORDER BY a NULLS LAST)", "6,6,1,3,6")]
    [InlineData("FIRST_VALUE(a) OVER (ORDER BY a NULLS LAST)", "1,1,1,1,1")]
    [InlineData("LAG(a) OVER (ORDER BY a NULLS LAST)", "3,N,N,1,2")]
    public async Task AWindowSortPlacesNullWhereItIsTold(string expression, string expected)
    {
        // Output rows: (x, NULL), (y, NULL), (y, 1), (x, 2), (x, 3).
        var rows = await Run(Setup + $"SELECT g, a, {expression} AS v FROM #t ORDER BY a, g;");
        if (expression.StartsWith("LAG", StringComparison.Ordinal) || expression.StartsWith("ROW_NUMBER() OVER (ORDER", StringComparison.Ordinal))
        {
            // The two NULL rows are peers: either may come first.
            var nullValues = rows.Take(2).Select(r => Text(r["v"])).OrderBy(v => v, StringComparer.Ordinal);
            var want = expected.Split(',').Take(2).OrderBy(v => v, StringComparer.Ordinal);
            Assert.Equal(want, nullValues);
            Assert.Equal(expected.Split(',').Skip(2), rows.Skip(2).Select(r => Text(r["v"])));
            return;
        }
        Assert.Equal(expected, Join(rows, "v"));
    }

    [Fact]
    public async Task ASpilledWindowPlacesNullTheSameWay()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.WindowSpillThreshold = 20;
        var values = string.Join(", ", Enumerable.Range(1, 300).Select(n => n % 10 == 0 ? $"({n % 3}, NULL)" : $"({n % 3}, {n})"));
        await evaluator.Evaluate(new Lexer($"CREATE TABLE #big (g INT, a INT); INSERT INTO #big VALUES {values};").TokenizeToScript());
        await evaluator.Evaluate(new Lexer(
            "SELECT g, a, ROW_NUMBER() OVER (PARTITION BY g ORDER BY a NULLS LAST) AS rn, " +
            "COUNT(*) OVER (PARTITION BY g) AS c FROM #big;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows.ToArray();
        Assert.Equal(300, rows.Length);
        // In each partition the 10 NULL rows hold the last 10 row numbers.
        Assert.All(rows.Where(r => r["a"] is null or DBNull),
            r => Assert.True(Convert.ToInt32(r["rn"]) > Convert.ToInt32(r["c"]) - 10));
        Assert.All(rows.Where(r => r["a"] is not (null or DBNull)),
            r => Assert.True(Convert.ToInt32(r["rn"]) <= Convert.ToInt32(r["c"]) - 10));
    }

    [Fact]
    public async Task AnOrderedAggregatePlacesNullWhereItIsTold()
    {
        var rows = await Run(Setup +
            "SELECT STRING_AGG(ISNULL(CAST(a AS VARCHAR), 'N'), ',') WITHIN GROUP (ORDER BY a DESC NULLS FIRST) AS v FROM #t;");
        Assert.Equal("N,N,3,2,1", rows[0]["v"]);
    }

    [Theory]
    [InlineData("SELECT a FROM #t ORDER BY a NULLS;")]
    [InlineData("SELECT a FROM #t ORDER BY a NULLS MIDDLE;")]
    [InlineData("SELECT a FROM #t ORDER BY a NULLS FIRST DESC;")]
    public async Task ABadPlacementIsASyntaxError(string query)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Run(Setup + query));
    }

    [Fact]
    public async Task AColumnNamedNullsStillSorts()
    {
        var rows = await Run(
            "CREATE TABLE #n (nulls INT); INSERT INTO #n VALUES (2), (1); SELECT nulls FROM #n ORDER BY nulls DESC;");
        Assert.Equal("2,1", Join(rows, "nulls"));
    }

    [Theory]
    [InlineData("SELECT a FROM t ORDER BY a DESC NULLS LAST, b NULLS FIRST;", "a DESC NULLS LAST, b ASC NULLS FIRST")]
    [InlineData("SELECT ROW_NUMBER() OVER (ORDER BY a NULLS LAST) AS rn FROM t;", "ORDER BY a ASC NULLS LAST")]
    [InlineData("SELECT a FROM t ORDER BY a DESC;", "ORDER BY a DESC")]
    public void ThePlacementSurvivesSerialization(string sql, string expected)
    {
        var statement = Parse(sql);
        var text = statement.ToSql();
        Assert.Contains(expected, text);
        if (!expected.Contains("NULLS", StringComparison.Ordinal))
            Assert.DoesNotContain("NULLS", text);
        // What the serializer writes parses back to the same text.
        Assert.Equal(text, Parse(text + ";").ToSql());
    }

    [Fact]
    public void TheFormatterKeepsThePlacement()
    {
        const string sql =
            "SELECT a, ROW_NUMBER() OVER (PARTITION BY g ORDER BY a NULLS LAST) AS rn FROM t ORDER BY a DESC NULLS LAST, b NULLS FIRST;";
        var formatted = ETL_SQL.Core.Formatting.SqlFormatter.Format(sql, new ETL_SQL.Core.Formatting.FormatterOptions());
        Assert.Equal(Parse(sql).ToSql(), Parse(formatted).ToSql());
    }

    [Theory]
    [InlineData("POSTGRES")]
    [InlineData("ORACLE")]
    public void ASourceThatPlacesNullIsSentThePlacement(string dialect)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var compiled = new QueryCompiler(evaluator).CompileQuery(
            Parse("SELECT a FROM db.t ORDER BY a DESC NULLS LAST, b NULLS FIRST;"), dialect);
        Assert.Contains("DESC NULLS LAST", compiled.Sql);
        Assert.Contains("ASC NULLS FIRST", compiled.Sql);
    }

    [Fact]
    public void ASourceThatCannotPlaceNullIsNotSentTheQuery()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var compiler = new QueryCompiler(evaluator);
        // The pushdown check reads this failure as "sort in the engine".
        Assert.Throws<NotSupportedException>(() =>
            compiler.CompileQuery(Parse("SELECT a FROM db.t ORDER BY a NULLS LAST;"), "MSSQL"));
        // A plain ORDER BY still compiles.
        Assert.Contains("ORDER BY", compiler.CompileQuery(Parse("SELECT a FROM db.t ORDER BY a DESC;"), "MSSQL").Sql);
    }

    private static Statement Parse(string sql) =>
        Assert.Single(new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements);

    private static string Join(Row[] rows, string column) => string.Join(",", rows.Select(r => Text(r[column])));

    private static string Text(object? value) => value switch
    {
        null or DBNull => "N",
        string s => s,
        _ => Convert.ToDecimal(value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
    };

    private static async Task<Row[]> Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!.Rows.ToArray();
    }
}
