using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;

namespace ETL_SQL.Tests.Reporting.CascadingSlicers;

public sealed class ProductionCascadingFilterTests
{
    [Theory]
    [InlineData("LOCAL")]
    [InlineData("LIVE")]
    public async Task DashboardTransition_LocalAndLiveCommitEquivalentAtomicState(string mode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cascade-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, BuildDashboardScript(mode, "CLEAR"));
        try
        {
            await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            var before = await service.GetManifestAsync();
            Assert.Equal("Boston", before.Parameters["@City"]);

            var after = await service.SetParameterAsync("Region", "south");
            Assert.Equal("south", after.Parameters["@Region"]);
            Assert.Equal(string.Empty, after.Parameters["@City"]);
            Assert.Equal(["Austin", "south"], Assert.Single(after.Visuals.Single(v => v.Name == "CityFilter").Rows));
            Assert.NotNull(after.CascadeTransaction);
            Assert.Contains("@City", after.CascadeTransaction!.ChangedParameters);
            Assert.Equal(new[] { "CityFilter", "Detail" }, after.CascadeTransaction.RefreshedVisuals);
            Assert.Equal(new[] { "@City" }, after.CascadeGraph!.Order);
            Assert.Equal("north", before.Parameters["@Region"]);
            Assert.Equal("Boston", before.Parameters["@City"]);
            Assert.Equal(2, before.Visuals.Single(v => v.Name == "CityFilter").Rows.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LocalManifestSnapshot_RetainsEnoughDataForOfflineCascade()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cascade-snapshot-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, BuildDashboardScript("LOCAL", "CLEAR"));
        try
        {
            await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<ReportManifest>(
                System.Text.Json.JsonSerializer.Serialize(manifest))!;
            var city = snapshot.Visuals.Single(v => v.Name == "CityFilter");

            var rows = CascadingFilterState.FilterRows(city.Cascade!,
                new Dictionary<string, string> { ["@Region"] = "south" });
            Assert.Equal(["Austin", "south"], Assert.Single(rows));
            Assert.Equal(3, city.Cascade!.SourceRows!.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConcurrentParentChanges_EachPublishInternallyConsistentState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cascade-concurrent-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, BuildDashboardScript("LOCAL", "FIRST"));
        try
        {
            await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            await service.GetManifestAsync();
            var results = await Task.WhenAll(
                service.SetParameterAsync("Region", "south"),
                service.SetParameterAsync("Region", "north"));

            foreach (var result in results)
            {
                var resultRegion = result.Parameters["@Region"];
                var resultCity = resultRegion == "south" ? "Austin" : "Boston";
                Assert.Equal(resultCity, result.Parameters["@City"]);
                Assert.All(result.Visuals.Single(v => v.Name == "CityFilter").Rows,
                    row => Assert.Equal(resultRegion, row[1]));
            }

            var final = await service.GetManifestAsync();
            var region = final.Parameters["@Region"];
            var expectedCity = region == "south" ? "Austin" : "Boston";
            Assert.Equal(expectedCity, final.Parameters["@City"]);
            Assert.All(final.Visuals.Single(v => v.Name == "CityFilter").Rows,
                row => Assert.Equal(region, row[1]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InvalidErrorPolicy_RollsBackParametersAndVisuals()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cascade-rollback-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, BuildDashboardScript("LOCAL", "ERROR"));
        try
        {
            await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            var before = await service.GetManifestAsync();
            var rows = before.Visuals.Single(v => v.Name == "CityFilter").Rows.Select(r => r.ToList()).ToList();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetParameterAsync("Region", "south"));

            var after = await service.GetManifestAsync();
            Assert.Equal("north", after.Parameters["@Region"]);
            Assert.Equal("Boston", after.Parameters["@City"]);
            Assert.Equal(rows, after.Visuals.Single(v => v.Name == "CityFilter").Rows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LiveCascade_RefreshesTopologicallyAndRepairsBeforeGrandchildQuery()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cascade-live-order-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, """
DECLARE @Region STRING INPUT = 'north';
DECLARE @City STRING INPUT = 'Boston';
DECLARE @Store STRING INPUT = 'Boston One';
SELECT 'north' AS RegionCode INTO #regions;
INSERT INTO #regions (RegionCode) VALUES ('south');
SELECT 'Boston' AS City, 'north' AS RegionCode INTO #cities;
INSERT INTO #cities (City, RegionCode) VALUES ('Austin', 'south');
SELECT 'Boston One' AS Store, 'Boston' AS City INTO #stores;
INSERT INTO #stores (Store, City) VALUES ('Austin One', 'Austin');
CREATE VISUAL RegionFilter AS SLICER (SOURCE = #regions, MAPPINGS (VALUE = RegionCode), ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, RegionCode)));
CREATE VISUAL CityFilter AS SLICER (
  SOURCE = (SELECT City FROM #cities WHERE RegionCode = @Region), MAPPINGS (VALUE = City),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@City, City)), CASCADE (MODE = LIVE, INVALID = FIRST));
CREATE VISUAL StoreFilter AS SLICER (
  SOURCE = (SELECT Store FROM #stores WHERE City = @City), MAPPINGS (VALUE = Store),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@Store, Store)), CASCADE (MODE = LIVE, INVALID = FIRST));
CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A B C', MAP ('A' = RegionFilter, 'B' = CityFilter, 'C' = StoreFilter)));
""");
        try
        {
            await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            await service.GetManifestAsync();
            var manifest = await service.SetParameterAsync("Region", "south");

            Assert.Equal("Austin", manifest.Parameters["@City"]);
            Assert.Equal("Austin One", manifest.Parameters["@Store"]);
            Assert.Equal(new[] { "CityFilter", "StoreFilter" }, manifest.CascadeTransaction!.RefreshedVisuals);
            Assert.Equal(new[] { "@City", "@Store" }, manifest.CascadeGraph!.Order);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GraphCompiler_OrdersMultipleParentsBeforeDescendant()
    {
        var graph = CascadingFilterGraphCompiler.Compile(ParseVisuals("""
CREATE VISUAL Region AS SLICER (SOURCE = #r, ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, value)));
CREATE VISUAL Segment AS SLICER (SOURCE = #s, ACTIONS (ON_CHANGE = SET_PARAMETER(@Segment, value)));
CREATE VISUAL City AS SLICER (
  SOURCE = #c,
  ACTIONS (ON_CHANGE = SET_PARAMETER(@City, value)),
  CASCADE (MODE = LOCAL, PARENTS (@Region = RegionCode, @Segment = SegmentCode))
);
"""));

        var node = Assert.Single(graph.OrderedNodes);
        Assert.Equal("@City", node.ProducedParameter);
        Assert.Equal(new[] { "@Region", "@Segment" }, node.ParentParameters);
    }

    [Fact]
    public void GraphCompiler_RejectsCycleWithFullPath()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CascadingFilterGraphCompiler.Compile(ParseVisuals("""
CREATE VISUAL A AS SLICER (SOURCE = #a, ACTIONS (ON_CHANGE = SET_PARAMETER(@A, value)), CASCADE (MODE = LOCAL, PARENTS (@B = B)));
CREATE VISUAL B AS SLICER (SOURCE = #b, ACTIONS (ON_CHANGE = SET_PARAMETER(@B, value)), CASCADE (MODE = LOCAL, PARENTS (@A = A)));
""")));

        Assert.Contains("@A -> @B -> @A", exception.Message);
    }

    [Fact]
    public async Task AnalysisRule_ReportsCycleAtAuthorTime()
    {
        const string sql = """
CREATE VISUAL A AS SLICER (SOURCE = #a, ACTIONS (ON_CHANGE = SET_PARAMETER(@A, value)), CASCADE (MODE = LOCAL, PARENTS (@B = B)));
CREATE VISUAL B AS SLICER (SOURCE = #b, ACTIONS (ON_CHANGE = SET_PARAMETER(@B, value)), CASCADE (MODE = LOCAL, PARENTS (@A = A)));
""";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var diagnostics = await new CascadingFilterDependencyRule().AnalyzeAsync(script, new DefaultLintContext());
        var diagnostic = Assert.Single(diagnostics, d => d.Code == "RPT-CASCADE");
        Assert.Contains("@A -> @B -> @A", diagnostic.Message);
        Assert.Equal(LintSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("*", 3)]
    [InlineData("north", 2)]
    [InlineData("[\"north\",\"south\"]", 3)]
    [InlineData("north,south", 3)]
    public void LocalVector_FiltersNullAllAndMultiSelect(string selected, int expected)
    {
        var cascade = Cascade("ANY");
        var rows = CascadingFilterState.FilterRows(cascade,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["@Region"] = selected });
        Assert.Equal(expected, rows.Count);
    }

    [Fact]
    public void LocalVector_AllPolicy_KeepsOptionsAssociatedWithEverySelectedParent()
    {
        var cascade = WithRows(Cascade("ALL"),
            [
                ["Shared", "north"], ["Shared", "south"],
                ["NorthOnly", "north"]
            ]);
        var rows = CascadingFilterState.FilterRows(cascade,
            new Dictionary<string, string> { ["@Region"] = "[\"north\",\"south\"]" });
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Shared", row[0]));
    }

    [Fact]
    public void LocalVector_AllSentinel_RemainsNonConstrainingUnderNullMatch()
    {
        var cascade = Cascade("ANY");
        cascade.Null = "MATCH";
        var rows = CascadingFilterState.FilterRows(cascade,
            new Dictionary<string, string> { ["@Region"] = "*" });
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Reconcile_InvalidSelectionPolicies_AreDeterministic()
    {
        var visual = new VisualManifest
        {
            VisualType = "SLICER",
            Columns = ["value", "RegionCode"],
            Rows = [["Austin", "south"]]
        };

        Assert.Equal(string.Empty, CascadingFilterState.Reconcile(Cascade("ANY", "CLEAR"), visual, "Chicago"));
        Assert.Equal("Austin", CascadingFilterState.Reconcile(Cascade("ANY", "FIRST"), visual, "Chicago"));
        Assert.Throws<InvalidOperationException>(() =>
            CascadingFilterState.Reconcile(Cascade("ANY", "ERROR"), visual, "Chicago"));
    }

    [Fact]
    public void Reconcile_MultiSelectCanonicalizesLegacyAndDuplicateValues()
    {
        var visual = new VisualManifest
        {
            VisualType = "MULTISELECT",
            Columns = ["value"],
            Rows = [["Austin"], ["Boston"]]
        };
        Assert.Equal("[\"Austin\",\"Boston\"]",
            CascadingFilterState.Reconcile(Cascade("ANY"), visual, "Austin,Austin,Boston"));
    }

    private static CascadeVisualManifest Cascade(string multi, string invalid = "CLEAR") => new()
    {
        ProducedParameter = "@City",
        Parents = [new CascadeParentManifest("@Region", "RegionCode")],
        Invalid = invalid,
        Null = "ALL",
        AllValue = "*",
        MultiSelect = multi,
        ValueColumn = "value",
        SourceColumns = ["value", "RegionCode"],
        SourceRows = [["New York", "north"], ["Boston", "north"], ["Austin", "south"]]
    };

    private static CascadeVisualManifest WithRows(CascadeVisualManifest cascade, List<List<string?>> rows)
    {
        cascade.SourceRows = rows;
        return cascade;
    }

    private static List<CreateVisualStatement> ParseVisuals(string sql) =>
        new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements.OfType<CreateVisualStatement>().ToList();

    private static string BuildDashboardScript(string mode, string invalid)
    {
        var source = mode == "LOCAL"
            ? "#options"
            : "(SELECT City, RegionCode FROM #options WHERE RegionCode = @Region)";
        var parents = mode == "LOCAL" ? ", PARENTS (@Region = RegionCode)" : string.Empty;
        return $$"""
DECLARE @Region STRING INPUT = 'north';
DECLARE @City STRING INPUT = 'Boston';
SELECT 'north' AS RegionCode INTO #regions;
INSERT INTO #regions (RegionCode) VALUES ('south');
SELECT 'Boston' AS City, 'north' AS RegionCode INTO #options;
INSERT INTO #options (City, RegionCode) VALUES ('New York', 'north');
INSERT INTO #options (City, RegionCode) VALUES ('Austin', 'south');
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE = #regions,
  MAPPINGS (VALUE = RegionCode),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, RegionCode))
);
CREATE VISUAL CityFilter AS SLICER (
  SOURCE = {{source}},
  MAPPINGS (VALUE = City),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@City, City)),
  CASCADE (MODE = {{mode}}{{parents}}, INVALID = {{invalid}}, NULL = ALL)
);
CREATE VISUAL Detail AS TABLE (
  SOURCE = (SELECT City, RegionCode FROM #options WHERE City = @City)
);
CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A B / C C', MAP ('A' = RegionFilter, 'B' = CityFilter, 'C' = Detail)));
""";
    }
}
