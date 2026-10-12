using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// An ORDER BY over more rows than the spill threshold sorts on disk and stays a stream through
/// OFFSET / LIMIT and projection. The answer must be the one the in-memory sort gives.
/// </summary>
public sealed class SpilledOrderByTests
{
    private const int Rows = 600;

    [Theory]
    [InlineData("SELECT n FROM #t ORDER BY n DESC;")]
    [InlineData("SELECT n FROM #t ORDER BY n DESC LIMIT 25 OFFSET 40;")]
    [InlineData("SELECT TOP 10 PERCENT n FROM #t ORDER BY n;")]
    [InlineData("SELECT TOP 5 WITH TIES g, n FROM #t ORDER BY g;")]
    [InlineData("SELECT n * 2 AS doubled FROM #t WHERE n % 3 = 0 ORDER BY n DESC LIMIT 7;")]
    public async Task ASpilledSortReturnsWhatTheInMemorySortReturns(string query)
    {
        var inMemory = await RunAsync(query, spillThreshold: 1_000_000);
        var spilled = await RunAsync(query, spillThreshold: 50);

        Assert.NotEmpty(inMemory);
        Assert.Equal(inMemory, spilled);
    }

    private static async Task<List<string>> RunAsync(string query, int spillThreshold)
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var providerLifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        evaluator.JoinSpillThreshold = spillThreshold;

        var values = string.Join(", ", Enumerable.Range(1, Rows).Select(n => $"({n}, {n % 4})"));
        await evaluator.Evaluate(Parse($"CREATE TABLE #t (n INT, g INT); INSERT INTO #t VALUES {values};"));

        var lines = new List<string>();
        await foreach (var batch in evaluator.ExecuteQuery(Parse(query).Statements[0]))
            lines.AddRange(batch.Rows.Select(row => string.Join("|", batch.ColumnNames.Select(
                column => Convert.ToString(row[column], System.Globalization.CultureInfo.InvariantCulture)))));

        // WITH TIES on g leaves the order inside a tie open, so compare that one as a set.
        if (query.Contains("WITH TIES", StringComparison.Ordinal)) lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();
}
