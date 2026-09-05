using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class PageNavigationAndChartGapsTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    [Fact]
    public void Page_MobileLayout_ParsesAndSerializes()
    {
        const string sql = """
CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A B',
    MAP ('A' = Chart1, 'B' = Chart2),
    MOBILE_LAYOUT (
        STRUCTURE = 'A / B',
        MAP ('A' = Chart1, 'B' = Chart2),
        BREAKPOINT = 768
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreatePageStatement>().Single();

        Assert.NotNull(stmt.MobileLayout);
        Assert.Equal("A / B", stmt.MobileLayout.Structure);
        Assert.Equal(2, stmt.MobileLayout.SlotMap.Count);
        Assert.Equal("Chart1", stmt.MobileLayout.SlotMap["A"]);
        Assert.Equal("768", stmt.MobileLayout.Breakpoint);

        var serialized = stmt.ToSql();
        Assert.Contains("MOBILE_LAYOUT (", serialized);
        Assert.Contains("BREAKPOINT = 768", serialized);
    }

    [Fact]
    public async Task Page_OptionsAndActions_LowersToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"page_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 1 AS id INTO #t;
CREATE VISUAL V1 AS TABLE (SOURCE = #t);
CREATE VISUAL V2 AS TABLE (SOURCE = #t);

CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A B',
    MAP ('A' = V1, 'B' = V2),
    MOBILE_LAYOUT (
        STRUCTURE = 'A / B',
        MAP ('A' = V1, 'B' = V2),
        BREAKPOINT = 640
    ),
    OPTIONS (
        BACKGROUND_IMAGE = 'url(/assets/bg.png)',
        BACKGROUND_SIZE = 'cover',
        MAX_WIDTH = 1280,
        ALIGN_CONTENT = 'CENTER',
        OVERFLOW = 'SCROLL',
        TRANSITION = 'FADE'
    ),
    ACTIONS (
        ON_LOAD = (SET_PARAMETER(@Loaded, 1), REFRESH_REPORT)
    ),
    VISIBLE = @ShowOverview
);
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var page = manifest.Pages.Single(p => p.Name == "Overview");

            Assert.NotNull(page.MobileLayout);
            Assert.Equal("A / B", page.MobileLayout.Structure);
            Assert.Equal("640", page.MobileLayout.Breakpoint);
            Assert.Equal("V1", page.MobileLayout.SlotMap["A"]);

            Assert.NotNull(page.Options);
            Assert.Equal("url(/assets/bg.png)", page.Options["BACKGROUND_IMAGE"]);
            Assert.Equal("cover", page.Options["BACKGROUND_SIZE"]);
            Assert.Equal("1280", page.Options["MAX_WIDTH"]);
            Assert.Equal("CENTER", page.Options["ALIGN_CONTENT"]);
            Assert.Equal("SCROLL", page.Options["OVERFLOW"]);
            Assert.Equal("FADE", page.Options["TRANSITION"]);

            Assert.Equal("@ShowOverview", page.VisibleExpression);
            Assert.NotNull(page.Actions);
            Assert.Contains(page.Actions, a => a.Trigger == "ON_LOAD" && a.Type == "SET_PARAMETER" && a.ParameterName == "@Loaded");
            Assert.Contains(page.Actions, a => a.Trigger == "ON_LOAD" && a.Type == "REFRESH");
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public void Navigation_RichItems_GroupsAndLinks_ParsesAndSerializes()
    {
        const string sql = """
CREATE NAVIGATION MainNav AS TAB (
    PAGES (
        Overview (ICON = 'dashboard', LABEL = 'Dashboard Home', BADGE = '3'),
        Reports (ICON = 'chart-bar', LABEL = 'Detailed Reports')
    ),
    GROUP ('Management' = (Users, Settings)),
    LINK ('External Docs' = OPEN_URL('https://example.com/docs')),
    HIDE_INVISIBLE = ON,
    STYLE (COLOR = '#333333', FONT_SIZE = '14px'),
    ACTIVE_STYLE (COLOR = '#0066cc', FONT_WEIGHT = 'bold')
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateNavigationStatement>().Single();

        Assert.Equal(3, stmt.Items.Count);
        Assert.Equal("Overview", stmt.Items[0].PageName);
        Assert.Equal("dashboard", stmt.Items[0].Icon);
        Assert.Equal("Dashboard Home", stmt.Items[0].Label);
        Assert.Equal("3", stmt.Items[0].Badge);

        var extLink = stmt.Items.Single(i => i.IsExternalLink);
        Assert.Equal("External Docs", extLink.Label);
        Assert.Equal("https://example.com/docs", extLink.ExternalUrl);

        Assert.Single(stmt.Groups);
        Assert.Equal("Management", stmt.Groups[0].Title);
        Assert.Equal(new[] { "Users", "Settings" }, stmt.Groups[0].Items.Select(i => i.PageName));

        Assert.True(stmt.HideInvisible);
        Assert.Equal("#333333", stmt.Styles["COLOR"]);
        Assert.Equal("#0066cc", stmt.ActiveStyles["COLOR"]);

        var serialized = stmt.ToSql();
        Assert.Contains("PAGES (", serialized);
        Assert.Contains("ICON = 'dashboard'", serialized);
        Assert.Contains("GROUP ('Management' = (Users, Settings))", serialized);
        Assert.Contains("LINK ('External Docs' = OPEN_URL('https://example.com/docs'))", serialized);
        Assert.Contains("HIDE_INVISIBLE = ON", serialized);
        Assert.Contains("ACTIVE_STYLE (", serialized);
    }

    [Fact]
    public async Task Navigation_RichProperties_LowersToManifest()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"nav_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 1 AS id INTO #t;
CREATE VISUAL V AS TABLE (SOURCE = #t);
CREATE PAGE P1 AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));
CREATE PAGE P2 AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));

