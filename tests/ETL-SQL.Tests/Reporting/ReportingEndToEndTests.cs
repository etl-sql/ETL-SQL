using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportHosting;
using ETL_SQL.Core;
using ETL_SQL.Data;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    public class ReportingEndToEndTests
    {
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task TestComplexReport_BuildsCompleteManifest()
        {
            // 1. Setup multi-dataset report script
            string scriptPath = Path.Combine(Path.GetTempPath(), $"complex_report_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
DECLARE @Threshold INT = 50;

-- Dataset A: Sales
SELECT 'Region1' AS Region, 100 AS Amount INTO #Sales;
INSERT INTO #Sales (Region, Amount) VALUES ('Region2', 20);

-- Dataset B: Inventory
SELECT 'Item1' AS Item, 200 AS Stock INTO #Inventory;

CREATE VISUAL HighSales AS BAR (
    TITLE 'High Sales',
    SOURCE = (SELECT * FROM #Sales WHERE Amount > @Threshold),
    MAPPINGS (X = Region, Y = Amount)
);

CREATE VISUAL LowSales AS TABLE (
    TITLE 'Low Sales',
    SOURCE = (SELECT * FROM #Sales WHERE Amount <= @Threshold)
);

CREATE VISUAL InventoryStatus AS CARD (
    TITLE 'Total Stock',
    SOURCE = (SELECT SUM(Stock) AS StockVal FROM #Inventory),
    MAPPINGS (VALUE = StockVal)
);

CREATE PAGE Dashboard AS (
    STRUCTURE = 'A B / C .',
    MAP('A' = HighSales, 'B' = LowSales, 'C' = InventoryStatus)
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                // 2. Verify Manifest Structure
                Assert.Equal(3, manifest.Visuals.Count);
                
                var highSales = manifest.Visuals.First(v => v.Name == "HighSales");
                Assert.Single(highSales.Rows); // Region1 only
                Assert.Equal("Region1", highSales.Rows[0][0]);

                var lowSales = manifest.Visuals.First(v => v.Name == "LowSales");
                Assert.Single(lowSales.Rows); // Region2 only
                Assert.Equal("Region2", lowSales.Rows[0][0]);

                var invCard = manifest.Visuals.First(v => v.Name == "InventoryStatus");
                Assert.Equal("200", invCard.Rows[0][0]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task GoldenWorkflowSample_BuildsInteractableExportableManifest()
        {
            var scriptPath = GetGoldenWorkflowPath();
            await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());

            var manifest = await service.GetManifestAsync();

            Assert.Null(manifest.Error); // guard: if evaluation failed, Error contains the reason
            Assert.Equal("Golden Sales Operations Workflow", manifest.Title);
            Assert.Equal("light", manifest.Theme);
            Assert.Equal(10, manifest.Visuals.Count);
            Assert.Equal(3, manifest.Pages.Count);
            Assert.NotNull(manifest.Containers);
            Assert.Contains(manifest.Containers!, c => c.Name == "WorkflowControls");
            Assert.NotNull(manifest.Buttons);
            Assert.Contains(manifest.Buttons!, b => b.Name == "ApplyWorkflow" && b.Actions.Any(a => a.Type == "APPLY_PARAMETERS"));
            Assert.NotNull(manifest.Navigations);
            Assert.Contains(manifest.Navigations!, n =>
                n.Name == "GoldenNav"
                && n.DefaultPage == "Overview"
                && n.Pages.SequenceEqual(new[] { "Overview", "Quality", "Export" }));
            Assert.Equal(new[] { "@Region", "@MinMargin", "@ShowIssues", "@ExportPath" }.OrderBy(x => x), manifest.ParameterMetadata.Keys.OrderBy(x => x));

            var orderDetail = manifest.Visuals.Single(v => v.Name == "OrderDetail");
            Assert.Equal("TABLE", orderDetail.VisualType);
            Assert.True(orderDetail.Rows.Count > 50);
            Assert.Contains("Margin", orderDetail.Columns);
            Assert.NotNull(orderDetail.RowStyles);

            var issueTable = manifest.Visuals.Single(v => v.Name == "QualityIssues");
            Assert.True(issueTable.Rows.Count > 0);

            var westOnly = await service.SetParametersAsync(new[]
            {
                ("Region", "EMEA"),
                ("MinMargin", "500")
            });

            var filteredDetail = westOnly.Visuals.Single(v => v.Name == "OrderDetail");
            Assert.True(filteredDetail.Rows.Count > 0);
            Assert.All(filteredDetail.Rows, row => Assert.Equal("EMEA", row[2]));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task TestDashboardService_SelectiveRefresh_UpdatesCorrectVisuals()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"selective_refresh_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
DECLARE @RegionFilter STRING INPUT = 'All';

SELECT 'North' AS Region, 100 AS Sales INTO #Data;
INSERT INTO #Data (Region, Sales) VALUES ('South', 200);

CREATE VISUAL RegionSales AS TABLE (
    SOURCE = (SELECT * FROM #Data WHERE @RegionFilter = 'All' OR Region = @RegionFilter)
);

CREATE VISUAL StaticTotal AS CARD (
    SOURCE = (SELECT SUM(Sales) AS TotalVal FROM #Data),
    MAPPINGS (VALUE = TotalVal)
);

CREATE PAGE Main AS (
    STRUCTURE = 'A B',
    MAP('A' = RegionSales, 'B' = StaticTotal)
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                
                // Initial build (All regions)
                var manifest1 = await service.GetManifestAsync();
                Assert.Equal(2, manifest1.Visuals.First(v => v.Name == "RegionSales").Rows.Count);

                // Update parameter to 'North'
                // This should refresh RegionSales but technically StaticTotal persists
                var manifest2 = await service.SetParameterAsync("RegionFilter", "North");
                
                var regionSales = manifest2.Visuals.First(v => v.Name == "RegionSales");
                Assert.Single(regionSales.Rows);
                Assert.Equal("North", regionSales.Rows[0][0]);

                var totalSales = manifest2.Visuals.First(v => v.Name == "StaticTotal");
                Assert.Equal("300", totalSales.Rows[0][0]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Security")]
        public async Task DashboardService_RunScriptAsync_RejectsSiblingDirectoryBypass()
        {
            var root = Path.Combine(Path.GetTempPath(), $"run_script_root_{Guid.NewGuid():N}");
            var scriptRoot = Path.Combine(root, "reports");
            var siblingRoot = Path.Combine(root, "reports2");
            Directory.CreateDirectory(scriptRoot);
            Directory.CreateDirectory(siblingRoot);

            var reportPath = Path.Combine(scriptRoot, "main.rptsql");
            var siblingScript = Path.Combine(siblingRoot, "outside.etlsql");
            await File.WriteAllTextAsync(reportPath, "CREATE VISUAL V AS CARD (SOURCE = (SELECT 1 AS Value));");
            await File.WriteAllTextAsync(siblingScript, "PRINT 'outside';");

            try
            {
                await using var service = new DashboardService(reportPath, DashboardTestHelper.CreateMockScopeFactory());

                var result = await service.RunScriptAsync(siblingScript, new());

                Assert.False(result.Refresh);
                Assert.Contains("outside the report directory", result.Message);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Security")]
        public void DashboardServiceFactory_RejectsSiblingManifestPathBypass()
        {
            var root = Path.Combine(Path.GetTempPath(), $"manifest_root_{Guid.NewGuid():N}");
            var manifestRoot = Path.Combine(root, "reports");
            var siblingRoot = Path.Combine(root, "reports2");
            Directory.CreateDirectory(manifestRoot);
            Directory.CreateDirectory(siblingRoot);

            var insideReport = Path.Combine(manifestRoot, "inside.rptsql");
            var outsideReport = Path.Combine(siblingRoot, "outside.rptsql");
            var manifestPath = Path.Combine(manifestRoot, "reports.json");
            File.WriteAllText(insideReport, "CREATE VISUAL V AS CARD (SOURCE = (SELECT 1 AS Value));");
            File.WriteAllText(outsideReport, "CREATE VISUAL V AS CARD (SOURCE = (SELECT 2 AS Value));");
            File.WriteAllText(manifestPath, """
{
  "reports": [
    { "name": "Inside", "path": "inside.rptsql" },
    { "name": "Outside", "path": "../reports2/outside.rptsql" }
  ]
}
""");

            try
            {
                var factory = new DashboardServiceFactory(manifestPath, DashboardTestHelper.CreateMockScopeFactory());

                Assert.Single(factory.Reports);
                Assert.Equal("Inside", factory.Reports[0].Name);
                Assert.NotNull(factory.GetService("Inside"));
                Assert.Null(factory.GetService("Outside"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task ManifestBuilder_ResolvesNamedStylesAndServerRowStyles()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"style_row_manifest_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'North' AS Region, 100 AS Amount INTO #Sales;
INSERT INTO #Sales (Region, Amount) VALUES ('South', -5);

CREATE STYLE DarkPanel (
    THEME = dark,
    COLOR = '#f8fafc',
    HEIGHT = '240px'
);

CREATE VISUAL SalesTable AS TABLE (
    SOURCE = (SELECT Region, Amount FROM #Sales),
    STYLE = DarkPanel,
    STYLE (COLOR = '#ffffff'),
    FORMATTING (
        Amount < 0 THEN '#ef4444',
        Amount >= 100 THEN '#22c55e'
    )
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());

                var manifest = await service.GetManifestAsync();
                var table = manifest.Visuals.Single(v => v.Name == "SalesTable");

                Assert.Equal("dark", table.Styles!["THEME"]);
                Assert.Equal("240px", table.Styles["HEIGHT"]);
                Assert.Equal("#ffffff", table.Styles["COLOR"]);
                Assert.Equal(new[] { "#22c55e", "#ef4444" }, table.RowStyles);
                Assert.Null(table.FormattingRules);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task ManifestBuilder_EmitsResolvedReportPageContainerAndButtonStyles()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"style_cascade_manifest_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SET REPORT THEME = 'light';

SELECT 'North' AS Region, 100 AS Amount INTO #Sales;

CREATE STYLE PageDark (
    THEME = dark,
    BACKGROUND = '#111827'
);

CREATE VISUAL SalesCard AS CARD (
    SOURCE = (SELECT SUM(Amount) AS Amount FROM #Sales),
    MAPPINGS (VALUE = Amount)
);

CREATE BUTTON RefreshButton AS RUN (
    TITLE 'Run',
    STYLE (COLOR = '#ffffff')
);

CREATE CONTAINER Shell AS BOX (
    STRUCTURE = 'A B',
    MAP('A' = SalesCard, 'B' = RefreshButton),
    STYLE (THEME = 'corporate', HEIGHT = '360px')
);

CREATE PAGE Dashboard AS (
    STRUCTURE = 'A',
    MAP('A' = Shell),
    STYLE = PageDark,
    STYLE (BACKGROUND = '#0f172a')
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());

                var manifest = await service.GetManifestAsync();
                var page = manifest.Pages.Single(p => p.Name == "Dashboard");
                var container = manifest.Containers!.Single(c => c.Name == "Shell");
                var button = manifest.Buttons!.Single(b => b.Name == "RefreshButton");
                var visual = manifest.Visuals.Single(v => v.Name == "SalesCard");

                Assert.Equal("light", manifest.Theme);
                Assert.Equal("light", manifest.Styles!["THEME"]);
                Assert.Equal("dark", page.Styles!["THEME"]);
                Assert.Equal("#0f172a", page.Styles["BACKGROUND"]);
                Assert.Equal("corporate", container.Styles!["THEME"]);
                Assert.Equal("360px", container.Styles["HEIGHT"]);
                Assert.Equal("light", button.Styles!["THEME"]);
                Assert.Equal("#ffffff", button.Styles["COLOR"]);
                Assert.Empty(visual.Styles!);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task ManifestBuilder_EmitsResolvedActionValueMetadata()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"action_value_manifest_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
DECLARE @Region STRING INPUT = '';
DECLARE @Search STRING INPUT = '';

SELECT 'North' AS Region, 100 AS Amount INTO #Sales;
INSERT INTO #Sales (Region, Amount) VALUES ('South', 200);

CREATE VISUAL RegionPicker AS SLICER (
    SOURCE = (SELECT Region FROM #Sales),
    MAPPINGS (VALUE = Region),
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, VALUE))
);

CREATE VISUAL SalesTable AS TABLE (
    SOURCE = (SELECT Region, Amount FROM #Sales),
    ACTIONS (
        ON_CLICK = SET_PARAMETER(@Region, Region),
        ON_CLICK = RUN_SCRIPT('details.etlsql', @Region = Region, @Mode = DetailMode)
    )
);

CREATE BUTTON SearchButton AS RUN (
    TITLE 'Search',
    ACTIONS (ON_CLICK = SET_PARAMETER(@Search, SearchLiteral))
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());

                var manifest = await service.GetManifestAsync();
                var pickerAction = manifest.Visuals
                    .Single(v => v.Name == "RegionPicker")
                    .Actions.Single(a => a.Type == "SET_PARAMETER");
                var tableActions = manifest.Visuals
                    .Single(v => v.Name == "SalesTable")
                    .Actions;
                var tableSetParameter = tableActions.Single(a => a.Type == "SET_PARAMETER");
                var runScript = tableActions.Single(a => a.Type == "RUN_SCRIPT");
                var buttonAction = manifest.Buttons!
                    .Single(b => b.Name == "SearchButton")
                    .Actions.Single(a => a.Type == "SET_PARAMETER");

                Assert.Equal("CONTROL_VALUE", pickerAction.ValueSource);
                Assert.Equal("COLUMN", tableSetParameter.ValueSource);
                Assert.Equal("Region", tableSetParameter.ValueColumn);
                Assert.Equal("Region", runScript.ParameterColumns!["@Region"]);
                Assert.Equal("DetailMode", runScript.LiteralParameters!["@Mode"]);
                Assert.Equal("LITERAL", buttonAction.ValueSource);
                Assert.Equal("SearchLiteral", buttonAction.LiteralValue);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Theory]
        [Trait("Category", "Smoke.Reporting")]
        [MemberData(nameof(RepresentativeReportScripts))]
        public async Task ManifestBuilder_RepresentativeReports_EmitExpectedManifestShape(string scenario, string script, Action<ETL_SQL.Reporting.ReportManifest> assertManifest)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"manifest_snapshot_{scenario}_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, script);

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());

                var manifest = await service.GetManifestAsync();
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });

                Assert.Contains("\"visuals\"", json);
                Assert.Contains("\"parameters\"", json);
                assertManifest(manifest);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        public static IEnumerable<object[]> RepresentativeReportScripts()
        {
            yield return new object[]
            {
                "basic_chart",
                @"
SELECT 'Jan' AS Month, 10 AS Sales INTO #Sales;
INSERT INTO #Sales (Month, Sales) VALUES ('Feb', 20);
CREATE VISUAL SalesChart AS BAR (
    SOURCE = (SELECT Month, Sales FROM #Sales),
    MAPPINGS (X = Month, Y = Sales)
);",
                new Action<ETL_SQL.Reporting.ReportManifest>(m =>
                {
                    var visual = m.Visuals.Single(v => v.Name == "SalesChart");
                    Assert.Equal("BAR", visual.VisualType);
                    Assert.NotNull(visual.ChartConfig);
                })
            };

            yield return new object[]
            {
                "formatted_table",
                @"
SELECT 'North' AS Region, 10 AS Sales INTO #Sales;
INSERT INTO #Sales (Region, Sales) VALUES ('South', -1);
CREATE VISUAL SalesTable AS TABLE (
    SOURCE = (SELECT Region, Sales FROM #Sales),
    FORMATTING (Sales < 0 THEN '#ef4444')
);",
                new Action<ETL_SQL.Reporting.ReportManifest>(m =>
                {
                    var visual = m.Visuals.Single(v => v.Name == "SalesTable");
                    Assert.Equal("TABLE", visual.VisualType);
                    Assert.Equal(new[] { null, "#ef4444" }, visual.RowStyles);
                    Assert.Null(visual.FormattingRules);
                })
            };

            yield return new object[]
            {
                "slicer",
                @"
DECLARE @Region STRING INPUT = '';
SELECT 'North' AS Region, 10 AS Sales INTO #Sales;
INSERT INTO #Sales (Region, Sales) VALUES ('South', 20);
CREATE VISUAL RegionFilter AS SLICER (
    SOURCE = (SELECT Region FROM #Sales),
    MAPPINGS (VALUE = Region),
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, VALUE))
);",
                new Action<ETL_SQL.Reporting.ReportManifest>(m =>
                {
                    var visual = m.Visuals.Single(v => v.Name == "RegionFilter");
                    var action = visual.Actions.Single();
                    Assert.Equal("SLICER", visual.VisualType);
                    Assert.Equal("CONTROL_VALUE", action.ValueSource);
                })
            };

            yield return new object[]
            {
                "scalar_inputs_deferred_run",
                @"
DECLARE @Search STRING INPUT = '';
DECLARE @Limit INT INPUT = 10;
DECLARE @Active BOOL INPUT = TRUE;
CREATE VISUAL SearchBox AS TEXTBOX (
    DEFAULT = '',
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Search, VALUE))
);
CREATE VISUAL LimitBox AS NUMBERBOX (
    DEFAULT = '10',
    MIN = 1,
    MAX = 100,
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Limit, VALUE))
);
CREATE VISUAL ActiveOnly AS CHECKBOX (
    DEFAULT = 'TRUE',
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Active, VALUE))
);
CREATE BUTTON RunReport AS RUN (
    TITLE 'Run'
);",
                new Action<ETL_SQL.Reporting.ReportManifest>(m =>
                {
                    Assert.Contains(m.Visuals, v => v.VisualType == "TEXTBOX");
                    Assert.Contains(m.Visuals, v => v.VisualType == "NUMBERBOX");
                    Assert.Contains(m.Visuals, v => v.VisualType == "CHECKBOX");
                    Assert.Contains(m.Buttons!, b => b.Actions.Any(a => a.Type == "APPLY_PARAMETERS"));
                    Assert.Equal(3, m.ParameterMetadata.Count);
                })
            };

            yield return new object[]
            {
                "multi_page",
                @"
SELECT 'North' AS Region, 10 AS Sales INTO #Sales;
CREATE VISUAL SalesCard AS CARD (
    SOURCE = (SELECT SUM(Sales) AS Total FROM #Sales),
    MAPPINGS (VALUE = Total)
);
CREATE VISUAL SalesTable AS TABLE (
    SOURCE = (SELECT Region, Sales FROM #Sales)
);
CREATE PAGE Overview AS (
    STRUCTURE = 'A',
    MAP('A' = SalesCard)
);
CREATE PAGE Details AS (
    STRUCTURE = 'A',
    MAP('A' = SalesTable)
);",
                new Action<ETL_SQL.Reporting.ReportManifest>(m =>
                {
                    Assert.Equal(2, m.Pages.Count);
                    Assert.Equal(2, m.Visuals.Count);
                })
            };

            yield return new object[]
            {
                "kitchen_sink",
                @"
SET REPORT THEME = 'dark';
DECLARE @Region STRING INPUT = '';
SELECT 'North' AS Region, 'Hardware' AS Category, 10 AS Sales INTO #Sales;
INSERT INTO #Sales (Region, Category, Sales) VALUES ('South', 'Software', 20);
CREATE STYLE Panel (THEME = dark, HEIGHT = '260px');
CREATE VISUAL RegionFilter AS SLICER (
    SOURCE = (SELECT Region FROM #Sales),
    MAPPINGS (VALUE = Region),
    ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, VALUE))
);
CREATE VISUAL BarByRegion AS BAR (
    SOURCE = (SELECT Region, Sales FROM #Sales),
    MAPPINGS (X = Region, Y = Sales),
    STYLE = Panel
);
CREATE VISUAL SalesTable AS TABLE (
    SOURCE = (SELECT Region, Category, Sales FROM #Sales),
    FORMATTING (Sales >= 20 THEN '#22c55e')
);
CREATE BUTTON Apply AS RUN (TITLE 'Apply');
CREATE CONTAINER Filters AS BOX (
    STRUCTURE = 'A B',
    MAP('A' = RegionFilter, 'B' = Apply)
);
CREATE PAGE Dashboard AS (
    STRUCTURE = 'A / B C',
    MAP('A' = Filters, 'B' = BarByRegion, 'C' = SalesTable)
);",
                new Action<ETL_SQL.Reporting.ReportManifest>(m =>
                {
                    Assert.Equal("dark", m.Styles!["THEME"]);
                    Assert.NotNull(m.Containers);
                    Assert.Contains(m.Visuals, v => v.ChartConfig != null);
                    Assert.Contains(m.Visuals, v => v.RowStyles != null);
                })
            };
        }

        private static string GetGoldenWorkflowPath()
        {
            var candidates = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            };

            foreach (var start in candidates)
            {
                var dir = new DirectoryInfo(start);
                while (dir is not null)
                {
                    var candidate = Path.Combine(dir.FullName, "samples", "golden_workflow", "golden_workflow.rptsql");
                    if (File.Exists(candidate))
                        return candidate;

                    dir = dir.Parent;
                }
            }

            throw new FileNotFoundException("Could not locate samples/golden_workflow/golden_workflow.rptsql.");
        }
    }
}
