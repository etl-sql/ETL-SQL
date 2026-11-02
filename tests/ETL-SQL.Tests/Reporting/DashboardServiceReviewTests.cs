using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.ReportHosting;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>DashboardService defects from the 2026-11-01 review.</summary>
public sealed class DashboardServiceReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dash-review-" + Guid.NewGuid().ToString("N"));

    public DashboardServiceReviewTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string sql)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sql);
        return path;
    }

    private const string RegionReport = """
        DECLARE @Region VARCHAR INPUT = 'All';
        SELECT 'West' AS region, 100 AS revenue INTO #sales;
        INSERT INTO #sales (region, revenue) VALUES ('East', 200);
        CREATE VISUAL SalesTable AS TABLE (
            SOURCE = (SELECT region, revenue FROM #sales WHERE region = @Region OR @Region = 'All')
        );
        CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesTable)));
        CREATE BOOKMARK EastOnly AS (PARAMETERS (@Region = 'East'));
        """;

    [Fact]
    public async Task AtPrefixedAndBareParameterNamesAreOneParameter()
    {
        var path = Write("region.rptsql", RegionReport);
        await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
        await svc.GetManifestAsync();

        await svc.SetParameterAsync("@Region", "West");
        await svc.ApplyBookmarkAsync("EastOnly");

        Assert.Single(svc.Parameters);
        Assert.Equal("East", svc.Parameters.Single().Value);

        // A full rebuild re-declares parameters from the cache: it must carry the latest value.
        var rebuilt = await svc.RebuildAsync();
        Assert.Equal("East", rebuilt.Parameters["@Region"]);
        Assert.Single(rebuilt.Visuals.Single(v => v.Name == "SalesTable").Rows);
    }

    [Fact]
    public async Task RunScriptBeforeTheReportIsViewedUsesTheReportSession()
    {
        var path = Write("report.rptsql", """
            SELECT 1 AS id INTO #items;
            CREATE VISUAL Items AS TABLE (SOURCE = (SELECT * FROM #items));
            """);
        Write("add.etlsql", "INSERT INTO #items (id) VALUES (2);");
        await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());

        // No GetManifestAsync first: the old fallback ran this on a bare evaluator that had neither
        // the report's temp tables nor the session's storage and path policy.
        var (message, _) = await svc.RunScriptAsync("add.etlsql", new Dictionary<string, string>());

        Assert.Equal("Script executed successfully.", message);
    }

    [Fact]
    public async Task RunScriptResolvesRelativePathsFromTheActionScript()
    {
        var path = Write("report.rptsql", """
            SELECT 1 AS id INTO #items;
            CREATE VISUAL Items AS TABLE (SOURCE = (SELECT * FROM #items));
            """);
        Write(Path.Combine("actions", "export.etlsql"), """
            CREATE CONNECTION outFile AS FLATFILE(PATH = 'exported-review.csv', HEADER = ON);
            INSERT INTO outFile SELECT id FROM #items;
            """);
        var cwdCopy = Path.Combine(Directory.GetCurrentDirectory(), "exported-review.csv");
        try
        {
            await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            await svc.GetManifestAsync();

            var (message, _) = await svc.RunScriptAsync(Path.Combine("actions", "export.etlsql"), new Dictionary<string, string>());

            Assert.Equal("Script executed successfully.", message);
            Assert.True(File.Exists(Path.Combine(_dir, "actions", "exported-review.csv")));
            Assert.False(File.Exists(cwdCopy));
        }
        finally
        {
            if (File.Exists(cwdCopy)) File.Delete(cwdCopy);
        }
    }

    [Fact]
    public async Task ConcurrentDrillCallsDoNotCorruptState()
    {
        var path = Write("drill.rptsql", """
            SELECT 'West' AS region, 'CA' AS state, 'LA' AS city, 10 AS amount INTO #geo;
            INSERT INTO #geo VALUES ('West', 'WA', 'Seattle', 20);
            INSERT INTO #geo VALUES ('East', 'NY', 'NYC', 30);
            CREATE VISUAL Geo AS BAR (
                SOURCE = (SELECT * FROM #geo),
                MAPPINGS (X = region, Y = amount),
                ACTIONS (ON_CLICK = DRILL_IN(HIERARCHY = (region, state, city)))
            );
            """);
        await using var svc = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await svc.GetManifestAsync();
        Assert.Contains(manifest.Visuals.Single(v => v.Name == "Geo").Actions, a => a.Type == "DRILL_IN");

        var calls = Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            if (i % 3 == 0) await svc.DrillUpAsync("Geo", 0);
            else await svc.DrillInAsync("Geo", i % 2 == 0 ? "West" : "CA");
        }));
        await Task.WhenAll(calls);

        // Drilling from any reachable state never indexes past the hierarchy.
        for (var i = 0; i < 5; i++)
            Assert.NotNull(await svc.DrillInAsync("Geo", "West"));
    }
}
