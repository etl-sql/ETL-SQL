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

        [Fact]
        public async Task Card_SparklineColorAndReferenceLine_GeneratesExpectedLayers()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"card_sparkline_ref_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'Revenue' AS Label, 150 AS Value INTO #Summary;
SELECT 'Mon' AS Day, 10 AS Amount INTO #Daily;
INSERT INTO #Daily (Day, Amount) VALUES ('Tue', 15), ('Wed', 20);
CREATE VISUAL RevCard AS CARD (
  SOURCE = #Summary,
  MAPPINGS (
    LABEL = Label,
    VALUE = Value,
    SPARKLINE = #Daily (X = Day, Y = Amount, TYPE = LINE, COLOR = '#10B981', REFERENCE_LINE = 12.5)
  )
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = RevCard));
");
            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();
                var card = manifest.Visuals.Single(v => v.Name == "RevCard");

                var cardMicro = Assert.Single(card.MicroCharts!);
                Assert.Equal("card.sparkline", cardMicro.Role);
                Assert.Equal(2, cardMicro.PlotPlan.Layers.Length);
                Assert.Equal(MarkKind.Line, cardMicro.PlotPlan.Layers[0].Mark);
                Assert.Equal(MarkKind.Rule, cardMicro.PlotPlan.Layers[1].Mark);

                var ruleLayer = cardMicro.PlotPlan.Layers[1];
                var overlayToken = ruleLayer.Style.FirstOrDefault(t => t.Name == "overlayType");
                Assert.NotNull(overlayToken);
                Assert.Equal("ReferenceLine", overlayToken.Value);

                var paramToken = ruleLayer.Style.FirstOrDefault(t => t.Name == "parameter");
                Assert.NotNull(paramToken);
                Assert.Equal("12.5", paramToken.Value);

                Assert.Contains("class='plot-reference-line'", cardMicro.Svg);
                Assert.Contains("ref 12.5", cardMicro.PlainText);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        public async Task Card_ConditionalValueColor_And_ValueColorOption()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"card_value_color_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'Profit' AS Metric, 250 AS Amount INTO #CardData1;
SELECT 'Loss' AS Metric, -50 AS Amount INTO #CardData2;
SELECT 'Fixed' AS Metric, 80 AS Amount INTO #CardData3;

CREATE VISUAL ProfitCard AS CARD (
  SOURCE = #CardData1,
  MAPPINGS (VALUE = Amount, LABEL = Metric),
  FORMATTING (WHEN VALUE >= 100 THEN '#10B981')
);

CREATE VISUAL LossCard AS CARD (
  SOURCE = #CardData2,
  MAPPINGS (VALUE = Amount, LABEL = Metric),
  FORMATTING (WHEN VALUE < 0 THEN '#EF4444')
);

CREATE VISUAL FixedCard AS CARD (
  SOURCE = #CardData3,
  MAPPINGS (VALUE = Amount, LABEL = Metric),
  OPTIONS (VALUE_COLOR = '#3B82F6')
);

CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A B C', MAP('A' = ProfitCard, 'B' = LossCard, 'C' = FixedCard));
");
            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                var profitCard = manifest.Visuals.Single(v => v.Name == "ProfitCard");
                var lossCard = manifest.Visuals.Single(v => v.Name == "LossCard");
                var fixedCard = manifest.Visuals.Single(v => v.Name == "FixedCard");

                Assert.NotNull(profitCard.RowStyles);
                Assert.Equal("#10B981", profitCard.RowStyles[0]);

                Assert.NotNull(lossCard.RowStyles);
                Assert.Equal("#EF4444", lossCard.RowStyles[0]);

                Assert.Equal("#3B82F6", fixedCard.Options["value_color"]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }

        [Fact]
        public async Task Card_DynamicDeltaLabel_MappingPopulatesOption()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"card_delta_label_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 1000 AS Revenue, 850 AS PriorRevenue, 'vs 2025-Q3' AS Period INTO #Kpi;
CREATE VISUAL RevDeltaCard AS CARD (
  SOURCE = #Kpi,
  MAPPINGS (
    VALUE = Revenue,
    DELTA = PriorRevenue,
    DELTA_LABEL = Period
  )
);
CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP('A' = RevDeltaCard));
");
            try
            {
                var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                var card = manifest.Visuals.Single(v => v.Name == "RevDeltaCard");
                Assert.Equal("Period", card.Options["mapping:delta_label"]);
                Assert.Equal(3, card.Columns.Count);
                Assert.Equal("vs 2025-Q3", card.Rows[0][2]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
