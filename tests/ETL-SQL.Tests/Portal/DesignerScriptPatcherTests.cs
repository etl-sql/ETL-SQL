using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

public class DesignerScriptPatcherTests
{
    private readonly DesignerScriptPatcher _patcher = new();

    [Fact]
    public void AddsReportParameterWithoutRewritingExistingSqlOrPresentation()
    {
        const string original = """
            -- hand-authored preparation stays byte-identical
            WITH source_rows AS (SELECT Region FROM #sales)
            SELECT Region INTO #regions FROM source_rows;

            CREATE VISUAL RegionSlicer AS SLICER (
                SOURCE = #regions,
                MAPPINGS (VALUE = Region)
            );

            CREATE PAGE [Main] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = RegionSlicer))
            );
            """;
        var protectedSql = original[..original.IndexOf("CREATE VISUAL", StringComparison.Ordinal)];
        var state = new DesignerStateDto(
            [
                new DesignerPageDto("p1", "Main", "Dashboard",
                [
                    new DesignerVisualDto(
                        "v1", "RegionSlicer", "SLICER", 1, 1, 12, 4, null, "regions",
                        new Dictionary<string, string> { ["VALUE"] = "Region" },
                        new Dictionary<string, string>())
                ])
            ],
            [],
            Parameters: [new DesignerParameterDto("@selected_region", "VARCHAR", "'All'", IsInput: true)]);

        var patched = _patcher.Patch(original, state);

        Assert.StartsWith("DECLARE @selected_region VARCHAR = 'All' INPUT;", patched, StringComparison.Ordinal);
        Assert.Contains(protectedSql, patched, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesPrecedingSqlAndComments_WhenVisualIsUpdated()
    {
        var originalScript = """
            -- Step 1: Initialize Database Connection
            CREATE CONNECTION pg AS POSTGRES(HOST='localhost', DATABASE='sales');

            /* Extract and aggregate active customer orders */
            SELECT customer_name, SUM(amount) AS total_revenue
            INTO #revenue_by_cust
            FROM pg.orders
            WHERE status = 'ACTIVE'
            GROUP BY customer_name;

            -- Visuals
            CREATE VISUAL v_rev AS BAR (
                TITLE = 'Initial Revenue',
                SOURCE = #revenue_by_cust,
                MAPPINGS (X = customer_name, Y = total_revenue)
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_rev
                    )
                )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Dashboard",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_rev",
                            Type: "BAR",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 6,
                            Title: "Updated Revenue 2026",
                            Dataset: "revenue_by_cust",
                            Mappings: new Dictionary<string, string> { ["X"] = "customer_name", ["Y"] = "total_revenue" },
                            Options: new Dictionary<string, string> { ["WIDTH"] = "100%", ["HEIGHT"] = "450px" }
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        // Preceding SQL and comments must remain 100% intact
        Assert.Contains("-- Step 1: Initialize Database Connection", patched);
        Assert.Contains("CREATE CONNECTION pg AS POSTGRES(HOST='localhost', DATABASE='sales');", patched);
        Assert.Contains("/* Extract and aggregate active customer orders */", patched);
        Assert.Contains("SELECT customer_name, SUM(amount) AS total_revenue", patched);
        Assert.Contains("INTO #revenue_by_cust", patched);
        Assert.Contains("WHERE status = 'ACTIVE'", patched);

        // Visual title and styles must be updated
        Assert.Contains("TITLE = 'Updated Revenue 2026'", patched);
        Assert.Contains("STYLE (WIDTH = '100%', HEIGHT = '450px')", patched);
    }

    [Fact]
    public void InsertsNewVisualBeforePage_WhenVisualAdded()
    {
        var originalScript = """
            -- Staging query
            SELECT 1 AS id INTO #data;

            CREATE VISUAL v_chart1 AS BAR (
                TITLE = 'Chart 1',
                SOURCE = #data
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_chart1
                    )
                )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Dashboard",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_chart1",
                            Type: "BAR",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 6,
                            GridRowSpan: 4,
                            Title: "Chart 1",
                            Dataset: "data",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        ),
                        new DesignerVisualDto(
                            Id: "v2",
                            Name: "v_chart2",
                            Type: "LINE",
                            GridCol: 7,
                            GridRow: 1,
                            GridColSpan: 6,
                            GridRowSpan: 4,
                            Title: "Chart 2",
                            Dataset: "data",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("CREATE VISUAL v_chart1 AS BAR", patched);
        Assert.Contains("CREATE VISUAL v_chart2 AS LINE", patched);
        Assert.Contains("TITLE = 'Chart 2'", patched);
        Assert.Contains("'A' = v_chart1", patched);
        Assert.Contains("'B' = v_chart2", patched);
        Assert.Contains("SELECT 1 AS id INTO #data;", patched);
    }

    [Fact]
    public void DeletesVisual_WhenRemovedFromDesignerState()
    {
        var originalScript = """
            SELECT 1 AS id INTO #data;

            CREATE VISUAL v_keep AS BAR (
                TITLE = 'Keep Me',
                SOURCE = #data
            );

            CREATE VISUAL v_remove AS PIE (
                TITLE = 'Remove Me',
                SOURCE = #data
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A B',
                    MAP (
                        'A' = v_keep,
                        'B' = v_remove
                    )
                )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Dashboard",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_keep",
                            Type: "BAR",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 4,
                            Title: "Keep Me",
                            Dataset: "data",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("CREATE VISUAL v_keep AS BAR", patched);
        Assert.DoesNotContain("CREATE VISUAL v_remove AS PIE", patched);
        Assert.DoesNotContain("'B' = v_remove", patched);
    }

    [Fact]
    public void ReconcilesReportStyleTheme_Surgically()
    {
        var originalScript = """
            -- Main ETL
            SELECT 42 AS val INTO #metrics;

            CREATE VISUAL v_card AS CARD (
                TITLE = 'Metric',
                SOURCE = #metrics
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_card
                    )
                )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Dashboard",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_card",
                            Type: "CARD",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 4,
                            Title: "Metric",
                            Dataset: "metrics",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets: [],
            ReportStyle: new DesignerReportStyleDto(Theme: "dark", Accent: "#00E5FF")
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("SET REPORT THEME = 'dark';", patched);
        Assert.Contains("SELECT 42 AS val INTO #metrics;", patched);
        Assert.Contains("CREATE VISUAL v_card AS CARD", patched);
    }

