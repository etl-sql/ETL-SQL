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
/// A temp table that spilled to disk read its text columns back through a number parse and a date
/// parse: <c>'00123'</c> came back as the number 123 and <c>'2026-01-05'</c> as a date.
/// </summary>
public sealed class SpilledTextColumnTests
{
    [Theory]
    [InlineData(1_000_000)]
    [InlineData(25)]
    public async Task ATextColumnReadsBackAsTheTextItWasGiven(int spillThresholdRows)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.TempTableSpillThresholdRows = spillThresholdRows;
        var values = string.Join(", ", Enumerable.Range(1, 200).Select(n =>
            $"({n}, '{n:00000}', '{n}', '2026-01-{(n % 9) + 1:00}', '{n}.50', '1e{n % 5}', 'x{n}')"));
        await evaluator.Evaluate(new Lexer(
            "CREATE TABLE #t (n INT, code VARCHAR, plain VARCHAR, day VARCHAR, price VARCHAR, sci VARCHAR, word VARCHAR); " +
            $"INSERT INTO #t VALUES {values}; SELECT n, code, plain, day, price, sci, word FROM #t;").TokenizeToScript());

        var rows = evaluator.LastResult!.Rows.ToArray();
        Assert.Equal(200, rows.Length);
        Assert.All(rows, row =>
        {
            var n = Convert.ToInt32(row["n"]);
            Assert.Equal($"{n:00000}", row["code"]);
            Assert.Equal($"{n}", row["plain"]);
            Assert.Equal($"2026-01-{(n % 9) + 1:00}", row["day"]);
            Assert.Equal($"{n}.50", row["price"]);
            Assert.Equal($"1e{n % 5}", row["sci"]);
            Assert.Equal($"x{n}", row["word"]);
        });
    }

    [Fact]
    public async Task ASpilledNumberColumnIsStillANumber()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.TempTableSpillThresholdRows = 25;
        var values = string.Join(", ", Enumerable.Range(1, 200).Select(n => $"({n}, {n}.25, '2026-01-{(n % 9) + 1:00}')"));
        await evaluator.Evaluate(new Lexer(
            "CREATE TABLE #t (n INT, amount DECIMAL(10,2), day DATE); " +
            $"INSERT INTO #t VALUES {values}; SELECT SUM(n) AS total, SUM(amount) AS amounts, MIN(day) AS first_day FROM #t;").TokenizeToScript());

        var row = Assert.Single(evaluator.LastResult!.Rows);
        Assert.Equal(20100m, Convert.ToDecimal(row["total"]));
        Assert.Equal(20150m, Convert.ToDecimal(row["amounts"]));
        Assert.Equal(new DateTime(2026, 1, 1), Convert.ToDateTime(row["first_day"]));
    }
}
