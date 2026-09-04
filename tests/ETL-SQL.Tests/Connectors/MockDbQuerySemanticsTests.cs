using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// MOCKDB is the built-in demo connector: it backs Studio's "Start with sample data", the samples,
/// and a large share of the test suite. Those surfaces read it as if it were a database, so the SQL
/// they write has to mean what it says.
///
/// <para>It did not. <c>MockSqlDataSource</c> declared <c>SupportsSqlPushdown = true</c>, which hands
/// it whole statements to execute, while its <c>ExecuteRawSql</c> is a string matcher that
/// understands a projection and one <c>WHERE col = val</c> and silently ignores everything else. A
/// <c>GROUP BY</c> came back ungrouped; an aggregate came back as a column of nulls; and because
/// <c>COUNT(*)</c> contains a <c>*</c>, a query using it fell through to "return the whole table".
/// No error was raised in any of those cases, so the wrong answer looked like a real one.</para>
///
/// <para>The connector now reports that it cannot execute SQL, which routes these statements through
/// the engine's own execution path. These tests pin the behaviour rather than the flag: each one
/// states a result the language guarantees, so they stay meaningful if the mock ever grows a real
/// query engine and turns pushdown back on.</para>
/// </summary>
[Trait("Category", "Connectors")]
public sealed class MockDbQuerySemanticsTests
{
    private static async Task<(System.Collections.Generic.IReadOnlyList<string> Columns, System.Collections.Generic.List<Row> Rows)> QueryAsync(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var script = "CREATE CONNECTION demo AS MOCKDB();\n" + sql;
        await evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

        var result = evaluator.LastResult;
        Assert.NotNull(result);
        return (result!.Schema?.ColumnNames ?? Array.Empty<string>(), result.Rows.ToList());
    }

    [Fact]
    public async Task GroupBy_CollapsesRowsToOnePerKey()
    {
        var (columns, rows) = await QueryAsync(
            "SELECT Region, SUM(Total) AS Revenue FROM demo.Orders GROUP BY Region;");

        Assert.Equal(new[] { "Region", "Revenue" }, columns);

        // The seeded Orders table has 250 rows across a handful of regions. Ungrouped, this returned
        // all 250 — the defect this test exists for.
        Assert.InRange(rows.Count, 2, 12);
        Assert.Equal(rows.Count, rows.Select(row => row["Region"]?.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(rows, row => Assert.True(Convert.ToDecimal(row["Revenue"]) > 0m,
            "A summed measure came back as zero or null, so the aggregate was never computed."));
    }

    [Fact]
    public async Task CountStar_IsAggregatedRatherThanReadAsSelectStar()
    {
        // COUNT(*) contains a '*', which the old string matcher read as "SELECT *" and answered with
        // the entire unprojected table.
        var (columns, rows) = await QueryAsync(
            "SELECT Region, COUNT(*) AS Orders FROM demo.Orders GROUP BY Region;");

        Assert.Equal(new[] { "Region", "Orders" }, columns);
        Assert.InRange(rows.Count, 2, 12);
        Assert.All(rows, row => Assert.True(Convert.ToInt64(row["Orders"]) > 0L));
    }

    [Fact]
    public async Task AggregateWithoutGroupBy_ReturnsASingleTotalRow()
    {
        // Previously this returned one row per source row, every one of them null, because the mock
        // projected a column named "Revenue" that does not exist on Orders.
        var (columns, rows) = await QueryAsync(
            "SELECT SUM(Total) AS Revenue FROM demo.Orders;");

        Assert.Equal("Revenue", Assert.Single(columns));
        var only = Assert.Single(rows);
        Assert.True(Convert.ToDecimal(only["Revenue"]) > 0m);
    }

    [Fact]
    public async Task GroupedSelectInto_MaterializesTheGroupedRows()
    {
        // How the sample dashboard stages its data: the #temp table must hold the aggregate, not the
        // rows it was aggregated from.
        var (columns, rows) = await QueryAsync(
            "SELECT Region, SUM(Total) AS Revenue INTO #by_region FROM demo.Orders GROUP BY Region;\n"
            + "SELECT * FROM #by_region;");

        Assert.Equal(new[] { "Region", "Revenue" }, columns);
        Assert.InRange(rows.Count, 2, 12);
    }

    [Fact]
    public async Task WhereAndOrderBy_StillNarrowAndOrderTheResult()
    {
        // The mock's one real capability was `WHERE col = val`; ORDER BY it ignored outright. Both
        // have to hold now that the engine runs the statement.
        var (_, rows) = await QueryAsync(
            "SELECT Region, SUM(Total) AS Revenue FROM demo.Orders GROUP BY Region ORDER BY Revenue DESC;");

        var revenues = rows.Select(row => Convert.ToDecimal(row["Revenue"])).ToList();
        Assert.Equal(revenues.OrderByDescending(value => value).ToList(), revenues);

        var region = rows[0]["Region"]?.ToString();
        var (_, filtered) = await QueryAsync(
            $"SELECT SaleID FROM demo.Orders WHERE Region = '{region}';");
        Assert.NotEmpty(filtered);
        Assert.True(filtered.Count < 250, "WHERE did not narrow the result at all.");
    }
}
