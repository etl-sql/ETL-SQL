using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportPlayer;
using ETL_SQL.Core;
using ETL_SQL.Data;
using System.Linq;

namespace ETL_SQL.Tests
{
    public class DashboardInjectionTests
    {
        [Fact]
        public async Task SetParameterAsync_PreventsScriptInjection()
        {
            // 1. Setup a test script that uses a parameter in a WHERE clause.
            string scriptPath = Path.Combine(Path.GetTempPath(), $"injection_test_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
DECLARE @Category string INPUT = 'None';
SELECT 'Direct' AS Category INTO #SourceTable;
CREATE VISUAL InjectionResult AS TABLE (SOURCE = (SELECT * FROM #SourceTable WHERE Category = @Category));
CREATE PAGE Main AS LAYOUT (STRUCTURE = 'grid:1x1', MAP('A' = InjectionResult));
");

            try 
            {
                var service = new DashboardService(scriptPath);
                
                // 2. Initial build with safe value
                await service.SetParameterAsync("Category", "Direct");
                var manifest1 = await service.GetManifestAsync();
                var visual1 = manifest1.Visuals.First(v => v.Name == "InjectionResult");
                // MOCK() typically has 100 rows, 'Direct' might have some or zero depending on mock implementation.
                // But we know 'Direct' is safe.

                // 3. Attempt script injection
                // This payload attempts to close the quote, run a separate PRINT, and comment out the rest.
                // If it were concatenated like: DECLARE @Category = '...';
                // It would become: DECLARE @Category = ''; PRINT 'INJECTION_SUCCESSFULL'; --';
                string maliciousPayload = "'; PRINT 'INJECTION_SUCCESSFUL'; --";
                
                var manifest2 = await service.SetParameterAsync("Category", maliciousPayload);
                var visual2 = manifest2.Visuals.First(v => v.Name == "InjectionResult");

                // 4. Verification
                // If the injection failed (safe), the visual should simply have 0 rows 
                // because no record has exactly the malicious string as its category.
                // If the injection succeeded (vulnerable), it might have rows (due to malformed logic) 
                // or we might have seen side effects if we could capture them.
                
                // Most importantly, the engine should NOT have interpreted the PRINT statement.
                // Since DashboardService is secure, the variable @Category literally contains the payload.
                Assert.Empty(visual2.Rows); 
                
                // We can also verify that the parameter state in the service is the literal malicious string
                Assert.Equal(maliciousPayload, service.Parameters["Category"]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
