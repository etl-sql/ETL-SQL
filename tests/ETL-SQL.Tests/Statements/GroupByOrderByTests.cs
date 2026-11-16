using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// ORDER BY on a grouping key that the SELECT list does not return. The sort key used to be read
/// from the aggregated output row, where the key is absent, so every key was NULL and the groups
/// came back in arrival order.
/// </summary>
public sealed class GroupByOrderByTests
{
    // Groups arrive as g = 2, 3, 1 with counts 2, 3, 1, so arrival order (2, 3, 1) differs from
    // both sorted orders.
    private const string Setup = @"
        CREATE TABLE #t (id INT, g INT);
        INSERT INTO #t VALUES (1, 2), (2, 2), (3, 3), (4, 3), (5, 3), (6, 1);
    ";

    [Theory]
    [InlineData("SELECT COUNT(*) AS total FROM #t GROUP BY g ORDER BY g;", new[] { 1, 2, 3 })]
    [InlineData("SELECT COUNT(*) AS total FROM #t GROUP BY g ORDER BY g DESC;", new[] { 3, 2, 1 })]
    [InlineData("SELECT COUNT(*) AS total FROM #t t GROUP BY t.g ORDER BY t.g;", new[] { 1, 2, 3 })]
    [InlineData("SELECT g AS k, COUNT(*) AS total FROM #t GROUP BY g ORDER BY g;", new[] { 1, 2, 3 })]
    [InlineData("SELECT COUNT(*) AS total FROM #t GROUP BY g ORDER BY total;", new[] { 1, 2, 3 })]
    public async Task SortsByAGroupKeyThatIsNotSelected(string query, int[] expectedTotals)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(Setup + query).TokenizeToScript());

        var result = evaluator.LastResult;
        Assert.NotNull(result);
        var totals = result!.Rows.Select(r => Convert.ToInt32(r["total"])).ToArray();
        Assert.Equal(expectedTotals, totals);
        Assert.DoesNotContain(result.ColumnNames, c => c.StartsWith("__", StringComparison.Ordinal));
    }
}
