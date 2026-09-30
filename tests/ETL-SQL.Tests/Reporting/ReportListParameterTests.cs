using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.ReportHosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// A reader's picks reach a LIST parameter as text: a MULTISELECT posts a JSON array, and a
/// Ctrl+click selection posts several values. <c>IN @list</c> must match each of them. Before
/// LIST had a converter both matched nothing, so the documented MULTISELECT pattern emptied
/// its visuals on the first pick.
/// </summary>
[Trait("CompatBreak", "0.20")]
public sealed class ReportListParameterTests
{
    /// <summary>
    /// Before 0.20 the text stayed one value: <c>IN</c> matched no row and <c>FOREACH</c> ran once
    /// with <c>'North,South'</c>.
    /// </summary>
    [Fact]
    public async Task ALISTDeclaredFromTextHoldsItsItems()
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Lexer("""
            DECLARE @L LIST = 'North, South';
            DECLARE @msg VARCHAR = '';
            FOREACH @n IN @L
            BEGIN
                SET @msg = @msg + @n + ';';
            END
            """).TokenizeToScript());

        Assert.Equal(new object?[] { "North", "South" }, Assert.IsType<List<object?>>(eval.Variables["@L"]));
        Assert.Equal("North;South;", eval.Variables["@msg"]?.ToString());
    }

    private const string Script = """
        DECLARE @selected_regions LIST = ('North', 'South', 'West', 'East, Coast');
        CREATE TABLE #sales (SaleID INT, Region VARCHAR(20));
        INSERT INTO #sales VALUES (1, 'North'), (2, 'South'), (3, 'West'), (4, 'East, Coast');
        SELECT DISTINCT Region INTO #region_opts FROM #sales;
        CREATE VISUAL RegionFilter AS MULTISELECT (
          SOURCE = #region_opts,
          MAPPINGS (VALUE = Region),
          ACTIONS (ON_CHANGE = SET_PARAMETER(@selected_regions, value))
        );
        CREATE VISUAL Orders AS TABLE (
          SOURCE = 'SELECT * FROM #sales WHERE Region IN @selected_regions',
          INTERACTIONS (ON_SELECT = FILTER)
        );
        CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A B', MAP ('A' = RegionFilter, 'B' = Orders));
        """;

    [Theory]
    [InlineData("[\"North\",\"West\"]", false, new[] { "North", "West" })]
    [InlineData("[\"East, Coast\"]", false, new[] { "East, Coast" })]
    [InlineData("North,West", true, new[] { "North", "West" })]
    [InlineData("South", true, new[] { "South" })]
    public async Task EachPickedValueIsMatched(string posted, bool isInteraction, string[] expected)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"list_param_{Guid.NewGuid():N}.rptsql");
        File.WriteAllText(scriptPath, Script);
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var initial = await service.GetManifestAsync();
            Assert.Null(initial.Error);
            Assert.Equal(4, initial.Visuals.Single(visual => visual.Name == "Orders").Rows.Count);

            var manifest = await service.SetParametersAsync([("@selected_regions", posted)], isInteraction);

            var orders = manifest.Visuals.Single(visual => visual.Name == "Orders");
            Assert.Null(orders.Error);
            var region = orders.Columns.FindIndex(column => column.Equals("Region", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expected, orders.Rows.Select(row => row[region]?.ToString()).OrderBy(value => value).ToArray());
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    /// <summary>
    /// The runtime posts <c>manifest.parameters</c> back (drill-back, bookmarks, the parameter panel),
    /// so a LIST must read as a value the binding accepts, not as a CLR type name.
    /// </summary>
    [Fact]
    public async Task AListParameterIsReportedAsAJsonArrayAndRoundTrips()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"list_param_{Guid.NewGuid():N}.rptsql");
        File.WriteAllText(scriptPath, Script);
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var initial = await service.GetManifestAsync();
            Assert.Equal("[\"North\",\"South\",\"West\",\"East, Coast\"]", initial.Parameters["@selected_regions"]);
            Assert.Contains("@selected_regions", initial.ListParameters, StringComparer.OrdinalIgnoreCase);

            var narrowed = await service.SetParametersAsync([("@selected_regions", "[\"East, Coast\"]")]);
            Assert.Equal("[\"East, Coast\"]", narrowed.Parameters["@selected_regions"]);

            var restored = await service.SetParametersAsync([("@selected_regions", initial.Parameters["@selected_regions"])]);
            Assert.Equal(4, restored.Visuals.Single(visual => visual.Name == "Orders").Rows.Count);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task ARebuildKeepsThePostedList()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"list_param_{Guid.NewGuid():N}.rptsql");
        File.WriteAllText(scriptPath, Script);
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            await service.GetManifestAsync();
            await service.SetParametersAsync([("@selected_regions", "[\"North\",\"West\"]")]);

            // A rebuild injects the value before the script's DECLARE, which casts it.
            var manifest = await service.RebuildAsync();

            var orders = manifest.Visuals.Single(visual => visual.Name == "Orders");
            Assert.Equal(2, orders.Rows.Count);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
