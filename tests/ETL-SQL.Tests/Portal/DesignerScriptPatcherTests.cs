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

        Assert.Contains("SET REPORT STYLE (THEME = 'dark', ACCENT = '#00E5FF');", patched);
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
}
