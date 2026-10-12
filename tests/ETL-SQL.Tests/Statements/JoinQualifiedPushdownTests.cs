using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// A WHERE predicate that names a table applies to that table. Pushdown filters each side of a join
/// early, and it must not apply <c>c.status</c> to another table that also has a <c>status</c>.
/// </summary>
public sealed class JoinQualifiedPushdownTests
{
    [Fact]
    public async Task AQualifiedPredicateIsNotAppliedToTheFromTable()
    {
        var ids = await QueryAsync("""
            CREATE TABLE #orders (id INT, customer_id INT, status VARCHAR(20));
            CREATE TABLE #customers (id INT, status VARCHAR(20));
            INSERT INTO #orders VALUES (1, 10, 'SHIPPED'), (2, 10, 'ACTIVE'), (3, 20, 'SHIPPED');
            INSERT INTO #customers VALUES (10, 'ACTIVE'), (20, 'CLOSED');
            """,
            "SELECT o.id FROM #orders o JOIN #customers c ON o.customer_id = c.id WHERE c.status = 'ACTIVE';",
            "id");

        // Both orders of the active customer, whatever the orders' own status.
        Assert.Equal(["1", "2"], ids);
    }

    [Fact]
    public async Task AQualifiedPredicateIsNotAppliedToAnEarlierJoinedTable()
    {
        var ids = await QueryAsync("""
            CREATE TABLE #a (id INT);
            CREATE TABLE #b (id INT, a_id INT, y INT);
            CREATE TABLE #c (id INT, b_id INT, y INT);
            INSERT INTO #a VALUES (1);
            INSERT INTO #b VALUES (100, 1, 5), (101, 1, 7);
            INSERT INTO #c VALUES (1000, 100, 7), (1001, 101, 9);
            """,
            "SELECT c.id FROM #a a JOIN #b b ON b.a_id = a.id JOIN #c c ON c.b_id = b.id WHERE c.y = 7;",
            "id");

        // c.y = 7 is row 1000, reached through b 100 whose own y is 5.
        Assert.Equal(["1000"], ids);
    }

    [Fact]
    public async Task APredicateOnTheFromTableStillFiltersIt()
    {
        var ids = await QueryAsync("""
            CREATE TABLE #orders (id INT, customer_id INT, status VARCHAR(20));
            CREATE TABLE #customers (id INT, status VARCHAR(20));
            INSERT INTO #orders VALUES (1, 10, 'SHIPPED'), (2, 10, 'ACTIVE'), (3, 20, 'SHIPPED');
            INSERT INTO #customers VALUES (10, 'ACTIVE'), (20, 'CLOSED');
            """,
            "SELECT o.id FROM #orders o JOIN #customers c ON o.customer_id = c.id WHERE o.status = 'SHIPPED' AND c.status = 'CLOSED';",
            "id");

        Assert.Equal(["3"], ids);
    }

    private static async Task<List<string>> QueryAsync(string setup, string query, string column)
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var providerLifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        await evaluator.Evaluate(Parse(setup));
        var values = new List<string>();
        await foreach (var batch in evaluator.ExecuteQuery(Parse(query).Statements[0]))
            values.AddRange(batch.Rows.Select(row => Convert.ToString(row[column], System.Globalization.CultureInfo.InvariantCulture)!));
        values.Sort(StringComparer.Ordinal);
        return values;
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();
}
