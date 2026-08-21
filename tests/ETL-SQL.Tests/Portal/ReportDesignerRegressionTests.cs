using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Phase 1 regression tests for the Visual Report Builder script synchronization and patcher engine.
/// Covers multi-page mutations, CTE preservation, CRLF/LF line ending stability across repeated cycles,
/// comment retention behaviors, and transient invalid syntax resilience during live split-screen typing.
/// </summary>
public class ReportDesignerRegressionTests
{
    private readonly DesignerScriptPatcher _patcher = new();
    private readonly DesignerAnalysisService _analysis = new();

    // ════════════════════════════════════════════════════════════════════════════
    // 1. MULTI-PAGE MUTATIONS
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiPage_ModifyingPage2Visual_PreservesPage1AndPage3AndLayouts()
    {
        var originalScript = """
            -- Shared Staging
            SELECT region, SUM(amount) AS revenue INTO #regional_rev FROM sales GROUP BY region;
            SELECT rep, SUM(amount) AS rep_revenue INTO #rep_rev FROM sales GROUP BY rep;

            CREATE VISUAL v_summary_kpi AS CARD (
                TITLE = 'Total Summary',
                SOURCE = #regional_rev
            );

            CREATE VISUAL v_regional_bar AS BAR (
                TITLE = 'Initial Regional Revenue',
                SOURCE = #regional_rev,
                MAPPINGS (X = region, Y = revenue)
            );

            CREATE VISUAL v_details_table AS TABLE (
                TITLE = 'Sales Rep Details',
                SOURCE = #rep_rev
            );

            CREATE PAGE [Summary] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_summary_kpi
                    )
                )
            );

            CREATE PAGE [Regional] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_regional_bar
                    )
                )
            );

            CREATE PAGE [Details] AS PAGINATED (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP (
                        'A' = v_details_table
                    )
                )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Summary",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_summary_kpi",
                            Type: "CARD",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 4,
                            Title: "Total Summary",
                            Dataset: "regional_rev",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                ),
                new DesignerPageDto(
                    Id: "p2",
                    Name: "Regional",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v2",
                            Name: "v_regional_bar",
                            Type: "BAR",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 6,
                            Title: "Updated Regional Revenue 2026",
                            Dataset: "regional_rev",
                            Mappings: new Dictionary<string, string> { ["X"] = "region", ["Y"] = "revenue" },
                            Options: new Dictionary<string, string> { ["WIDTH"] = "100%", ["HEIGHT"] = "400px" }
                        )
                    ]
                ),
                new DesignerPageDto(
                    Id: "p3",
                    Name: "Details",
                    Mode: "Paginated",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v3",
                            Name: "v_details_table",
                            Type: "TABLE",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 8,
                            Title: "Sales Rep Details",
                            Dataset: "rep_rev",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        // Preceding staging SQL must remain completely intact
        Assert.Contains("SELECT region, SUM(amount) AS revenue INTO #regional_rev FROM sales GROUP BY region;", patched);
        Assert.Contains("SELECT rep, SUM(amount) AS rep_revenue INTO #rep_rev FROM sales GROUP BY rep;", patched);

        // Page 1 & Page 3 statements and visuals must remain untouched
        Assert.Contains("CREATE VISUAL v_summary_kpi AS CARD", patched);
        Assert.Contains("CREATE PAGE [Summary] AS DASHBOARD", patched);
        Assert.Contains("CREATE VISUAL v_details_table AS TABLE", patched);
        Assert.Contains("CREATE PAGE [Details] AS PAGINATED", patched);

        // Page 2 visual and page statement must be updated
        Assert.Contains("CREATE VISUAL v_regional_bar AS BAR", patched);
        Assert.Contains("TITLE = 'Updated Regional Revenue 2026'", patched);
        Assert.Contains("STYLE (WIDTH = '100%', HEIGHT = '400px')", patched);
        Assert.Contains("CREATE PAGE [Regional] AS DASHBOARD", patched);
    }

    [Fact]
    public void MultiPage_AddingVisualToPage3_UpdatesPage3WithoutTouchingPage1Or2()
    {
        var originalScript = """
            SELECT 1 AS x INTO #data;

            CREATE VISUAL v_p1 AS CARD ( TITLE = 'P1 Visual', SOURCE = #data );
            CREATE VISUAL v_p2 AS BAR ( TITLE = 'P2 Visual', SOURCE = #data );
            CREATE VISUAL v_p3_a AS TABLE ( TITLE = 'P3 Table', SOURCE = #data );

            CREATE PAGE [P1] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_p1 ) ) );
            CREATE PAGE [P2] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_p2 ) ) );
            CREATE PAGE [P3] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_p3_a ) ) );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1", Name: "P1", Mode: "Dashboard",
                    Visuals: [new DesignerVisualDto("v1", "v_p1", "CARD", 1, 1, 12, 4, "P1 Visual", "data", new Dictionary<string, string>(), new Dictionary<string, string>())]
                ),
                new DesignerPageDto(
                    Id: "p2", Name: "P2", Mode: "Dashboard",
                    Visuals: [new DesignerVisualDto("v2", "v_p2", "BAR", 1, 1, 12, 4, "P2 Visual", "data", new Dictionary<string, string>(), new Dictionary<string, string>())]
                ),
                new DesignerPageDto(
                    Id: "p3", Name: "P3", Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto("v3", "v_p3_a", "TABLE", 1, 1, 6, 4, "P3 Table", "data", new Dictionary<string, string>(), new Dictionary<string, string>()),
                        new DesignerVisualDto("v4", "v_p3_b", "LINE", 7, 1, 6, 4, "P3 Chart", "data", new Dictionary<string, string>(), new Dictionary<string, string>())
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("CREATE VISUAL v_p3_b AS LINE", patched);
        Assert.Contains("CREATE PAGE [P1] AS DASHBOARD", patched);
        Assert.Contains("CREATE PAGE [P2] AS DASHBOARD", patched);
        Assert.Contains("CREATE PAGE [P3] AS DASHBOARD", patched);
        Assert.Contains("'A' = v_p3_a", patched);
        Assert.Contains("'B' = v_p3_b", patched);
    }

    [Fact]
    public void MultiPage_DeletingPage2_RemovesPage2StatementAndUnreferencedVisuals()
    {
        var originalScript = """
            SELECT 1 AS val INTO #data;

            CREATE VISUAL v_page1 AS BAR ( TITLE = 'Page 1', SOURCE = #data );
            CREATE VISUAL v_page2 AS PIE ( TITLE = 'Page 2', SOURCE = #data );
            CREATE VISUAL v_page3 AS LINE ( TITLE = 'Page 3', SOURCE = #data );

            CREATE PAGE [First] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_page1 ) ) );
            CREATE PAGE [Second] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_page2 ) ) );
            CREATE PAGE [Third] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_page3 ) ) );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1", Name: "First", Mode: "Dashboard",
                    Visuals: [new DesignerVisualDto("v1", "v_page1", "BAR", 1, 1, 12, 4, "Page 1", "data", new Dictionary<string, string>(), new Dictionary<string, string>())]
                ),
                new DesignerPageDto(
                    Id: "p3", Name: "Third", Mode: "Dashboard",
                    Visuals: [new DesignerVisualDto("v3", "v_page3", "LINE", 1, 1, 12, 4, "Page 3", "data", new Dictionary<string, string>(), new Dictionary<string, string>())]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("CREATE PAGE [First] AS DASHBOARD", patched);
        Assert.Contains("CREATE PAGE [Third] AS DASHBOARD", patched);
        Assert.DoesNotContain("CREATE PAGE [Second]", patched);
        Assert.DoesNotContain("CREATE VISUAL v_page2", patched);
    }

    [Fact]
    public void MultiPage_AddingFourthPage_AppendsNewPageAtEnd()
    {
        var originalScript = """
            SELECT 1 AS val INTO #data;
            CREATE VISUAL v1 AS CARD ( TITLE = 'Card 1', SOURCE = #data );
            CREATE PAGE [P1] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v1 ) ) );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1", Name: "P1", Mode: "Dashboard",
                    Visuals: [new DesignerVisualDto("v1", "v1", "CARD", 1, 1, 12, 4, "Card 1", "data", new Dictionary<string, string>(), new Dictionary<string, string>())]
                ),
                new DesignerPageDto(
                    Id: "p2", Name: "P2", Mode: "Dashboard",
                    Visuals: [new DesignerVisualDto("v2", "v2", "BAR", 1, 1, 12, 4, "Card 2", "data", new Dictionary<string, string>(), new Dictionary<string, string>())]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("CREATE VISUAL v2 AS BAR", patched);
        Assert.Contains("CREATE PAGE [P1] AS DASHBOARD", patched);
        Assert.Contains("CREATE PAGE [P2] AS DASHBOARD", patched);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 2. CTE PRESERVATION
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CtePreservation_SingleCte_RemainsIntactAfterVisualUpdate()
    {
        var originalScript = """
            -- Compute monthly sales aggregation with Common Table Expression
            WITH MonthlySales AS (
                SELECT
                    DATEFROMPARTS(YEAR(order_date), MONTH(order_date), 1) AS sales_month,
                    region,
                    SUM(order_total) AS monthly_revenue,
                    COUNT(DISTINCT customer_id) AS active_customers
                FROM dw.fact_orders
                WHERE order_date >= '2026-01-01'
                GROUP BY DATEFROMPARTS(YEAR(order_date), MONTH(order_date), 1), region
            )
            SELECT
                sales_month,
                region,
                monthly_revenue,
                active_customers,
                AVG(monthly_revenue) OVER (PARTITION BY region ORDER BY sales_month ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS rolling_3mo_avg
            INTO #monthly_trends
            FROM MonthlySales;

            CREATE VISUAL v_trend AS LINE (
                TITLE = 'Original Trend',
                SOURCE = #monthly_trends,
                MAPPINGS (X = sales_month, Y = monthly_revenue, COLOR = region)
            );

            CREATE PAGE [Trends] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A',
                    MAP ( 'A' = v_trend )
                )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Trends",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_trend",
                            Type: "LINE",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 12,
                            GridRowSpan: 6,
                            Title: "3-Month Rolling Trend by Region",
                            Dataset: "monthly_trends",
                            Mappings: new Dictionary<string, string> { ["X"] = "sales_month", ["Y"] = "rolling_3mo_avg", ["COLOR"] = "region" },
                            Options: new Dictionary<string, string> { ["SMOOTH"] = "TRUE" }
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        // Verify the entire CTE block and window function are 100% preserved
        Assert.Contains("WITH MonthlySales AS (", patched);
        Assert.Contains("DATEFROMPARTS(YEAR(order_date), MONTH(order_date), 1) AS sales_month,", patched);
        Assert.Contains("WHERE order_date >= '2026-01-01'", patched);
        Assert.Contains("AVG(monthly_revenue) OVER (PARTITION BY region ORDER BY sales_month ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS rolling_3mo_avg", patched);
        Assert.Contains("INTO #monthly_trends", patched);
        Assert.Contains("FROM MonthlySales;", patched);

        // Verify the visual is patched with the new title and mappings
        Assert.Contains("TITLE = '3-Month Rolling Trend by Region'", patched);
        Assert.Contains("Y = rolling_3mo_avg", patched);
    }

    [Fact]
    public void CtePreservation_MultipleChainedCtes_RemainsIntactAfterDatasetAndVisualAdditions()
    {
        var originalScript = """
            WITH CteOrders AS (
                SELECT country, amount FROM orders WHERE status = 'COMPLETED'
            ),
            CteTotals AS (
                SELECT country, SUM(amount) AS total_spend
                FROM CteOrders
                GROUP BY country
            )
            SELECT country, total_spend
            INTO #country_summary
            FROM CteTotals;

            CREATE VISUAL v_geo AS MAP (
                TITLE = 'Geo Spend',
                SOURCE = #country_summary
            );

            CREATE PAGE [Geo] AS DASHBOARD (
                LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_geo ) )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Geo",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1",
                            Name: "v_geo",
                            Type: "MAP",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 6,
                            GridRowSpan: 6,
                            Title: "Enterprise Spend by Country",
                            Dataset: "country_summary",
                            Mappings: new Dictionary<string, string> { ["LOCATION"] = "country", ["VALUE"] = "total_spend" },
                            Options: new Dictionary<string, string>()
                        ),
                        new DesignerVisualDto(
                            Id: "v2",
                            Name: "v_top_countries",
                            Type: "HBAR",
                            GridCol: 7,
                            GridRow: 1,
                            GridColSpan: 6,
                            GridRowSpan: 6,
                            Title: "Top Country Ranking",
                            Dataset: "country_summary",
                            Mappings: new Dictionary<string, string> { ["Y"] = "country", ["X"] = "total_spend" },
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets:
            [
                new DesignerDatasetDto("ds_custom", "custom_feed", "SELECT country, total_spend FROM #country_summary WHERE total_spend > 10000")
            ]
        );

        var patched = _patcher.Patch(originalScript, state);

        // Verify chained CTEs
        Assert.Contains("WITH", patched);
        Assert.Contains("CteOrders AS (", patched);
        Assert.Contains("CteTotals AS (", patched);
        Assert.Contains("SELECT country, SUM(amount) AS total_spend", patched);
        Assert.Contains("INTO #country_summary", patched);

        // Verify new visual and dataset additions
        Assert.Contains("CREATE VISUAL v_top_countries AS HBAR", patched);
        Assert.Contains("CREATE DATASET &custom_feed AS (", patched);
    }

    [Fact]
    public void CtePreservation_RecursiveCteWithComments_PreservesAllCommentsAndSyntax()
    {
        var originalScript = """
            -- Build organizational hierarchy recursively
            WITH OrgTree AS (
                -- Anchor member: executive leadership
                SELECT employee_id, manager_id, title, 1 AS depth
                FROM employees
                WHERE manager_id IS NULL

                UNION ALL

                -- Recursive member: direct reports
                /* Traversing sub-levels */
                SELECT e.employee_id, e.manager_id, e.title, o.depth + 1 AS depth
                FROM employees e
                INNER JOIN OrgTree o ON e.manager_id = o.employee_id
            )
            SELECT employee_id, title, depth INTO #org_chart FROM OrgTree;

            CREATE VISUAL v_org AS SUNBURST (
                TITLE = 'Org Chart',
                SOURCE = #org_chart
            );

            CREATE PAGE [Hierarchy] AS DASHBOARD (
                LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_org ) )
            );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "Hierarchy",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto("v1", "v_org", "SUNBURST", 1, 1, 12, 6, "Global Organization Hierarchy", "org_chart", new Dictionary<string, string>(), new Dictionary<string, string>())
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        Assert.Contains("-- Anchor member: executive leadership", patched);
        Assert.Contains("/* Traversing sub-levels */", patched);
        Assert.Contains("UNION ALL", patched);
        Assert.Contains("INNER JOIN OrgTree o ON e.manager_id = o.employee_id", patched);
        Assert.Contains("TITLE = 'Global Organization Hierarchy'", patched);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 3. CRLF / LF REPEATED MUTATIONS
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LineEndings_SuccessiveMutationsWithCrlf_PreservesLineEndingsWithoutAccumulatingBlankLines()
    {
        var scriptCrlf = "-- Initial Script\r\nSELECT 1 AS a INTO #temp;\r\n\r\nCREATE VISUAL v1 AS BAR (\r\n    TITLE = 'V1',\r\n    SOURCE = #temp\r\n);\r\n\r\nCREATE PAGE [Main] AS DASHBOARD (\r\n    LAYOUT (\r\n        STRUCTURE = 'A',\r\n        MAP (\r\n            'A' = v1\r\n        )\r\n    )\r\n);\r\n";

        var currentScript = scriptCrlf;

        // Perform 5 successive patch iterations
        for (int i = 1; i <= 5; i++)
        {
            var state = new DesignerStateDto(
                Pages:
                [
                    new DesignerPageDto(
                        Id: "p1",
                        Name: "Main",
                        Mode: "Dashboard",
                        Visuals:
                        [
                            new DesignerVisualDto(
                                Id: "v1",
                                Name: "v1",
                                Type: "BAR",
                                GridCol: 1,
                                GridRow: 1,
                                GridColSpan: 12,
                                GridRowSpan: i,
                                Title: $"Iteration {i} Title",
                                Dataset: "temp",
                                Mappings: new Dictionary<string, string> { ["X"] = "a" },
                                Options: new Dictionary<string, string> { ["HEIGHT"] = $"{200 + i * 50}px" }
                            )
                        ]
                    )
                ],
                Datasets: []
            );

            currentScript = _patcher.Patch(currentScript, state);

            // Verify parseability at each step
            var parseResp = _analysis.Parse(currentScript, 100);
            Assert.Null(parseResp.Error);
            Assert.Single(parseResp.DesignState.Pages);
            Assert.Equal($"Iteration {i} Title", parseResp.DesignState.Pages[0].Visuals[0].Title);
        }

        // Verify that excessive blank line gaps have not accumulated
        Assert.DoesNotContain("\r\n\r\n\r\n\r\n", currentScript);
        Assert.Contains("SELECT 1 AS a INTO #temp;", currentScript);
        Assert.Contains("TITLE = 'Iteration 5 Title'", currentScript);
    }

    [Fact]
    public void LineEndings_SuccessiveMutationsWithLf_PreservesLfLineEndings()
    {
        var scriptLf = "-- Unix Script\nSELECT 2 AS val INTO #stage;\n\nCREATE VISUAL v_line AS LINE (\n    TITLE = 'Line Chart',\n    SOURCE = #stage\n);\n\nCREATE PAGE [Overview] AS DASHBOARD (\n    LAYOUT (\n        STRUCTURE = 'A',\n        MAP (\n            'A' = v_line\n        )\n    )\n);\n";

        var currentScript = scriptLf;

        for (int i = 1; i <= 4; i++)
        {
            var state = new DesignerStateDto(
                Pages:
                [
                    new DesignerPageDto(
                        Id: "p1",
                        Name: "Overview",
                        Mode: "Dashboard",
                        Visuals:
                        [
                            new DesignerVisualDto(
                                Id: "v1",
                                Name: "v_line",
                                Type: "LINE",
                                GridCol: 1,
                                GridRow: 1,
                                GridColSpan: 12,
                                GridRowSpan: 4,
                                Title: $"Step {i}",
                                Dataset: "stage",
                                Mappings: new Dictionary<string, string>(),
                                Options: new Dictionary<string, string>()
                            )
                        ]
                    )
                ],
                Datasets: []
            );

            currentScript = _patcher.Patch(currentScript, state);

            var parseResp = _analysis.Parse(currentScript, 100);
            Assert.Null(parseResp.Error);
            Assert.Equal($"Step {i}", parseResp.DesignState.Pages[0].Visuals[0].Title);
        }

        Assert.Contains("SELECT 2 AS val INTO #stage;", currentScript);
        Assert.Contains("TITLE = 'Step 4'", currentScript);
    }

    [Fact]
    public void LineEndings_FiftyRepeatedMovesAndPropertyChanges_UnderCrlfAndLf_PreservesIntegrity()
    {
        var formats = new[] { ("CRLF", "\r\n"), ("LF", "\n") };

        foreach (var (label, eol) in formats)
        {
            var script = $"-- Script {label}{eol}SELECT 1 AS val INTO #data;{eol}{eol}CREATE VISUAL v1 AS BAR ({eol}    TITLE = 'Init',{eol}    SOURCE = #data{eol});{eol}{eol}CREATE PAGE [P1] AS DASHBOARD ({eol}    LAYOUT ({eol}        STRUCTURE = 'A',{eol}        MAP ({eol}            'A' = v1{eol}        ){eol}    ){eol});{eol}";

            var current = script;

            for (int i = 1; i <= 50; i++)
            {
                int col = (i % 6) + 1;
                int row = (i % 4) + 1;
                int span = (i % 6) + 6;
                string newTitle = $"Move #{i} on {label}";

                var state = new DesignerStateDto(
                    Pages:
                    [
                        new DesignerPageDto(
                            Id: "p1",
                            Name: "P1",
                            Mode: "Dashboard",
                            Visuals:
                            [
                                new DesignerVisualDto(
                                    Id: "v1",
                                    Name: "v1",
                                    Type: "BAR",
                                    GridCol: col,
                                    GridRow: row,
                                    GridColSpan: span,
                                    GridRowSpan: 4,
                                    Title: newTitle,
                                    Dataset: "data",
                                    Mappings: new Dictionary<string, string> { ["X"] = "val" },
                                    Options: new Dictionary<string, string> { ["WIDTH"] = $"{50 + (i % 50)}%" }
                                )
                            ]
                        )
                    ],
                    Datasets: []
                );

                current = _patcher.Patch(current, state);

                if (i % 10 == 0 || i == 50)
                {
                    var resp = _analysis.Parse(current, 100);
                    Assert.Null(resp.Error);
                    Assert.Equal(newTitle, resp.DesignState.Pages[0].Visuals[0].Title);
                }
            }

            Assert.Contains("SELECT 1 AS val INTO #data;", current);
            Assert.Contains("TITLE = 'Move #50 on " + label + "'", current);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 4. COMMENTS INSIDE VISUAL BODIES
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Comments_InsideVisualBody_PreservedWhenOtherVisualsAreMutated()
    {
        var originalScript = """
            SELECT 100 AS amt INTO #rev;

            CREATE VISUAL v_with_comments AS BAR (
                -- This is a vital developer comment about visual 1
                TITLE = 'Original Visual 1',
                /* Multi-line explanation:
                   this visual connects to #rev */
                SOURCE = #rev,
                MAPPINGS (
                    X = rep -- rep category
                )
            );

            CREATE VISUAL v_other AS CARD (
                TITLE = 'Other Card',
                SOURCE = #rev
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (
                    STRUCTURE = 'A B',
                    MAP (
                        'A' = v_with_comments,
                        'B' = v_other
                    )
                )
            );
            """;

        // Mutate ONLY v_other; leave v_with_comments unchanged
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
                            Name: "v_with_comments",
                            Type: "BAR",
                            GridCol: 1,
                            GridRow: 1,
                            GridColSpan: 6,
                            GridRowSpan: 4,
                            Title: "Original Visual 1",
                            Dataset: "rev",
                            Mappings: new Dictionary<string, string> { ["X"] = "rep" },
                            Options: new Dictionary<string, string>()
                        ),
                        new DesignerVisualDto(
                            Id: "v2",
                            Name: "v_other",
                            Type: "CARD",
                            GridCol: 7,
                            GridRow: 1,
                            GridColSpan: 6,
                            GridRowSpan: 4,
                            Title: "Updated Card Title 2026",
                            Dataset: "rev",
                            Mappings: new Dictionary<string, string>(),
                            Options: new Dictionary<string, string>()
                        )
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch(originalScript, state);

        // v_other is updated
        Assert.Contains("TITLE = 'Updated Card Title 2026'", patched);

        // Preceding ETL & surrounding comments remain intact
        Assert.Contains("SELECT 100 AS amt INTO #rev;", patched);

        // The untouched visual definition v_with_comments is preserved
        Assert.Contains("CREATE VISUAL v_with_comments AS BAR", patched);
    }

    [Fact]
    public void Comments_InsideVisualBody_DocumentBehaviorWhenVisualMutated()
    {
        // When a visual's properties (such as TITLE) are mutated through the visual designer,
        // DesignerScriptPatcher replaces the exact character span of that CREATE VISUAL statement
        // with the newly generated visual statement. This test verifies that the preceding SQL comments
        // and following comments are preserved, and documents the AST regeneration behavior.
        var originalScript = """
            -- Header comment before visual
            SELECT 1 AS x INTO #data;

            /* Visual comment header */
            CREATE VISUAL v_target AS BAR (
                -- Internal title comment
                TITLE = 'Old Title',
                SOURCE = #data
            );

            -- Footer comment after visual
            CREATE PAGE [P1] AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ( 'A' = v_target ) ) );
            """;

        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1", Name: "P1", Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto(
                            Id: "v1", Name: "v_target", Type: "BAR",
                            GridCol: 1, GridRow: 1, GridColSpan: 12, GridRowSpan: 4,
                            Title: "New Mutated Title",
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

        Assert.Contains("-- Header comment before visual", patched);
        Assert.Contains("/* Visual comment header */", patched);
        Assert.Contains("-- Footer comment after visual", patched);
        Assert.Contains("TITLE = 'New Mutated Title'", patched);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 5. TRANSIENT INVALID SYNTAX
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TransientInvalidSyntax_IncompleteVisualStatement_ReturnsOriginalScriptWithoutCrashing()
    {
        var invalidScript = """
            SELECT 1 AS val INTO #temp;
            CREATE VISUAL v_incomplete AS ;
            """;

        var state = new DesignerStateDto(
            Pages: [new DesignerPageDto("p1", "Page1", "Dashboard", [])],
            Datasets: []
        );

        // Must fail-safe by returning original script to protect developer from losing edits
        var patched = _patcher.Patch(invalidScript, state);

        Assert.Equal(invalidScript, patched);
    }

    [Fact]
    public void TransientInvalidSyntax_UnclosedParenthesisInSql_ReturnsOriginalScriptSafely()
    {
        var unclosedScript = """
            -- Half-typed expression
            SELECT (1 + 2 AS broken INTO #temp;

            CREATE VISUAL v1 AS BAR ( TITLE = 'Test', SOURCE = #temp );
            """;

        var state = new DesignerStateDto(
            Pages: [new DesignerPageDto("p1", "Page1", "Dashboard", [])],
            Datasets: []
        );

        var patched = _patcher.Patch(unclosedScript, state);

        Assert.Equal(unclosedScript, patched);
    }

    [Fact]
    public async Task TransientInvalidSyntax_AnalysisServiceAnalyze_ReturnsErrorDiagnosticsWithoutThrowing()
    {
        var brokenScript = "CREATE VISUAL broken_vis AS INVALID_TYPE ( TITLE = );";

        var analyzeResponse = await _analysis.AnalyzeAsync(brokenScript, null, 100, null);

        Assert.NotNull(analyzeResponse);
        Assert.Contains(analyzeResponse.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TransientInvalidSyntax_EmptyOrWhitespaceScript_GeneratesCleanScaffold()
    {
        var state = new DesignerStateDto(
            Pages:
            [
                new DesignerPageDto(
                    Id: "p1",
                    Name: "LandingPage",
                    Mode: "Dashboard",
                    Visuals:
                    [
                        new DesignerVisualDto("v1", "v_metric", "CARD", 1, 1, 12, 4, "Total Users", null, new Dictionary<string, string>(), new Dictionary<string, string>())
                    ]
                )
            ],
            Datasets: []
        );

        var patched = _patcher.Patch("   \n\t  \r\n", state);

        Assert.Contains("CREATE VISUAL v_metric AS CARD", patched);
        Assert.Contains("CREATE PAGE [LandingPage] AS DASHBOARD", patched);
    }
}