CREATE NAVIGATION TopNav AS BAR (
    PAGES (
        P1 (ICON = 'home', LABEL = 'Home', BADGE = 'New'),
        P2
    ),
    GROUP ('Extra' = (P2)),
    LINK ('Support' = OPEN_URL('https://support.example.com')),
    HIDE_INVISIBLE = ON,
    ACTIVE_STYLE (BACKGROUND = '#e0f0ff')
);
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var nav = manifest.Navigations!.Single(n => n.Name == "TopNav");

            Assert.True(nav.HideInvisible);
            Assert.NotNull(nav.Items);
            Assert.Equal(3, nav.Items.Count);
            Assert.Equal("home", nav.Items[0].Icon);
            Assert.Equal("Home", nav.Items[0].Label);
            Assert.Equal("New", nav.Items[0].Badge);

            var linkItem = nav.Items.Single(i => i.IsExternalLink);
            Assert.Equal("Support", linkItem.Label);
            Assert.Equal("https://support.example.com", linkItem.ExternalUrl);

            Assert.NotNull(nav.Groups);
            Assert.Single(nav.Groups);
            Assert.Equal("Extra", nav.Groups[0].Title);
            Assert.Equal(new[] { "P2" }, nav.Groups[0].Items.Select(i => i.PageName));

            Assert.NotNull(nav.ActiveStyles);
            Assert.Equal("#e0f0ff", nav.ActiveStyles["BACKGROUND"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task LineChart_NullHandling_Connect_ProducesConnectedPath()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"null_connect_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 'Jan' AS Month, 10.0 AS Revenue INTO #data;
INSERT INTO #data VALUES ('Feb', NULL);
INSERT INTO #data VALUES ('Mar', 30.0);

CREATE VISUAL TrendLine AS LINE (
    SOURCE = #data,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (NULL_HANDLING = 'CONNECT')
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = TrendLine));
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "TrendLine");
            Assert.Null(visual.Error);

            var svg = new SvgChartRenderer().Render(visual);
            Assert.NotNull(svg);
            // In CONNECT mode, Feb (null) is skipped, so points are connected into a single path
            var pathParts = svg.Split(" d='M ").Length - 1;
            Assert.True(pathParts >= 1);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task LineChart_NullHandling_Gap_ProducesMultiplePathSegments()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"null_gap_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 'Jan' AS Month, 10.0 AS Revenue INTO #data;
INSERT INTO #data VALUES ('Feb', 20.0);
INSERT INTO #data VALUES ('Mar', NULL);
INSERT INTO #data VALUES ('Apr', 40.0);
INSERT INTO #data VALUES ('May', 50.0);

CREATE VISUAL TrendLine AS LINE (
    SOURCE = #data,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (NULL_HANDLING = 'GAP')
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = TrendLine));
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "TrendLine");
            Assert.Null(visual.Error);

            var svg = new SvgChartRenderer().Render(visual);
            Assert.NotNull(svg);
            // In GAP mode, the null in Mar splits the path into two disconnected segments: Jan-Feb and Apr-May
            var pathDMatches = svg.Split(" d='M ").Length - 1;
            Assert.True(pathDMatches >= 2);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task LineChart_NullHandling_Zero_TreatsNullAsZero()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"null_zero_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 'Jan' AS Month, 10.0 AS Revenue INTO #data;
INSERT INTO #data VALUES ('Feb', NULL);
INSERT INTO #data VALUES ('Mar', 30.0);

