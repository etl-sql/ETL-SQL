using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    public class VisualMappingTests
    {
        [Fact]
        public async Task TestVisual_NonExistentColumnMapping_DoesNotCrash()
        {
            // Verifies that mapping a visual property to a column that isn't in the source SELECT
            // doesn't crash the manifest builder, although it might lead to empty chart data.
            string scriptPath = Path.Combine(Path.GetTempPath(), $"mapping_error_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'A' AS RealColumn, 10 AS Val INTO #Data;
CREATE VISUAL BadMapping AS BAR (
    SOURCE = #Data,
    TITLE 'Broken',
    MAPPINGS (X = FakeColumn, Y = Val)
);
CREATE PAGE P AS DASHBOARD (
    STRUCTURE = 'A',
    MAP('A' = BadMapping)
);
");

            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                var visual = manifest.Visuals.First(v => v.Name == "BadMapping");
                Assert.NotNull(visual);
                // The manifest should still be generated. 
                // Depending on implementation, visual.ChartConfig might be null or simplified.
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        public async Task TestVisual_NullData_HandlesGracefully()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"null_data_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'A' AS Category, 10 AS Val INTO #Data;
INSERT INTO #Data (Category, Val) VALUES ('B', NULL);
CREATE VISUAL NullValues AS BAR (
    SOURCE = #Data,
    MAPPINGS (X = Category, Y = Val)
);
CREATE PAGE P AS DASHBOARD (
    STRUCTURE = 'A',
    MAP('A' = NullValues)
);
");

            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                var visual = manifest.Visuals.First(v => v.Name == "NullValues");
                Assert.Equal(2, visual.Rows.Count);
                Assert.Null(visual.Rows[1][1]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        public async Task NativeMicroCharts_BuildOneSemanticPlanPerCardAndTableCell()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"native_micro_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'Revenue' AS Label, 42 AS Value INTO #Summary;
SELECT 'Mon' AS Day, 10 AS Amount INTO #Daily;
INSERT INTO #Daily (Day, Amount) VALUES ('Tue', 15), ('Wed', 12);
SELECT 'Central' AS Region, 10 AS Jan, 14 AS Feb, 18 AS Mar, 0.75 AS Attainment INTO #Goals;
CREATE VISUAL Kpi AS CARD (
  SOURCE = #Summary,
  MAPPINGS (LABEL = Label, VALUE = Value, SPARKLINE = #Daily (X = Day, Y = Amount, TYPE = AREA))
);
CREATE VISUAL GoalTable AS TABLE (
  SOURCE = #Goals,
  MAPPINGS (
    Region,
    SPARKLINE(Jan, Feb, Mar) LINE AS 'Trend',
    Attainment PROGRESS_BAR (MIN = 0, MAX = 1, COLOR = '#16A34A') AS 'Progress'
  )
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A B', MAP('A' = Kpi, 'B' = GoalTable));
");
            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();
                var card = manifest.Visuals.Single(visual => visual.Name == "Kpi");
                var table = manifest.Visuals.Single(visual => visual.Name == "GoalTable");

                var cardMicro = Assert.Single(card.MicroCharts!);
                Assert.Equal("card.sparkline", cardMicro.Role);
                Assert.Equal(MarkKind.Area, Assert.Single(cardMicro.PlotPlan.Layers).Mark);
                Assert.Equal(3, cardMicro.PlotPlan.Layers[0].Data.Length);

                Assert.Equal(2, table.MicroCharts!.Count);
                Assert.Contains(table.MicroCharts, micro => micro.Kind == "sparkline" && micro.ColumnIndex == 1);
                Assert.Contains(table.MicroCharts, micro => micro.Kind == "progress" && micro.ColumnIndex == 2);
                Assert.All(table.MicroCharts, micro => Assert.Contains("role='img'", micro.Svg));
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
