using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class AdvancedVisualGapsTests
{
    [Fact]
    public async Task Gauge_FormattingGoal2AndLabelPosition_RendersCorrectly()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"gauge_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 75.5 AS ActualValue, 0 AS MinVal, 100 AS MaxVal, 70 AS Target1, 90 AS Target2 INTO #gauge_data;

CREATE VISUAL PerformanceGauge AS GAUGE (
  SOURCE = #gauge_data,
  MAPPINGS (
    VALUE = ActualValue,
    MIN = MinVal,
    MAX = MaxVal,
    GOAL = Target1,
    GOAL2 = Target2
  ),
  OPTIONS (
    TITLE = 'Quarterly Performance',
    GOAL_LABEL = 'Tier 1 Goal',
    GOAL2_LABEL = 'Tier 2 Stretch',
    LABEL_POSITION = 'BOTTOM',
    FORMAT = '0.0%'
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = PerformanceGauge));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "PerformanceGauge");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual);

            Assert.Contains("data-label-position='BOTTOM'", svg);
            Assert.Contains("plot-gauge-goal", svg);
            Assert.Contains("plot-gauge-goal2", svg);
            Assert.Contains("Tier 1 Goal", svg);
            Assert.Contains("Tier 2 Stretch", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Map_ColorScaleAndNullColor_RendersCorrectly()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"map_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 'United States of America' AS Country, 500 AS Metric INTO #map_data;

CREATE VISUAL WorldChoropleth AS MAP (
  SOURCE = #map_data,
  MAPPINGS (
    REGION = Country,
    VALUE = Metric,
    TOOLTIP = Country
  ),
  OPTIONS (
    TITLE = 'Global Metrics',
    COLOR_SCALE = 'QUANTILE',
    NULL_COLOR = '#f3f4f6',
    COLOR_LOW = '#dbeafe',
    COLOR_HIGH = '#1e3a8a'
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = WorldChoropleth));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "WorldChoropleth");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual);

            Assert.Contains("plot-geographic-regions", svg);
            Assert.Contains("fill='#f3f4f6'", svg);
            Assert.Contains("<title>United States of America</title>", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(", PROJECTION = MERCATOR")]
    public async Task Map_UsStates_DrawsAlaskaAndHawaiiAsInsets(string projection)
    {
        // At their true positions Alaska took much of the card and the lower 48 about half its width.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"map_insets_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, $@"
SELECT 'California' AS State, 1250000 AS Revenue INTO #state_rev
UNION ALL SELECT 'Texas', 980000
UNION ALL SELECT 'Alaska', 120000
UNION ALL SELECT 'Hawaii', 150000;

CREATE VISUAL RevenueMap AS MAP (
  SOURCE = #state_rev,
  MAPPINGS (REGION = State, VALUE = Revenue),
  OPTIONS (MAP_NAME = US_STATES, TITLE = 'Revenue by State'{projection})
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = RevenueMap));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "RevenueMap");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual)!;

            static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            var box = System.Text.RegularExpressions.Regex.Match(svg, "viewBox='0 0 (?<w>[0-9.]+) (?<h>[0-9.]+)'");
            var width = Num(box.Groups["w"].Value);
            var height = Num(box.Groups["h"].Value);
            var regions = System.Text.RegularExpressions.Regex.Matches(svg, "class='plot-geographic-region'[^>]* d='(?<d>[^']+)'[^>]*><title>(?<t>[^<:]+)")
                .Select(m => (Name: m.Groups["t"].Value.Trim(), Points: System.Text.RegularExpressions.Regex.Matches(m.Groups["d"].Value, "[ML] (?<x>[-0-9.]+) (?<y>[-0-9.]+)")
                    .Select(p => (X: Num(p.Groups["x"].Value), Y: Num(p.Groups["y"].Value))).ToList()))
                .ToList();
            (double MinX, double MaxX, double MinY, double MaxY) Extent(IEnumerable<(double X, double Y)> points)
            {
                var list = points.ToList();
                Assert.NotEmpty(list);
                return (list.Min(p => p.X), list.Max(p => p.X), list.Min(p => p.Y), list.Max(p => p.Y));
            }

            var all = Extent(regions.SelectMany(r => r.Points));
            Assert.True(all.MinX >= 0 && all.MaxX <= width && all.MinY >= 0 && all.MaxY <= height, $"map leaves the chart: {all} in {width}x{height}");
            var lower48 = Extent(regions.Where(r => r.Name is not ("Alaska" or "Hawaii" or "Puerto Rico")).SelectMany(r => r.Points));
            var puertoRico = Extent(regions.Where(r => r.Name == "Puerto Rico").SelectMany(r => r.Points));
            Assert.True(puertoRico.MinY >= lower48.MinY && puertoRico.MaxY <= lower48.MaxY, $"Puerto Rico {puertoRico} sits outside the lower 48 {lower48}");
            // The lower 48 now set the map's extent (the insets sit inside it), whatever the card's shape.
            Assert.True((lower48.MaxX - lower48.MinX) / (all.MaxX - all.MinX) > .90 && (lower48.MaxY - lower48.MinY) / (all.MaxY - all.MinY) > .90,
                $"lower 48 {lower48} do not set the map extent {all}");
            Assert.True((lower48.MaxX - lower48.MinX) / width > .60, $"lower 48 span {(lower48.MaxX - lower48.MinX) / width:P0} of the width: lower 48 {lower48}, all {all} in {width}x{height}");
            var texas = Extent(regions.Where(r => r.Name == "Texas").SelectMany(r => r.Points));
            foreach (var inset in new[] { "Alaska", "Hawaii" })
            {
                var e = Extent(regions.Where(r => r.Name == inset).SelectMany(r => r.Points));
                Assert.True(e.MaxY > texas.MinY && e.MinX < texas.MaxX, $"{inset} {e} is not below and left of Texas {texas}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Map_UsStatesPoints_MarkersMoveWithTheirInsets()
    {
        // Lower-case, hyphenated MAP_NAME also reaches the insets (its source authority is upper-cased).
        static string Choropleth(string options) => $@"
SELECT 'California' AS State, 1 AS Revenue INTO #state_rev;
CREATE VISUAL M AS MAP (SOURCE = #state_rev, MAPPINGS (REGION = State, VALUE = Revenue), OPTIONS (MAP_NAME = US_STATES{options}));
CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = M));";
        const string points = @"
SELECT -149.90 AS Lon, 61.22 AS Lat, 'Anchorage' AS Label INTO #pts
UNION ALL SELECT -122.33, 47.61, 'Seattle'
UNION ALL SELECT -64.93, 18.34, 'St. Thomas';
CREATE VISUAL M AS MAP (SOURCE = #pts, MAPPINGS (LON = Lon, LAT = Lat, TOOLTIP = Label), OPTIONS (MAP_NAME = 'us-states', MODE = 'POINTS'));
CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = M));";

        var shapes = GeographicRegions(await RenderMapScriptAsync(Choropleth("")));
        var svg = await RenderMapScriptAsync(points);
        static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        var box = System.Text.RegularExpressions.Regex.Match(svg, "viewBox='0 0 (?<w>[0-9.]+) (?<h>[0-9.]+)'");
        var (width, height) = (Num(box.Groups["w"].Value), Num(box.Groups["h"].Value));
        var markers = System.Text.RegularExpressions.Regex.Matches(svg, "<circle(?<a>[^>]*plot-geographic-point[^>]*)>\\s*<title>(?<t>[^<]+)")
            .ToDictionary(m => m.Groups["t"].Value, m => (
                X: Num(System.Text.RegularExpressions.Regex.Match(m.Groups["a"].Value, "cx='(?<v>[-0-9.]+)'").Groups["v"].Value),
                Y: Num(System.Text.RegularExpressions.Regex.Match(m.Groups["a"].Value, "cy='(?<v>[-0-9.]+)'").Groups["v"].Value)));

        foreach (var (label, state) in new[] { ("Anchorage", "Alaska"), ("Seattle", "Washington") })
        {
            var (x, y) = markers[label];
            var e = MapExtent(shapes.Where(s => s.Name == state).SelectMany(s => s.Points));
            Assert.True(x >= e.MinX && x <= e.MaxX && y >= e.MinY && y <= e.MaxY, $"{label} marker ({x}, {y}) is off {state} {e}");
        }
        // Puerto Rico's true position used to stretch the fit far enough to include the Virgin Islands.
        var (vx, vy) = markers["St. Thomas"];
        var map = MapExtent(shapes.SelectMany(s => s.Points));
        Assert.True(vx >= map.MinX && vx <= map.MaxX && vy >= map.MinY && vy <= map.MaxY, $"St. Thomas marker ({vx}, {vy}) is off the map {map} in {width}x{height}");

        // An explicit view is the author's own framing: Alaska stays north-west of Washington.
        var zoomed = GeographicRegions(await RenderMapScriptAsync(Choropleth(", ZOOM = 1")));
        var alaska = MapExtent(zoomed.Where(r => r.Name == "Alaska").SelectMany(r => r.Points));
        var washington = MapExtent(zoomed.Where(r => r.Name == "Washington").SelectMany(r => r.Points));
        Assert.True(alaska.MinY < washington.MinY && alaska.MinX < washington.MinX, $"Alaska {alaska} is not north-west of Washington {washington}");
    }

    private static async Task<string> RenderMapScriptAsync(string script)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"map_script_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, script);
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var visual = (await service.GetManifestAsync()).Visuals.Single(v => v.Name == "M");
            Assert.Null(visual.Error);
            return new SvgChartRenderer().Render(visual)!;
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    private static List<(string Name, List<(double X, double Y)> Points)> GeographicRegions(string svg)
    {
        static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return System.Text.RegularExpressions.Regex.Matches(svg, "class='plot-geographic-region'[^>]* d='(?<d>[^']+)'[^>]*><title>(?<t>[^<:]+)")
            .Select(m => (m.Groups["t"].Value.Trim(), System.Text.RegularExpressions.Regex.Matches(m.Groups["d"].Value, "[ML] (?<x>[-0-9.]+) (?<y>[-0-9.]+)")
                .Select(p => (Num(p.Groups["x"].Value), Num(p.Groups["y"].Value))).ToList()))
            .ToList();
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) MapExtent(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        Assert.NotEmpty(list);
        return (list.Min(p => p.X), list.Max(p => p.X), list.Min(p => p.Y), list.Max(p => p.Y));
    }

    [Fact]
    public async Task Map_WithoutZoomOrCenter_FitsUsStatesToThePlot()
    {
        // The projection mapped the whole world onto the plot, so a US_STATES choropleth was
        // drawn at world scale: about a third of the width, left of centre (2026-11-21).
        var scriptPath = Path.Combine(Path.GetTempPath(), $"map_fit_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 'California' AS State, 1250000 AS Revenue INTO #state_rev
UNION ALL SELECT 'Texas', 980000;

CREATE VISUAL RevenueMap AS MAP (
  SOURCE = #state_rev,
  MAPPINGS (REGION = State, VALUE = Revenue),
  OPTIONS (MAP_NAME = US_STATES, TITLE = 'Revenue by State')
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = RevenueMap));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "RevenueMap");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual)!;

            static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            var box = System.Text.RegularExpressions.Regex.Match(svg, "viewBox='0 0 (?<w>[0-9.]+) (?<h>[0-9.]+)'");
            var width = Num(box.Groups["w"].Value);
            var height = Num(box.Groups["h"].Value);
            var points = System.Text.RegularExpressions.Regex.Matches(svg, "class='plot-geographic-region'[^>]* d='(?<d>[^']+)'")
                .SelectMany(m => System.Text.RegularExpressions.Regex.Matches(m.Groups["d"].Value, "[ML] (?<x>[-0-9.]+) (?<y>[-0-9.]+)"))
                .Select(m => (X: Num(m.Groups["x"].Value), Y: Num(m.Groups["y"].Value))).ToList();
            Assert.NotEmpty(points);
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);

            Assert.True(minX >= 0 && maxX <= width && minY >= 0 && maxY <= height, $"map leaves the chart: x {minX}..{maxX}, y {minY}..{maxY} in {width}x{height}");
            Assert.True((maxX - minX) / width > .75 || (maxY - minY) / height > .75, $"map spans x {minX}..{maxX}, y {minY}..{maxY} in {width}x{height}");
            Assert.InRange((minX + maxX) / 2 / width, .4, .6);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Map_ZoomAndCenter_PointsModeWithColorAndTooltip_RendersCorrectly()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"map_pts_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT -74.0060 AS Lon, 40.7128 AS Lat, 100 AS Val, '#10b981' AS MarkerColor, 'NYC Hub: 100 units' AS CustomTip INTO #pts;