CREATE VISUAL TrendLine AS LINE (
    SOURCE = #data,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (NULL_HANDLING = 'ZERO')
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = TrendLine));
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "TrendLine");
            Assert.Null(visual.Error);

            var svg = new SvgChartRenderer().Render(visual);
            Assert.NotNull(svg);
            // All 3 points are rendered in a single path (no gap split, zero is plotted)
            var pathDMatches = svg.Split(" d='M ").Length - 1;
            Assert.Equal(1, pathDMatches);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Chart_Annotations_RendersExtremaAndCoordinates()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"annotation_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 'Jan' AS Month, 10.0 AS Revenue INTO #data;
INSERT INTO #data VALUES ('Feb', 80.0);
INSERT INTO #data VALUES ('Mar', 25.0);

CREATE VISUAL SalesTrend AS LINE (
    SOURCE = #data,
    MAPPINGS (X = Month, Y = Revenue),
    ANNOTATIONS (
        POINT (SERIES = 'Revenue', TYPE = MAX, LABEL = 'Peak Sales', SYMBOL = 'pin'),
        POINT (SERIES = 'Revenue', TYPE = MIN, LABEL = 'Lowest Month', SYMBOL = 'arrow'),
        POINT (SERIES = 'Revenue', TYPE = COORD(1, 50), LABEL = 'Target Mark', SYMBOL = 'circle')
    )
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SalesTrend));
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "SalesTrend");
            Assert.Null(visual.Error);

            var svg = new SvgChartRenderer().Render(visual);
            Assert.NotNull(svg);
            Assert.Contains("class='plot-annotation-point'", svg);
            Assert.Contains("data-annotation-symbol='pin'", svg);
            Assert.Contains("data-annotation-symbol='arrow'", svg);
            Assert.Contains("data-annotation-symbol='circle'", svg);
            Assert.Contains("Peak Sales", svg);
            Assert.Contains("Lowest Month", svg);
            Assert.Contains("Target Mark", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task CustomChart_AnnotationsAndNullHandling_ParsesAndRenders()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"custom_chart_ann_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 'Q1' AS Qtr, 100 AS Val INTO #data;
INSERT INTO #data VALUES ('Q2', 300);
INSERT INTO #data VALUES ('Q3', 50);

CREATE VISUAL MultiChart AS CUSTOM (
    SOURCE = #data,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        LAYERS (
            trend = LINE (
                NULL_HANDLING = ZERO,
                ENCODINGS (
                    X = Qtr (TYPE = ORDINAL),
                    Y = Val (TYPE = QUANTITATIVE)
                )
            )
        ),
        ANNOTATIONS (
            POINT (SERIES = 'trend', TYPE = MAX, LABEL = 'Record High', SYMBOL = 'pin')
        )
    )
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = MultiChart));
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "MultiChart");
            Assert.Null(visual.Error);

            var svg = new SvgChartRenderer().Render(visual);
            Assert.NotNull(svg);
            Assert.Contains("Record High", svg);
            Assert.Contains("data-annotation-symbol='pin'", svg);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Chart_TooltipAndCrosshairOptions_StoredInManifestAndOptions()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"tooltip_test_{Guid.NewGuid()}.rptsql");
        File.WriteAllText(scriptPath, """
SELECT 'A' AS Cat, 10 AS Val INTO #data;

CREATE VISUAL CrosshairChart AS BAR (
    SOURCE = #data,
    MAPPINGS (X = Cat, Y = Val),
    OPTIONS (
        TOOLTIP_MODE = 'SHARED',
        TOOLTIP_POSITION = 'AUTO',
        CROSSHAIR = 'ON',
        CROSSHAIR_AXIS = 'X',
        CROSSHAIR_COLOR = '#ff0000',
        CROSSHAIR_DASH = '4,4',
        LINK_TOOLTIP = 'synced-group'
    )
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = CrosshairChart));
""");
        try
        {
            var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(v => v.Name == "CrosshairChart");
            Assert.Null(visual.Error);

            Assert.NotNull(visual.Options);
            Assert.Equal("SHARED", visual.Options["TOOLTIP_MODE"]);
            Assert.Equal("AUTO", visual.Options["TOOLTIP_POSITION"]);
            Assert.Equal("ON", visual.Options["CROSSHAIR"]);
            Assert.Equal("X", visual.Options["CROSSHAIR_AXIS"]);
            Assert.Equal("#ff0000", visual.Options["CROSSHAIR_COLOR"]);
            Assert.Equal("4,4", visual.Options["CROSSHAIR_DASH"]);
            Assert.Equal("synced-group", visual.Options["LINK_TOOLTIP"]);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
