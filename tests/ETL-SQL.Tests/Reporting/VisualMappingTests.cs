using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportPlayer;
using ETL_SQL.Core;
using ETL_SQL.Data;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;

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
CREATE PAGE P AS LAYOUT (
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
CREATE PAGE P AS LAYOUT (
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
    }
}
