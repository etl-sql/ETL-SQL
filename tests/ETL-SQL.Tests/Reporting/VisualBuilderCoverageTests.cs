using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportHosting;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Exercises VisualBuilder paths not covered by other reporting tests:
    /// summaries/aggregates, actions, DATA_LABELS, control visuals, overlays,
    /// axis options, and mapping resolution.
    /// </summary>
    public class VisualBuilderCoverageTests : IDisposable
    {
        private readonly string _scriptDir;

        public VisualBuilderCoverageTests()
        {
            _scriptDir = Path.Combine(Path.GetTempPath(), "VB-Coverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scriptDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_scriptDir)) Directory.Delete(_scriptDir, recursive: true);
        }

        private string Write(string name, string content)
        {
            var path = Path.Combine(_scriptDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static DashboardService Svc(string path) =>
            new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());

        // ── CalculateSummaries ────────────────────────────────────────────────────

        [Fact]
        public async Task Summaries_CountAggregate_ReturnsCorrectCount()
        {
            var path = Write("sum_count.rptsql", @"
SELECT 'A' AS Cat, 10 AS Val INTO #D;
INSERT INTO #D VALUES ('B', 20);
INSERT INTO #D VALUES ('C', 30);
CREATE VISUAL T AS TABLE (
    SOURCE = #D,
    SUMMARY (COUNT(Val) AS TotalRows)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = T));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "T");

            Assert.NotNull(v.SummaryData);
            Assert.Single(v.SummaryData.Aggregates);
            Assert.Equal("COUNT", v.SummaryData.Aggregates[0].Aggregate);
            Assert.Equal("3", v.SummaryData.Aggregates[0].Value);
        }

        [Fact]
        public async Task Summaries_SumAggregate_ReturnsCorrectSum()
        {
            var path = Write("sum_sum.rptsql", @"
SELECT 'A' AS Cat, 10 AS Val INTO #D;
INSERT INTO #D VALUES ('B', 20);
CREATE VISUAL T AS TABLE (
    SOURCE = #D,
    SUMMARY (SUM(Val) AS Total)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = T));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "T");

            Assert.NotNull(v.SummaryData);
            Assert.Equal("30", v.SummaryData.Aggregates[0].Value);
        }

        [Fact]
        public async Task Summaries_AvgAggregate_ReturnsCorrectAvg()
        {
            var path = Write("sum_avg.rptsql", @"
SELECT 'A' AS Cat, 10 AS Val INTO #D;
INSERT INTO #D VALUES ('B', 20);
CREATE VISUAL T AS TABLE (
    SOURCE = #D,
    SUMMARY (AVG(Val) AS Average)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = T));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "T");

            Assert.NotNull(v.SummaryData);
            Assert.Equal("15", v.SummaryData.Aggregates[0].Value);
        }

        [Fact]
        public async Task Summaries_MinMaxAggregates_ReturnCorrectValues()
        {
            var path = Write("sum_minmax.rptsql", @"
SELECT 'A' AS Cat, 5 AS Val INTO #D;
INSERT INTO #D VALUES ('B', 15);
INSERT INTO #D VALUES ('C', 10);
CREATE VISUAL T AS TABLE (
    SOURCE = #D,
    SUMMARY (MIN(Val) AS MinVal, MAX(Val) AS MaxVal)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = T));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "T");

            Assert.NotNull(v.SummaryData);
            var min = v.SummaryData.Aggregates.First(a => a.Aggregate == "MIN");
            var max = v.SummaryData.Aggregates.First(a => a.Aggregate == "MAX");
            Assert.Equal("5", min.Value);
            Assert.Equal("15", max.Value);
        }

        [Fact]
        public async Task Summaries_WithAlias_SetsAliasOnManifest()
        {
            var path = Write("sum_alias.rptsql", @"
SELECT 'A' AS Cat, 10 AS Val INTO #D;
CREATE VISUAL T AS TABLE (
    SOURCE = #D,
    SUMMARY (COUNT(Val) AS 'Row Count')
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = T));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "T");

            Assert.NotNull(v.SummaryData);
            Assert.Equal("Row Count", v.SummaryData.Aggregates[0].Alias);
        }

        // ── DATA_LABELS options ───────────────────────────────────────────────────

        [Fact]
        public async Task DataLabels_OnWithSubOptions_SetsManifestProperties()
        {
            var path = Write("dl.rptsql", @"
SELECT 'A' AS Cat, 10 AS Val INTO #D;
CREATE VISUAL B AS BAR (
    SOURCE = #D,
    MAPPINGS (X = Cat, Y = Val),
    OPTIONS (
        DATA_LABELS = ON WITH (POSITION = 'top', COLOR = '#ff0000', FONT_SIZE = 12, FONT_WEIGHT = 'bold', FONT_FAMILY = 'Arial', FORMAT = '{value}')
    )
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = B));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "B");

            Assert.NotNull(v.DataLabels);
            Assert.True(v.DataLabels.Show);
            Assert.Equal("top", v.DataLabels.Position);
            Assert.Equal("#ff0000", v.DataLabels.Color);
            Assert.Equal(12, v.DataLabels.FontSize);
            Assert.Equal("bold", v.DataLabels.FontWeight);
            Assert.Equal("Arial", v.DataLabels.FontFamily);
            Assert.Equal("{value}", v.DataLabels.Format);
        }

        // ── Control visuals auto-set EXPORT=OFF ──────────────────────────────────

        [Fact]
        public async Task ControlVisual_Slicer_AutoSetsExportOff()
        {
            var path = Write("slicer.rptsql", @"
SELECT 'East' AS Region INTO #Regions;
INSERT INTO #Regions VALUES ('West');
CREATE VISUAL RegionFilter AS SLICER (
    SOURCE = #Regions,
    MAPPINGS (VALUE = Region)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = RegionFilter));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "RegionFilter");

            Assert.NotNull(v.Styles);
            Assert.True(v.Styles.TryGetValue("EXPORT", out var exportVal));
            Assert.Equal("OFF", exportVal);
        }

        // ── Actions ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Action_SetParameter_ValueSourceControl_IsControlValue()
        {
            var path = Write("action_sp.rptsql", @"
DECLARE @SelectedRegion STRING = 'All';
SELECT 'East' AS Region INTO #D;
INSERT INTO #D VALUES ('West');
CREATE VISUAL RegionPicker AS SLICER (
    SOURCE = #D,
    MAPPINGS (VALUE = Region),
    DEFAULT = 'All',
    ACTIONS (ON_CHANGE = SET_PARAMETER(@SelectedRegion, VALUE))
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = RegionPicker));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "RegionPicker");

            var action = v.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER");
            Assert.NotNull(action);
            Assert.Equal("CONTROL_VALUE", action.ValueSource);
        }

        [Fact]
        public async Task Action_SetParameter_ColumnExpression_IsColumnSource()
        {
            var path = Write("action_col.rptsql", @"
DECLARE @SelectedRegion STRING = '';
SELECT 'East' AS Region, 100 AS Total INTO #D;
CREATE VISUAL SalesChart AS BAR (
    SOURCE = #D,
    MAPPINGS (X = Region, Y = Total),
    ACTIONS (ON_CLICK = SET_PARAMETER(@SelectedRegion, Region))
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = SalesChart));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "SalesChart");

            var action = v.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER");
            Assert.NotNull(action);
            Assert.Equal("COLUMN", action.ValueSource);
            Assert.Equal("Region", action.ValueColumn, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Action_SetParameter_LiteralExpression_IsLiteralSource()
        {
            var path = Write("action_lit.rptsql", @"
DECLARE @Param STRING = '';
SELECT 'East' AS Region INTO #D;
CREATE VISUAL MyVis AS BAR (
    SOURCE = #D,
    MAPPINGS (X = Region, Y = Region),
    ACTIONS (ON_CLICK = SET_PARAMETER(@Param, 'fixed'))
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = MyVis));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "MyVis");

            var action = v.Actions.FirstOrDefault(a => a.Type == "SET_PARAMETER");
            Assert.NotNull(action);
            Assert.Equal("LITERAL", action.ValueSource);
            Assert.Equal("fixed", action.LiteralValue);
        }

        [Fact]
        public async Task Action_ClearFilters_RecordedOnManifest()
        {
            var path = Write("action_cf.rptsql", @"
SELECT 'East' AS Region INTO #D;
CREATE VISUAL ClearBtn AS BAR (
    SOURCE = #D,
    MAPPINGS (X = Region, Y = Region),
    ACTIONS (ON_CLICK = CLEAR_FILTERS)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = ClearBtn));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "ClearBtn");

            Assert.Contains(v.Actions, a => a.Type == "CLEAR_FILTERS");
        }

        [Fact]
        public async Task Action_DrillDown_RecordedOnManifest()
        {
            var path = Write("action_dd.rptsql", @"
SELECT 'East' AS Region, 100 AS Total INTO #D;
CREATE VISUAL Detail AS TABLE (SOURCE = #D);
CREATE VISUAL Summary AS BAR (
    SOURCE = #D,
    MAPPINGS (X = Region, Y = Total),
    ACTIONS (ON_CLICK = DRILL_DOWN(TARGET = Detail, KEY = Region))
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A B', MAP('A' = Summary, 'B' = Detail));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "Summary");

            var dd = v.Actions.FirstOrDefault(a => a.Type == "DRILL_DOWN");
            Assert.NotNull(dd);
            Assert.Equal("Detail", dd.TargetVisual, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Action_NavigatePage_RecordedOnManifest()
        {
            var path = Write("action_nav_page.rptsql", @"
CREATE BUTTON DetailsButton AS (
    TITLE = 'Details',
    ACTIONS (ON_CLICK = NAVIGATE_PAGE(Details))
);
CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP('A' = DetailsButton));
CREATE PAGE Details AS DASHBOARD (STRUCTURE = 'A', MAP('A' = DetailsButton), VISIBLE = OFF);
CREATE NAVIGATION MainNav AS TAB (
    DEFAULT = Overview,
    PAGES (Overview)
);
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var button = m.Buttons!.First(x => x.Name == "DetailsButton");

            var action = button.Actions.FirstOrDefault(a => a.Type == "NAVIGATE_PAGE");
            Assert.NotNull(action);
            Assert.Equal("Details", action!.TargetPage, StringComparer.OrdinalIgnoreCase);
        }

        // ── GRID style option ─────────────────────────────────────────────────────

        [Fact]
        public async Task GridOption_SetsGridStyleProperty()
        {
            var path = Write("grid.rptsql", @"
SELECT 'A' AS Cat, 10 AS Val INTO #D;
CREATE VISUAL G AS BAR (
    SOURCE = #D,
    MAPPINGS (X = Cat, Y = Val),
    OPTIONS (GRID = 'both')
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = G));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "G");

            Assert.Equal("BOTH", v.GridStyle);
        }

        // ── Typed series (COMBO) ─────────────────────────────────────────────────

        [Fact]
        public async Task TypedSeries_Combo_PopulatesSeriesDefs()
        {
            var path = Write("combo.rptsql", @"
SELECT 'Q1' AS Quarter, 100 AS Revenue, 20 AS Margin INTO #D;
INSERT INTO #D VALUES ('Q2', 120, 25);
CREATE VISUAL Mix AS COMBO (
    SOURCE = #D,
    MAPPINGS (X = Quarter),
    SERIES (BAR Revenue, LINE Margin)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = Mix));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "Mix");

            Assert.NotNull(v.SeriesDefs);
            Assert.Equal(2, v.SeriesDefs.Count);
            Assert.Contains(v.SeriesDefs, s => s.SeriesType.Equals("bar", StringComparison.OrdinalIgnoreCase) && s.Column == "Revenue");
            Assert.Contains(v.SeriesDefs, s => s.SeriesType.Equals("line", StringComparison.OrdinalIgnoreCase) && s.Column == "Margin");
        }

        // ── Markdown visual ───────────────────────────────────────────────────────

        [Fact]
        public async Task TextVisual_IsMarkdownTrue()
        {
            var path = Write("text.rptsql", @"
CREATE VISUAL Info AS TEXT (
    SOURCE = (SELECT '# Hello' AS Content),
    MAPPINGS (VALUE = Content)
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = Info));
");
            await using var svc = Svc(path);
            var m = await svc.GetManifestAsync();
            var v = m.Visuals.First(x => x.Name == "Info");

            Assert.True(v.IsMarkdown);
        }
    }
}
