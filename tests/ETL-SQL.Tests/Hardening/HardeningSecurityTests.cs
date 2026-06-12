using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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
        public void TestResolvePathSymlinks_UncPath_DoesNotCrash()
        {
            // UNC paths must not crash the symlink resolver — they are valid absolute paths
            // but have a different root format ("\\server\share\").
            var uncPath = @"\\server\share\data.csv";
            var result = SecurityService.ResolvePathSymlinks(uncPath);
            // UNC paths to non-existent servers just return themselves (no symlinks to follow).
            Assert.False(string.IsNullOrEmpty(result));
            Assert.DoesNotContain("..", result);
        }

        [Fact]
        public void TestResolvePathSymlinks_MixedSeparators_NormalizesCorrectly()
        {
            // Mixed forward/backslash separators should resolve to the same canonical path
            // as the backslash-only form. This matters on Windows where both separators are valid.
            var tempDir = Path.GetTempPath();
            var withForward = Path.Combine(tempDir, "test_mixed").Replace('\\', '/');
            var withBackslash = Path.Combine(tempDir, "test_mixed");

            var resolvedForward = SecurityService.ResolvePathSymlinks(withForward);
            var resolvedBackslash = SecurityService.ResolvePathSymlinks(withBackslash);

            Assert.Equal(
                resolvedBackslash.Replace('/', '\\'),
                resolvedForward.Replace('/', '\\'),
                StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void TestValidatePath_UncPath_BlockedInDefinedMode()
        {
            // In DEFINED mode, any path not in an approved safe zone should be blocked,
            // including UNC paths.  This is the tightest security posture.
            var securityService = Program.ServiceProvider.GetRequiredService<SecurityService>();
            var originalMode = securityService.ProtectionMode;
            var originalTest = securityService.IsTestMode;
            securityService.ProtectionMode = PathProtectionMode.Defined;
            securityService.IsTestMode = false;
            try
            {
                var ex = Assert.Throws<SecurityException>(
                    () => securityService.ValidatePath(@"\\server\share\data.csv"));
                Assert.Contains("Approved Safe Zone", ex.Message);
            }
            finally
            {
                securityService.ProtectionMode = originalMode;
                securityService.IsTestMode = originalTest;
            }
        }

        [Fact]
        public void TestValidatePath_MixedSeparators_SameAsBackslash()
        {
            // Forward-slash paths should be treated identically to backslash paths.
            // If a backslash path would be blocked, the forward-slash equivalent must also be blocked.
            var securityService = Program.ServiceProvider.GetRequiredService<SecurityService>();
            var originalTest = securityService.IsTestMode;
            securityService.IsTestMode = false;
            try
            {
                // .git/config is a blocked path regardless of separator style
                var exBackslash = Assert.Throws<SecurityException>(
                    () => securityService.ValidatePath(@".git\config"));
                var exForward = Assert.Throws<SecurityException>(
                    () => securityService.ValidatePath(".git/config"));

                Assert.Contains("protected system/environment directory", exBackslash.Message);
                Assert.Contains("protected system/environment directory", exForward.Message);
            }
            finally
            {
                securityService.IsTestMode = originalTest;
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
