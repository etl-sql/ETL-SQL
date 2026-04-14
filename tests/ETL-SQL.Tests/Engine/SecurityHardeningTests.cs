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

namespace ETL_SQL.Tests.Engine
{
    public class SecurityHardeningTests
    {
        private Evaluator CreateEvaluator()
        {
            return Program.ServiceProvider.GetRequiredService<Evaluator>();
        }

        [Fact]
        public async Task TestRootPathValidation_Throws()
        {
            var eval = CreateEvaluator();
            var rootPath = Path.GetPathRoot(Environment.CurrentDirectory);
            var sql = $"DELETE FILE '{rootPath}';";
            
            var script = TestHelpers.Parse(sql);
            var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
            Assert.Contains("Unauthorized access to root directory", ex.Message);
        }

        [Fact]
        public async Task TestProtectedDirectory_Throws()
        {
            var eval = CreateEvaluator();
            var sql = "DELETE FILE '.git/config';";
            
            var script = TestHelpers.Parse(sql);
            var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
            Assert.Contains("protected system directory", ex.Message);
        }

        [Fact]
        public async Task TestBlockedFileType_Throws()
        {
            var eval = CreateEvaluator();
            var tempFile = Path.Combine(Path.GetTempPath(), "test_hardening_blocked.dll");
            var sql = $"DELETE FILE '{tempFile.Replace("\\", "/")}';";
            
            var script = TestHelpers.Parse(sql);
            var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
            Assert.Contains("dangerous file type", ex.Message);
        }

        [Fact]
        public async Task TestRunawayProtection_CountLimit()
        {
            var eval = CreateEvaluator();
            var tempFile = Path.Combine(Path.GetTempPath(), "test_hardening_limit.csv");
            var scriptSql = $"DELETE FILE '{tempFile.Replace("\\", "/")}';\n";
            var fullSql = string.Concat(Enumerable.Repeat(scriptSql, 101));
            
            var script = TestHelpers.Parse(fullSql);
            var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
            Assert.Contains("safety limit of 100", ex.Message);
        }

        [Fact]
        public async Task TestPermissionOverride_AllowsLargeCount()
        {
            // We need to use ExecutionSession to test overrides
            var session = ETL_SQL.Program.ServiceProvider.GetRequiredService<ETL_SQL.Orchestrator.Execution.ExecutionSession>();
            
            var scriptSql = "DELETE FILE 'test.csv';\n";
            var fullSql = "SET WHAT_IF ON;\nSET ALLOW_GREATER_THAN_100_FILE ON;\n" + string.Concat(Enumerable.Repeat(scriptSql, 101));
            
            var result = await session.ExecuteAsync(fullSql);
            
            // It should NOT fail with SecurityException 100 limit
            bool hasSecurityError = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Safety limit of 100", StringComparison.OrdinalIgnoreCase));
            Assert.False(hasSecurityError, "Should NOT have triggered 100-file safety limit due to override.");
            
            // PROACTIVE CHECK: verify exactly 101 operations were 'performed' in WHAT_IF mode
            int attemptCount = result.Messages.Count(m => m.Contains("Would perform Delete_FILE", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(101, attemptCount);
        }

        [Fact]
        public async Task TestRecursiveDepth_Throws()
        {
            var eval = CreateEvaluator();
            eval.CurrentRecursiveDepth = 6;
            
            var ex = Assert.Throws<SecurityException>(() => eval.IncrementOperationCount());
            Assert.Contains("Recursive operation depth (6) exceeds the safety limit of 5", ex.Message);
        }
    }
}
