using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// <c>DECLARE @x LIST = (SELECT col ...)</c> used to be evaluated as a scalar subquery and keep
/// only the first row, so a FOREACH over it handled one item with no error (2026-11-21, found
/// in the dynamic SQL and SFTP bursting recipes).
/// </summary>
public sealed class DeclareListQueryTests
{
    private static Evaluator NewEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private const string Setup = @"
        CREATE TABLE #regions (Region VARCHAR(20), Sort INT);
        INSERT INTO #regions VALUES ('EMEA', 2), ('APAC', 1), ('LATAM', 3), ('EMEA', 4);";

    [Fact]
    public async Task ListFromQuery_HoldsEveryRow()
    {
        var eval = NewEvaluator();
        await eval.Evaluate(Parse(Setup + @"
            DECLARE @regions LIST = (SELECT DISTINCT Region FROM #regions ORDER BY Region);
            CREATE TABLE #seen (Region VARCHAR(20));
            FOREACH @r IN @regions
            BEGIN
                INSERT INTO #seen VALUES (@r);
            END
            SELECT Region FROM #seen;"));

        var seen = eval.LastResult!.Rows.Select(r => r["Region"]?.ToString()).ToList();
        Assert.Equal(new[] { "APAC", "EMEA", "LATAM" }, seen);
    }

    [Fact]
    public async Task ListFromQuery_KeepsDuplicatesAndNulls()
    {
        var eval = NewEvaluator();
        await eval.Evaluate(Parse(Setup + @"
            INSERT INTO #regions VALUES (NULL, 5);
            DECLARE @regions LIST = (SELECT Region FROM #regions ORDER BY Sort);"));

        var list = Assert.IsAssignableFrom<IEnumerable<object?>>(eval.VarContext.CurrentVariables["@regions"]);
        Assert.Equal(new object?[] { "APAC", "EMEA", "LATAM", "EMEA", null }, list.ToArray());
    }

    [Fact]
    public async Task ListFromEmptyQuery_IsEmpty()
    {
        var eval = NewEvaluator();
        await eval.Evaluate(Parse(Setup + @"
            DECLARE @none LIST = (SELECT Region FROM #regions WHERE Sort > 100);"));

        var list = Assert.IsAssignableFrom<IEnumerable<object?>>(eval.VarContext.CurrentVariables["@none"]);
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListFromMultiColumnQuery_Fails()
    {
        var eval = NewEvaluator();
        var ex = await Assert.ThrowsAnyAsync<ExecutionException>(() => eval.Evaluate(Parse(Setup + @"
            DECLARE @pairs LIST = (SELECT Region, Sort FROM #regions);")));
        Assert.Contains("must return one column", ex.Message);
    }

    [Fact]
    public async Task ScalarFromQuery_StillTakesTheValue()
    {
        var eval = NewEvaluator();
        await eval.Evaluate(Parse(Setup + @"
            DECLARE @n INT = (SELECT COUNT(*) FROM #regions);"));

        Assert.Equal(4m, System.Convert.ToDecimal(eval.VarContext.CurrentVariables["@n"]));
    }
}
