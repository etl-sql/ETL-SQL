using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions;

/// <summary>
/// SQL's scalar functions return NULL when an argument is NULL. Many of these answered with an
/// empty string, 0, 1 or an exception instead, so a NULL in the data turned into a value.
/// </summary>
public sealed class NullPropagationTests
{
    [Theory]
    [InlineData("REPLACE('abc', NULL, 'x')")]
    [InlineData("REPLACE('abc', 'b', NULL)")]
    [InlineData("REPLACE(NULL, 'b', 'x')")]
    [InlineData("SUBSTRING('abc', NULL, 2)")]
    [InlineData("SUBSTRING('abc', 1, NULL)")]
    [InlineData("LEFT('abc', NULL)")]
    [InlineData("RIGHT('abc', NULL)")]
    [InlineData("CHARINDEX(NULL, 'abc')")]
    [InlineData("CHARINDEX('a', NULL)")]
    [InlineData("INSTR('abc', NULL)")]
    [InlineData("STUFF('abc', 1, 1, NULL)")]
    [InlineData("STUFF(NULL, 1, 1, 'x')")]
    [InlineData("STR(NULL)")]
    [InlineData("QUOTENAME(NULL)")]
    [InlineData("TRANSLATE('abc', NULL, 'x')")]
    [InlineData("PATINDEX('%b%', NULL)")]
    [InlineData("STRING_ESCAPE(NULL, 'json')")]
    [InlineData("LOG(NULL)")]
    [InlineData("ATAN2(NULL, 1)")]
    [InlineData("ROUND(NULL, 2)")]
    [InlineData("ROUND(1.5, NULL)")]
    [InlineData("DATETIMEFROMPARTS(2026, NULL, 1, 0, 0, 0, 0)")]
    [InlineData("DATEPART(month, NULL)")]
    [InlineData("DATEDIFF(day, NULL, '2026-01-02')")]
    [InlineData("DATEDIFF(day, '2026-01-01', NULL)")]
    [InlineData("EOMONTH(NULL)")]
    [InlineData("HASHBYTES('SHA2_256', NULL)")]
    [InlineData("SIMILARITY(NULL, NULL)")]
    [InlineData("SIMILARITY('a', NULL)")]
    [InlineData("LEVENSHTEIN(NULL, NULL)")]
    [InlineData("POSITION('b' IN NULL)")]
    [InlineData("POSITION(NULL IN 'abc')")]
    [InlineData("CAST('2026-01-01' AS DATETIME) AT TIME ZONE NULL")]
    [InlineData("CAST(NULL AS DATETIME) AT TIME ZONE 'UTC'")]
    public async Task ANullArgumentGivesNull(string expression)
    {
        Assert.Null(await Scalar(expression));
    }

    [Theory]
    [InlineData("REPLACE('abc', 'b', 'x')", "axc")]
    [InlineData("LEFT('abc', 2)", "ab")]
    [InlineData("CHARINDEX('b', 'abc')", 2)]
    [InlineData("POSITION('b' IN 'abc')", 2)]
    [InlineData("DATEPART(month, '2026-03-04')", 3)]
    [InlineData("DATEDIFF(day, '2026-01-01', '2026-01-03')", 2)]
    [InlineData("ROUND(1.26, 1)", 1.3)]
    public async Task NonNullArgumentsStillAnswer(string expression, object expected)
    {
        var actual = await Scalar(expression);
        if (expected is string text) Assert.Equal(text, actual);
        else Assert.Equal(Convert.ToDecimal(expected), Convert.ToDecimal(actual));
    }

    private const string Words = @"
        CREATE TABLE #w (g INT, word VARCHAR(10), n INT);
        INSERT INTO #w VALUES (1, 'a', 1), (1, NULL, 2), (1, 'b', 3), (2, NULL, 4);
    ";

    [Theory]
    [InlineData("STRING_AGG(word, ',')")]
    [InlineData("LIST_AGG(word, ',')")]
    [InlineData("GROUP_CONCAT(word)")]
    public async Task StringAggregatesSkipNullAndGiveNullForNoValues(string aggregate)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(Words + $"SELECT g, {aggregate} AS v FROM #w GROUP BY g ORDER BY g;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("a,b", rows[0]["v"]);
        Assert.True(rows[1]["v"] is null or DBNull, $"A group with only NULLs gave '{rows[1]["v"]}'.");
    }

    [Theory]
    [InlineData("FIRST_VALUE(n) OVER (ORDER BY n ROWS BETWEEN 1 PRECEDING AND CURRENT ROW)", new[] { 1, 1, 2, 3 })]
    [InlineData("LAST_VALUE(n) OVER (ORDER BY n ROWS BETWEEN CURRENT ROW AND 1 FOLLOWING)", new[] { 2, 3, 4, 4 })]
    [InlineData("FIRST_VALUE(n) OVER (ORDER BY n)", new[] { 1, 1, 1, 1 })]
    [InlineData("NTH_VALUE(n, 2) OVER (ORDER BY n ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)", new[] { 2, 2, 2, 2 })]
    [InlineData("SUM(n) OVER (ORDER BY n)", new[] { 1, 3, 6, 10 })]
    [InlineData("SUM(n) OVER ()", new[] { 10, 10, 10, 10 })]
    [InlineData("COUNT(*) OVER (PARTITION BY g ORDER BY n)", new[] { 1, 2, 3, 1 })]
    public async Task WindowFunctionsReadTheirFrame(string window, int[] expected)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(Words + $"SELECT n, {window} AS v FROM #w ORDER BY n;").TokenizeToScript());

        var actual = evaluator.LastResult!.Rows.Select(row => Convert.ToInt32(row["v"])).ToArray();
        Assert.Equal(expected, actual);
    }

    private static async Task<object?> Scalar(string expression)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer($"SELECT {expression} AS v;").TokenizeToScript());
        var value = Assert.Single(evaluator.LastResult!.Rows)["v"];
        return value == DBNull.Value ? null : value;
    }
}