    [Fact]
    public void HandlesEmptyOrNullScript_ByGeneratingScaffold()
    {
        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Page1",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_pie",
                            Type: "PIE",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 4,
                            Title: "Market Share",
                            Dataset: null,
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch("", state);

        Assert.Contains("CREATE VISUAL v_pie AS PIE", patched);
        Assert.Contains("CREATE PAGE [Page1] AS DASHBOARD", patched);
    }

    [Fact]
    public void UpdatingSecondPage_PreservesFirstPageAndDataPreparationByteForByte()
    {
        const string original = """
            -- data preparation must not move
            WITH staged AS (
                SELECT category, amount FROM source.orders
            )
            SELECT category, amount INTO #stage FROM staged;

            CREATE VISUAL first_chart AS BAR (
                TITLE = 'First',
                SOURCE = &data,
                MAPPINGS (X = category, Y = amount)
            );

            CREATE VISUAL second_chart AS LINE (
                TITLE = 'Second',
                SOURCE = &data,
                MAPPINGS (X = category, Y = amount)
            );

            CREATE PAGE [FirstPage] AS DASHBOARD (
                -- first page trivia
                LAYOUT (STRUCTURE = 'A', MAP ('A' = first_chart))
            );

            CREATE PAGE [SecondPage] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = second_chart))
            );
            """;

        var firstPageEnd = original.IndexOf("CREATE PAGE [SecondPage]", StringComparison.Ordinal);
        var protectedPrefix = original[..firstPageEnd];
        var state = State(
            Page("p1", "FirstPage", Visual("v1", "first_chart", "BAR", 1, 1, 12, 4, "First")),
            Page("p2", "SecondPage", Visual("v2", "second_chart", "LINE", 7, 1, 6, 4, "Second")));

        var patched = _patcher.Patch(original, state);

        Assert.StartsWith(protectedPrefix, patched, StringComparison.Ordinal);
        Assert.Contains("STRUCTURE = '. A'", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatingVisualTitle_PreservesCteAndNestedBodyTrivia()
    {
        const string original = ReportDesignerRoundTripFixtures.NestedClauseTriviaScript;
        var state = ReportDesignerRoundTripFixtures.NestedClauseTriviaState("After");

        var patched = _patcher.Patch(original, state);

        Assert.Contains("TITLE = 'After'", patched, StringComparison.Ordinal);
        foreach (var trivia in new[]
                 {
                     "-- preserve the complete CTE chain",
                     "-- category binding stays with the author",
                     "/* amount binding */",
                     "-- retain option rationale",
                     "-- retain action rationale",
                     "/* selection policy */",
                     "-- retain sizing rationale"
                 })
            Assert.Contains(trivia, patched, StringComparison.Ordinal);

        Assert.StartsWith(original[..original.IndexOf("CREATE VISUAL", StringComparison.Ordinal)], patched, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void FiftySequentialMutations_PreserveLineEndingsAndStableDataBytes(string lineEnding)
    {
        var originalLf = """
            -- immutable preparation
            SELECT category, amount INTO #stage FROM source.orders;

            CREATE VISUAL chart AS BAR (
                TITLE = 'Chart 0',
                SOURCE = &data,
                MAPPINGS (X = category, Y = amount)
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = chart))
            );
            """;
        var script = ReportDesignerRoundTripFixtures.WithLineEnding(originalLf, lineEnding);
        var protectedPrefix = script[..script.IndexOf("CREATE VISUAL", StringComparison.Ordinal)];

        for (var iteration = 1; iteration <= 50; iteration++)
        {
            var column = iteration % 2 == 0 ? 1 : 7;
            script = _patcher.Patch(script, State(Page("p1", "Dashboard", Visual(
                "v1", "chart", "BAR", column, 1, 6, 4, $"Chart {iteration}"))));
        }

        Assert.StartsWith(protectedPrefix, script, StringComparison.Ordinal);
        Assert.Contains("TITLE = 'Chart 50'", script, StringComparison.Ordinal);
        if (lineEnding == "\r\n")
            Assert.DoesNotMatch("(?<!\\r)\\n", script);
        else
            Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidIntermediateScript_IsReturnedUnchanged()
    {
        const string invalid = """
            SELECT 1 AS value INTO #stage;
            CREATE VISUAL chart AS BAR (
                TITLE = 'unfinished',
                SOURCE = &data,
                MAPPINGS (X = category, Y = )
            """;

        var patched = _patcher.Patch(invalid, State(Page("p1", "Dashboard", Visual(
            "v1", "chart", "BAR", 1, 1, 12, 4, "Changed"))));

        Assert.Equal(invalid, patched);
    }

    [Fact]
    public void PaginatedPageSetup_RoundTripsThroughSharedParserAndPatcher()
    {
        const string original = """
            -- hand-authored data preparation must survive page setup changes
            SELECT * INTO #detail_rows FROM &orders;

            CREATE PAGE [Invoice] AS PAGINATED (
                LAYOUT (STRUCTURE = '.'),
                PRINT_LAYOUT (
                    PAGE_SIZE = 'Letter',
                    ORIENTATION = 'PORTRAIT',
                    MARGINS = (0.75, 0.75, 0.75, 0.75),
                    UNITS = 'in',
                    OVERFLOW = 'SPLIT'
                )
            );
            """;
        var parsed = new ETL_SQL.Reporting.Authoring.DesignerScriptParsingService().Parse(original);
        var page = Assert.Single(parsed.Pages);

        Assert.Equal("Paginated", page.Mode);
        Assert.Equal("Letter", page.PrintLayout?.PageSize);
        Assert.Equal(0.75m, page.PrintLayout?.MarginTop);

        var state = new DesignerStateDto(
            [new DesignerPageDto("p1", "Invoice", "Paginated", [],
                new DesignerPageLayoutDto("A4", "LANDSCAPE", 0.5m, 0.5m, 0.5m, 0.5m, "in", "SPLIT"))],
            []);
        var patched = _patcher.Patch(original, state);

        Assert.StartsWith("-- hand-authored data preparation", patched, StringComparison.Ordinal);
        Assert.Contains("PAGE_SIZE = 'A4'", patched, StringComparison.Ordinal);
        Assert.Contains("ORIENTATION = 'LANDSCAPE'", patched, StringComparison.Ordinal);
        Assert.Contains("MARGINS = (0.5, 0.5, 0.5, 0.5)", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("PAGE_SIZE = 'Letter'", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void PaginatedDetailBreak_RoundTripsWithoutRewritingHandAuthoredSql()
    {
        const string original = """
            -- preserve this preparation exactly
            SELECT * INTO #detail_rows FROM &orders;

            CREATE VISUAL detail_rows AS TABLE (
                SOURCE = #detail_rows,
                OPTIONS (PAGE_SIZE = 0)
            );

            CREATE PAGE [Invoice] AS PAGINATED (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = detail_rows))
            );
            """;
        var parsed = new ETL_SQL.Reporting.Authoring.DesignerScriptParsingService().Parse(original);
        var visual = Assert.Single(Assert.Single(parsed.Pages).Visuals);
        visual.Options["print_layout"] = "PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)";

        var patched = new ETL_SQL.Reporting.Authoring.DesignerScriptPatcher().Patch(original, parsed);
        var roundTripped = new ETL_SQL.Reporting.Authoring.DesignerScriptParsingService().Parse(patched);

        Assert.StartsWith("-- preserve this preparation exactly", patched, StringComparison.Ordinal);
        Assert.Contains("PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)", patched, StringComparison.Ordinal);
        Assert.Contains("PAGE_BREAK_AFTER = ON", Assert.Single(Assert.Single(roundTripped.Pages).Visuals).Options["print_layout"], StringComparison.Ordinal);
    }

    [Fact]
    public void PatchesVisualFormattingStyleOptions_WhenPropertiesPanelUpdated()
    {
        const string original = """
            SELECT 'Apples' AS category, 100 AS amount INTO #data;

            CREATE VISUAL v_bar AS BAR (
                TITLE = 'Sales',
                SOURCE = #data,
                MAPPINGS (X = category, Y = amount)
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_bar
                    )
                )
            );
            """;

        var visualOptions = new Dictionary<string, string>
        {
            ["BACKGROUND"] = "#f8fafc",
            ["COLOR"] = "#0f172a",
            ["BORDER"] = "1px solid #e2e8f0",
            ["BORDER_RADIUS"] = "10px",
            ["SHADOW"] = "ON",
            ["FONT"] = "Inter, sans-serif",
            ["FONT_SIZE"] = "14px",
            ["FONT_WEIGHT"] = "600",
            ["OPACITY"] = "0.95"
        };

        var state = State(Page("p1", "Dashboard", Visual(
            "v1", "v_bar", "BAR", 1, 1, 12, 4, "Sales", options: visualOptions)));

        var patched = _patcher.Patch(original, state);

        Assert.Contains("STYLE (", patched, StringComparison.Ordinal);
        Assert.Contains("BACKGROUND = '#f8fafc'", patched, StringComparison.Ordinal);
        Assert.Contains("COLOR = '#0f172a'", patched, StringComparison.Ordinal);
        Assert.Contains("BORDER = '1px solid #e2e8f0'", patched, StringComparison.Ordinal);
        Assert.Contains("BORDER_RADIUS = '10px'", patched, StringComparison.Ordinal);
        Assert.Contains("SHADOW = ON", patched, StringComparison.Ordinal);
        Assert.Contains("FONT = 'Inter, sans-serif'", patched, StringComparison.Ordinal);
        Assert.Contains("FONT_SIZE = '14px'", patched, StringComparison.Ordinal);
        Assert.Contains("FONT_WEIGHT = '600'", patched, StringComparison.Ordinal);
        Assert.Contains("OPACITY = '0.95'", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchesVisualFormattingInspectorClauses_AndRoundTripsTypedState()
    {
        const string original = """
            SELECT 'North' AS region, -125.5 AS margin INTO #data;

            CREATE VISUAL MarginTable AS TABLE (
                TITLE = 'Margin by region',
                SOURCE = #data,
                MAPPINGS (region, margin)
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = MarginTable))
            );
            """;

        var formatting = new DesignerVisualFormattingDto(
            Title: new DesignerTextFormattingDto(
                "Margin by region", "#f8fafc", "Segoe UI", "16px", "BOLD", "LEFT"),
            Subtitle: new DesignerTextFormattingDto("Negative values need review"),
            XAxis: new Dictionary<string, string> { ["LABEL"] = "Region" },
            YAxis: new Dictionary<string, string>
            {
                ["LABEL"] = "Margin",
                ["MIN"] = "-500",
                ["MAX"] = "500",
                ["FORMAT"] = "$#,##0.00"
            },
            Palette: ["#2563eb", "#dc2626"],
            ConditionalRules:
            [
                new DesignerConditionalFormattingRuleDto("margin < 0", "#fee2e2", "#991b1b")
            ],
            Fields: new Dictionary<string, DesignerFieldFormattingDto>
            {
                ["MARGIN"] = new("$#,##0.00", "right", "Margin", true, "#dc2626")
            });
        var visual = new DesignerVisualDto(
            "v1", "MarginTable", "TABLE", 1, 1, 12, 4, "Margin by region", null,
            new Dictionary<string, string> { ["REGION"] = "region", ["MARGIN"] = "margin" },
            new Dictionary<string, string> { ["LEGEND"] = "OFF", ["inline_source"] = "#data" },
            Formatting: formatting);

        var state = State(Page("p1", "Dashboard", visual));
        var patched = _patcher.Patch(original, state);
        var roundTripped = new ETL_SQL.Reporting.Authoring.DesignerScriptParsingService().Parse(patched);
        var actual = Assert.Single(Assert.Single(roundTripped.Pages).Visuals);

        Assert.Contains("TITLE (TEXT = 'Margin by region', COLOR = '#f8fafc'", patched, StringComparison.Ordinal);
        Assert.Contains("SUBTITLE = 'Negative values need review'", patched, StringComparison.Ordinal);
        Assert.Contains("margin FORMAT '$#,##0.00' ALIGN 'right' DATA_BAR COLOR '#dc2626' AS 'Margin'", patched, StringComparison.Ordinal);
        Assert.Contains("STYLE (PALETTE = ('#2563eb', '#dc2626'))", patched, StringComparison.Ordinal);
        Assert.Contains("OPTIONS (LEGEND = OFF, X_AXIS (LABEL = 'Region'), Y_AXIS (LABEL = 'Margin', MIN = -500, MAX = 500, FORMAT = '$#,##0.00'))", patched, StringComparison.Ordinal);
        Assert.Contains("FORMATTING (WHEN margin < 0 THEN '#fee2e2' FONT_COLOR '#991b1b')", patched, StringComparison.Ordinal);
        Assert.Equal("$#,##0.00", actual.Formatting!.Fields!["MARGIN"].Format);
        Assert.True(actual.Formatting.Fields["MARGIN"].DataBar);
        Assert.Equal("#991b1b", Assert.Single(actual.Formatting.ConditionalRules!).FontColor);
        Assert.Equal(["#2563eb", "#dc2626"], actual.Formatting.Palette);
    }

    private static DesignerStateDto State(params DesignerPageDto[] pages) =>
        new(pages.ToList(), []);

    private static DesignerPageDto Page(string id, string name, params DesignerVisualDto[] visuals) =>
        new(id, name, "Dashboard", visuals.ToList());

    private static DesignerVisualDto Visual(
        string id,
        string name,
        string type,
        int column,
        int row,
        int columnSpan,
        int rowSpan,
        string title,
        Dictionary<string, string>? mappings = null,
        Dictionary<string, string>? options = null) =>
        new(
            id,
            name,
            type,
            column,
            row,
            columnSpan,
            rowSpan,
            title,
            "data",
            mappings ?? new Dictionary<string, string> { ["X"] = "category", ["Y"] = "amount" },
            options ?? new Dictionary<string, string>());
}
