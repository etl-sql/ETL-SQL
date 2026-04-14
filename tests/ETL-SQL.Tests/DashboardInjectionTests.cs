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
        [Theory]
        [InlineData("'; PRINT 'INJECTION_SUCCESSFUL'; --", "Statement Termination")]
        [InlineData("'; DROP TABLE #SourceTable; --", "Destructive Suffix")]
        [InlineData("' OR '1'='1", "Tautology")]
        [InlineData("'); EXEC sp_help; --", "Parentheses Escape")]
        [InlineData("'; TRUNCATE src_conn.Table; --", "Connector Target")]
        public async Task SetParameterAsync_PreventsVariousScriptInjections(string maliciousPayload, string scenario)
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
                
                // 2. Initial build is implicit or explicit
                await service.GetManifestAsync();

                // 3. Attempt script injection
                var manifest = await service.SetParameterAsync("Category", maliciousPayload);
                var visual = manifest.Visuals.First(v => v.Name == "InjectionResult");

                // 4. Verification
                // The visual should be empty because 'Category' does not equal the malicious string
                // AND most importantly, the engine should not have crashed or executed the secondary statements.
                Assert.Empty(visual.Rows); 
                
                // Verify parameter was stored literally
                Assert.Equal(maliciousPayload, service.Parameters["Category"]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
