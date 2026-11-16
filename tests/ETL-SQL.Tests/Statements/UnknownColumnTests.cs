using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// A SELECT over tables whose columns are declared must reject a name that is not one of them. An
/// unknown name used to read as NULL, so a misspelt column returned a full result of NULLs and the
/// script succeeded.
/// </summary>
public sealed class UnknownColumnTests
{
    private const string Setup = @"
        CREATE TABLE #t (id INT, name VARCHAR(20), g INT, d DATETIME);
        INSERT INTO #t VALUES (1, 'a', 2, '2026-01-01'), (2, 'b', 1, '2026-01-02');
        CREATE TABLE #u (id INT, label VARCHAR(20));
        INSERT INTO #u VALUES (1, 'x');
    ";

    [Theory]
    [InlineData("SELECT id, nmae FROM #t;", "nmae")]
    [InlineData("SELECT id FROM #t WHERE nmae = 'a';", "nmae")]
    [InlineData("SELECT id FROM #t ORDER BY nmae;", "nmae")]
    [InlineData("SELECT g, COUNT(*) AS n FROM #t GROUP BY gg;", "gg")]
    [InlineData("SELECT UPPER(nmae) AS n FROM #t;", "nmae")]
    [InlineData("SELECT t.nmae FROM #t t;", "t.nmae")]
    [InlineData("SELECT t.id, u.lable FROM #t t JOIN #u u ON t.id = u.id;", "u.lable")]
    [InlineData("SELECT t.id FROM #t t JOIN #u u ON t.id = u.idd;", "u.idd")]
    public async Task UnknownColumnFailsTheStatement(string query, string unknown)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Run(Setup + query));
        Assert.Contains(unknown, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT id, name FROM #t ORDER BY id;")]
    [InlineData("SELECT t.*, u.label FROM #t t JOIN #u u ON t.id = u.id;")]
    [InlineData("SELECT * FROM #t;")]
    [InlineData("SELECT T.ID, NAME FROM #t t ORDER BY t.Id;")]
    [InlineData("SELECT id AS k FROM #t ORDER BY k;")]
    [InlineData("SELECT g, COUNT(*) AS n FROM #t GROUP BY g HAVING COUNT(*) > 0 ORDER BY n;")]
    [InlineData("SELECT t.id, u.label FROM #t t LEFT JOIN #u u ON t.id = u.id ORDER BY t.id;")]
    [InlineData("SELECT DATEADD(day, 1, d) AS nd, DATEPART(month, d) AS m, DATEDIFF(day, d, d) AS z FROM #t;")]
    [InlineData("SELECT EXTRACT(YEAR FROM d) AS y, CAST(id AS VARCHAR(5)) AS s FROM #t;")]
    [InlineData("DECLARE @x INT = 1; SELECT id FROM #t WHERE id = @x;")]
    [InlineData("SELECT id FROM #t WHERE id IN (SELECT id FROM #u WHERE #u.id = #t.id);")]
    [InlineData("SELECT id FROM #t t WHERE EXISTS (SELECT 1 FROM #u u WHERE u.id = t.id);")]
    [InlineData("SELECT id, ROW_NUMBER() OVER (PARTITION BY g ORDER BY id) AS rn FROM #t;")]
    [InlineData("SELECT id, GETDATE() AS now1, CURRENT_TIMESTAMP AS now2 FROM #t;")]
    [InlineData("SELECT CASE WHEN g > 1 THEN 'hi' ELSE 'lo' END AS band FROM #t;")]
    public async Task KnownNamesStillRun(string query)
    {
        await Run(Setup + query);
    }

    [Theory]
    [InlineData("UPDATE #t SET nmae = 'z';", "nmae")]
    [InlineData("UPDATE #t SET name = 'z', gg = 1 WHERE id = 1;", "gg")]
    public async Task UpdateOfAnUnknownColumnFailsTheStatement(string statement, string unknown)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Run(Setup + statement));
        Assert.Contains($"Unknown column '{unknown}'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateOfKnownColumnsStillRuns()
    {
        await Run(Setup + "UPDATE #t SET NAME = 'z', g = g + 1 WHERE id = 1;");
    }

    [Theory]
    [InlineData("SELECT id FROM #t WHERE id IN (SELECT id, label FROM #u);")]
    [InlineData("SELECT id FROM #t WHERE id NOT IN (SELECT 1, 2);")]
    [InlineData("SELECT id FROM #t WHERE id IN (SELECT * FROM #u);")]
    public async Task InSubqueryWithMoreThanOneColumnFailsTheStatement(string query)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Run(Setup + query));
        Assert.Contains("one column", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task Run(string script)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
    }
}
