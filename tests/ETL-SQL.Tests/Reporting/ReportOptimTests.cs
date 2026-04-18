using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportPlayer;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Reporting
{
    public class DashboardOptimizationTests
    {
        [Fact]
        public async Task SetParameterAsync_OnlyRefreshesAffectedVisuals()
        {
            // 1. Setup a test script with two visuals: one depending on @Cat, one static.
            string csvPath = Path.Combine(Path.GetTempPath(), "test_data.csv");
            File.WriteAllText(csvPath, "Category,Value\nA,10\nB,20");

            string scriptPath = Path.Combine(Path.GetTempPath(), "test_report.rptsql");
            File.WriteAllText(scriptPath, $@"
DECLARE @Cat = 'A';
CREATE CONNECTION test_csv ON FLATFILE('{csvPath}');
CREATE VISUAL AffectedVisual AS TABLE (SOURCE = 'SELECT * FROM test_csv WHERE Category = @Cat');
CREATE VISUAL StaticVisual AS TABLE (SOURCE = 'SELECT * FROM test_csv');
CREATE PAGE Main AS LAYOUT (STRUCTURE = 'grid:2x1', MAP('A' = AffectedVisual, 'B' = StaticVisual));
");

            try 
            {
                var service = new DashboardService(scriptPath);
                
                // 2. Initial build
                var manifest1 = await service.GetManifestAsync();
                var builtAt1 = manifest1.BuiltAt;

                // 3. Update parameter
                await Task.Delay(100); // Ensure timestamp change
                var manifest2 = await service.SetParameterAsync("Cat", "B");
                
                // 4. Verify results
                Assert.True(manifest2.BuiltAt > builtAt1, "Manifest timestamp should update.");
                Assert.Contains(manifest2.Visuals, v => v.Name == "AffectedVisual");
                Assert.Contains(manifest2.Visuals, v => v.Name == "StaticVisual");
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
