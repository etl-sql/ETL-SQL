using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;
using System.IO;
using System;
using System.Linq;
using ETL_SQL.Services;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Hardening.Hardening
{
    public class SecurityHardeningTests
    {

        [Fact]
        public async Task TestRootPathValidation_Throws()
        {
            var eval = Program.ServiceProvider.GetRequiredService<Evaluator>();
            var original = eval.SecurityService.IsTestMode;
            eval.SecurityService.IsTestMode = false;
            try
            {
                var rootPath = Path.GetPathRoot(Environment.CurrentDirectory);
                var sql = $"DELETE FILE '{rootPath}';";
                
                var script = TestHelpers.Parse(sql);
                var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
                Assert.Contains("Unauthorized access to root directory", ex.Message);
            }
            finally
            {
                eval.SecurityService.IsTestMode = original;
            }
        }

        [Fact]
        public async Task TestProtectedDirectory_Throws()
        {
            var eval = Program.ServiceProvider.GetRequiredService<Evaluator>();
            var original = eval.SecurityService.IsTestMode;
            eval.SecurityService.IsTestMode = false;
            try
            {
                var sql = "DELETE FILE '.git/config';";
                
                var script = TestHelpers.Parse(sql);
                var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
                Assert.Contains("protected system/environment directory", ex.Message);
            }
            finally
            {
                eval.SecurityService.IsTestMode = original;
            }
        }

        [Fact]
        public async Task TestBlockedFileType_Throws()
        {
            var eval = Program.ServiceProvider.GetRequiredService<Evaluator>();
            var original = eval.SecurityService.IsTestMode;
            eval.SecurityService.IsTestMode = false;
            
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var securityService = eval.SecurityService;
            securityService.ApprovedSafeZones.Add(baseDir);
            try
            {
                var tempFile = Path.Combine(baseDir, "test_hardening_blocked.dll");
                var sql = $"DELETE FILE '{tempFile.Replace("\\", "/")}';";
                var script = TestHelpers.Parse(sql);
                
                var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
                Assert.Contains("dangerous file type", ex.Message);
            }
            finally
            {
                securityService.IsTestMode = original;
                securityService.ApprovedSafeZones.Remove(baseDir);
            }
        }

        [Fact]
        public async Task TestRunawayProtection_CountLimit()
        {
            var securityService = Program.ServiceProvider.GetRequiredService<SecurityService>();
            var originalTestMode = securityService.IsTestMode;
            securityService.IsTestMode = false;
            
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            securityService.ApprovedSafeZones.Add(baseDir);

            try
            {
                var eval = Program.ServiceProvider.GetRequiredService<Evaluator>();
                var tempFile = Path.Combine(baseDir, "test_hardening_limit.csv");
                var scriptSql = $"DELETE FILE '{tempFile.Replace("\\", "/")}';\n";
                var fullSql = string.Concat(Enumerable.Repeat(scriptSql, 101));
                
                var script = TestHelpers.Parse(fullSql);
                var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
                Assert.Contains("limit of 100", ex.Message);
            }
            finally
            {
                securityService.IsTestMode = originalTestMode;
                securityService.ApprovedSafeZones.Remove(baseDir);
            }
        }

        [Fact]
        public async Task TestPermissionOverride_AllowsLargeCount()
        {
            // We need to use ExecutionSession to test overrides
            var session = ETL_SQL.Program.ServiceProvider.GetRequiredService<ETL_SQL.Orchestrator.Execution.ExecutionSession>();
            var securityService = ETL_SQL.Program.ServiceProvider.GetRequiredService<ETL_SQL.Services.SecurityService>();
            
            var originalTestMode = securityService.IsTestMode;
            securityService.IsTestMode = false; // Force enforcement logic to run
            
            // AUTHORIZE current test directory to bypass bin/obj block while IsTestMode is false
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            securityService.ApprovedSafeZones.Add(baseDir);

            try
            {
                var tempFile = Path.Combine(baseDir, "hardening_override_test.csv");
                var scriptSql = $"DELETE FILE '{tempFile.Replace("\\", "/")}';\n";
                var fullSql = "SET WHAT_IF ON;\nSET ALLOW_GREATER_THAN_100_FILE ON;\n" + string.Concat(Enumerable.Repeat(scriptSql, 101));
                
                var scriptPath = Path.Combine(baseDir, "test_override.sql");
                File.WriteAllText(scriptPath, fullSql);
                
                // Run as a script so CurrentScriptPath is populated
                var result = await session.ExecuteAsync($"RUN SCRIPT '{scriptPath.Replace("\\", "/")}';");
                
                // It should NOT fail with SecurityException 100 limit
                bool hasSecurityError = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && (d.Message.Contains("Safety limit of 100", StringComparison.OrdinalIgnoreCase) || d.Message.Contains("Runaway", StringComparison.OrdinalIgnoreCase)));
                if (hasSecurityError || result.Messages.Count == 0)
                {
                    var msgs = string.Join("\n", result.Messages.Select(m => m.Message));
                    var diags = string.Join("\n", result.Diagnostics.Select(d => d.Message));
                    Assert.Fail($"Security override failed or no messages captured.\nDIAGNOSTICS:\n{diags}\nMESSAGES:\n{msgs}");
                }
                
                // PROACTIVE CHECK: verify exactly 101 operations were 'performed' in WHAT_IF mode
                int attemptCount = result.Messages.Count(m => m.Message.Contains("Would perform Delete_FILE", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(101, attemptCount);
            }
            finally
            {
                securityService.IsTestMode = originalTestMode;
                securityService.ApprovedSafeZones.Remove(baseDir);
            }
        }

        [Fact]
        public async Task TestRecursiveDepth_Throws()
        {
            var eval = Program.ServiceProvider.GetRequiredService<Evaluator>();
            var securityService = eval.SecurityService;
            
            var originalTestMode = securityService.IsTestMode;
            securityService.IsTestMode = false;
            var originalMax = securityService.MaxRecursiveDepth;
            securityService.MaxRecursiveDepth = 5;

            try
            {
                eval.CurrentRecursiveDepth = 6;
                eval.AllowDeepRecursion = false;
                
                var ex = Assert.Throws<SecurityException>(() => eval.IncrementOperationCount());
                Assert.Contains("Recursive operation depth (6) exceeds the safety limit of 5", ex.Message);
            }
            finally
            {
                securityService.IsTestMode = originalTestMode;
                securityService.MaxRecursiveDepth = originalMax;
            }
        }
    }
}
