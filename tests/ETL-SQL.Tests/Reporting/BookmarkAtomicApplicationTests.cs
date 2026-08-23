using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.ReportHosting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// End-to-end coverage of the server-side atomic bookmark application through the real DashboardService,
/// evaluator, ManifestBuilder, and cascading-parameter engine.
/// </summary>
public sealed class BookmarkAtomicApplicationTests
{
    private static async Task<string> WriteScriptAsync(string sql)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bm-atomic-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, sql);
        return path;
    }

    private const string BasicReport = """
        DECLARE @region VARCHAR INPUT = 'All';
        DECLARE @year INT INPUT = 2026;
        SELECT 'West' AS region, 2026 AS year, 100 AS revenue INTO #sales;
        INSERT INTO #sales (region, year, revenue) VALUES ('East', 2026, 200);
        CREATE VISUAL SalesChart AS BAR (
            SOURCE = (SELECT region, revenue FROM #sales WHERE (region = @region OR @region = 'All') AND year = @year),
            MAPPINGS (X = region, Y = revenue)
        );
        CREATE CONTAINER FilterPanel AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesChart)));
        CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesChart)));
        CREATE PAGE Detail AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesChart)));
        CREATE BOOKMARK WestCoast AS (
            PARAMETERS (@region = 'West', @year = 2026),
            PAGE = Detail,
            STATE (FilterPanel.COLLAPSED = ON, SalesChart.VISIBLE = ON)
        );
        """;

    [Fact]
    public async Task ApplyBookmark_PublishesOneManifestWithResolvedState()
    {
        var path = await WriteScriptAsync(BasicReport);
        try
        {
            await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            var before = await svc.GetManifestAsync();
            Assert.Equal("All", before.Parameters["@region"]);
            Assert.Equal(2, before.Visuals.Single(v => v.Name == "SalesChart").Rows.Count); // West + East

            var after = await svc.ApplyBookmarkAsync("WestCoast");

            // Parameters committed atomically through the cascade engine.
            Assert.Null(after.Error);
            Assert.Equal("West", after.Parameters["@region"]);
            Assert.Single(after.Visuals.Single(v => v.Name == "SalesChart").Rows); // filtered to West only

            // One published manifest carries the resolved presentation state for a single client swap.
            Assert.NotNull(after.AppliedState);
            Assert.Equal("Detail", after.AppliedState!.ActivePage);
            Assert.True(after.AppliedState.Collapsed["FilterPanel"]);
            Assert.True(after.AppliedState.Visible["SalesChart"]);
            Assert.Equal(2026m, after.AppliedState.Parameters["@year"].NumberValue);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ApplyBookmark_UnknownName_DoesNotApplyAndReportsError()
    {
        var path = await WriteScriptAsync(BasicReport);
        try
        {
            await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            await svc.GetManifestAsync();

            var result = await svc.ApplyBookmarkAsync("DoesNotExist");
            Assert.NotNull(result.Error);
            Assert.Null(result.AppliedState);

            // Live manifest is untouched — nothing was partially applied.
            var live = await svc.GetManifestAsync();
            Assert.Equal("All", live.Parameters["@region"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ApplyBookmark_WhenCascadeReconciliationFails_RollsBackEverything()
    {
        // CityFilter is a LOCAL cascade with INVALID = ERROR. A bookmark that changes @Region to 'south'
        // invalidates the current @City = 'Boston' selection, so reconciliation throws and the whole
        // application must roll back — no parameter, visual, page, or presentation change is published.
        var script = """
            DECLARE @Region STRING INPUT = 'north';
            DECLARE @City STRING INPUT = 'Boston';
            SELECT 'Boston' AS City, 'north' AS RegionCode INTO #cities;
            INSERT INTO #cities (City, RegionCode) VALUES ('Austin', 'south');
            CREATE VISUAL RegionFilter AS SLICER (
                SOURCE = (SELECT DISTINCT RegionCode FROM #cities), MAPPINGS (VALUE = RegionCode),
                ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, RegionCode)));
            CREATE VISUAL CityFilter AS SLICER (
                SOURCE = (SELECT City FROM #cities WHERE RegionCode = @Region), MAPPINGS (VALUE = City),
                ACTIONS (ON_CHANGE = SET_PARAMETER(@City, City)),
                CASCADE (MODE = LOCAL, PARENTS (@Region = RegionCode), INVALID = ERROR));
            CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A B', MAP ('A' = RegionFilter, 'B' = CityFilter)));
            CREATE BOOKMARK SouthView AS (PARAMETERS (@Region = 'south'), PAGE = Main);
            """;
        var path = await WriteScriptAsync(script);
        try
        {
            await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            var before = await svc.GetManifestAsync();
            var cityRowsBefore = before.Visuals.Single(v => v.Name == "CityFilter").Rows
                .Select(r => r.ToList()).ToList();

            var result = await svc.ApplyBookmarkAsync("SouthView");

            // The application failed and rolled back: error reported, nothing applied.
            Assert.NotNull(result.Error);
            Assert.Null(result.AppliedState);

            // Live manifest is unchanged: parameters and the cascade visual are exactly as before.
            var live = await svc.GetManifestAsync();
            Assert.Equal("north", live.Parameters["@Region"]);
            Assert.Equal("Boston", live.Parameters["@City"]);
            Assert.Equal(cityRowsBefore, live.Visuals.Single(v => v.Name == "CityFilter").Rows);
        }
        finally { File.Delete(path); }
    }
}
