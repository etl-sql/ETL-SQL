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