CREATE VISUAL HubMap AS MAP (
  SOURCE = #pts,
  MAPPINGS (
    LON = Lon,
    LAT = Lat,
    VALUE = Val,
    COLOR = MarkerColor,
    TOOLTIP = CustomTip
  ),
  OPTIONS (
    TITLE = 'Hub Locations',
    MODE = 'POINTS',
    ZOOM = 2,
    CENTER = (40.7128, -74.0060)
  )
);

CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = HubMap));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "HubMap");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual);

            Assert.Contains("plot-geographic-point", svg);
            Assert.Contains("fill='#10b981'", svg);
            Assert.Contains("<title>NYC Hub: 100 units</title>", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Theme_PerVisualOverridesAndFontFamily_AppliesCorrectly()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"theme_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
CREATE THEME BrandTheme AS (
  PRIMARY = '#1e40af',
  FONT_FAMILY = 'Inter, sans-serif',
  [BAR] (COLORS = ('#e11d48', '#f59e0b'))
);

SELECT 'Alpha' AS Category, 42 AS Sales INTO #sales;

CREATE VISUAL SalesBar AS BAR (
  SOURCE = #sales,
  MAPPINGS (X = Category, Y = Sales),
  OPTIONS (TITLE = 'Sales by Brand', THEME = 'BrandTheme')
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SalesBar));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var customTheme = Assert.Single(manifest.CustomThemes!);
            Assert.Equal("BrandTheme", customTheme.Name);

            // Font family is preserved in config
            var configJson = customTheme.Config.ToString();
            Assert.Contains("Inter, sans-serif", configJson);

            // Visual uses custom BAR override color
            var visual = manifest.Visuals.Single(v => v.Name == "SalesBar");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual);
            Assert.Contains("#e11d48", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Scatter_FormattingOverlaysAndZoomSlider_RendersCorrectly()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"scatter_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 10 AS Cost, 20 AS Rev INTO #scatter_data;
INSERT INTO #scatter_data (Cost, Rev) VALUES (20, 50), (30, 90);

CREATE VISUAL PerfScatter AS SCATTER (
  SOURCE = #scatter_data,
  MAPPINGS (X = Cost, Y = Rev),
  OPTIONS (TITLE = 'Cost vs Rev', ZOOM_SLIDER = ON),
  OVERLAYS (
    REFERENCE_LINE (VALUE = 40, LABEL = 'Break-even', STYLE = DASHED, COLOR = '#dc2626')
  ),
  FORMATTING (
    WHEN Rev > 40 THEN '#16a34a'
  )
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = PerfScatter));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);
            var visual = manifest.Visuals.Single(v => v.Name == "PerfScatter");
            Assert.Null(visual.Error);
            var svg = new SvgChartRenderer().Render(visual);

            Assert.True(visual.Options.ContainsKey("ZOOM_SLIDER") && (visual.Options["ZOOM_SLIDER"] == "True" || visual.Options["ZOOM_SLIDER"] == "ON"));
            Assert.Contains("plot-reference-line", svg);
            Assert.Contains("Break-even", svg);
            Assert.Contains("fill='#16a34a'", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task RadarAndHeatmap_DataLabels_RendersCorrectly()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"labels_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, @"
SELECT 'Skills' AS Category, 85 AS Speed, 90 AS Reliability, 75 AS Comfort INTO #radar_data;

SELECT 'North' AS Region, 'Q1' AS Period, 120 AS TotalRev INTO #heatmap_data;
INSERT INTO #heatmap_data (Region, Period, TotalRev) VALUES ('South', 'Q1', 80);

CREATE VISUAL SkillsRadar AS RADAR (
  SOURCE = #radar_data,
  OPTIONS (TITLE = 'Skills Profile', DATA_LABELS = ON)
);

CREATE VISUAL GeoHeatmap AS HEATMAP (
  SOURCE = #heatmap_data,
  MAPPINGS (X = Period, Y = Region, VALUE = TotalRev),
  OPTIONS (TITLE = 'Regional Heatmap', DATA_LABELS = ON)
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A B', MAP ('A' = SkillsRadar, 'B' = GeoHeatmap));
");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            Assert.True(manifest.Error == null, manifest.Error);

            var radar = manifest.Visuals.Single(v => v.Name == "SkillsRadar");
            Assert.Null(radar.Error);
            var radarSvg = new SvgChartRenderer().Render(radar);
            Assert.Contains("plot-data-label", radarSvg);

            var heatmap = manifest.Visuals.Single(v => v.Name == "GeoHeatmap");
            Assert.Null(heatmap.Error);
            var heatmapSvg = new SvgChartRenderer().Render(heatmap);
            Assert.Contains("plot-data-label", heatmapSvg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
