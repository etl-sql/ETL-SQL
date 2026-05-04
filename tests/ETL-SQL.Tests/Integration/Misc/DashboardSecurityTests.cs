using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.ReportPlayer;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Tests.Integration.Misc
{
    public class DashboardSecurityTests
    {
        [Fact]
        public async Task TestSetParameterAsync_PreventsScriptInjection()
        {
            // 1. Create a dummy report script
            var scriptPath = Path.Combine(Path.GetTempPath(), "security_test_" + Guid.NewGuid().ToString() + ".rptsql");
            var scriptBody = @"
CREATE VISUAL V1 AS CARD (SOURCE = (SELECT @SafeParam AS val, 'Label' AS lbl));
";

            File.WriteAllText(scriptPath, scriptBody);

            try
            {
                var service = new DashboardService(scriptPath, ETL_SQL.Tests.Reporting.DashboardTestHelper.CreateMockScopeFactory());

                // 2. Attempt script injection via a parameter value
                // If it's concatenated, this would cause two DECLAREs or a statement breakage.
                // With DeclareVariable, it should be treated as a literal string.
                var maliciousValue = "'; DROP TABLE #NonExistent; DECLARE @Injected = '";
                
                // This should succeed because it's just setting a variable value,
                // and the "DROP TABLE" should NOT execute.
                var manifest = await service.SetParameterAsync("SafeParam", maliciousValue);

                // 3. Verify that the variable in the evaluator HAS the malicious value as a literal
                // We can't easily inspect the internal evaluator of DashboardService without reflection,
                // but we CAN verify that NO secondary statements were executed.
                // If the injection worked, the engine would have tried to PARSE the ';' and execute DROP TABLE.
                
                // Since the scriptBody doesn't have #NonExistent, a real injection would THROW if it parsed.
                // But it's safer to check the value.
                
                // In DashboardService, the ManifestBuilder captures the data. 
                // The CARD visual V1 should contain the maliciousValue as its 'val'.
                var visual = manifest.Visuals.Find(v => v.Name == "V1");
                Assert.NotNull(visual);
                
                // The first row, first column should be the malicious value
                Assert.Equal(maliciousValue, visual.Rows[0][0]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
