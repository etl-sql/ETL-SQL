using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class CliOrchestratorTests
    {
        [Fact]
        public async Task CliOrchestrator_ParsesPerfFlag()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(async (ctx) =>
            {
                capturedContext = ctx;
                return 0;
            });

            await root.Parse(new[] { "run", "script.sql", "--perf" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.True(capturedContext!.IsPerfMode);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesTestFlag()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(async (ctx) =>
            {
                capturedContext = ctx;
                return 0;
            });

            await root.Parse(new[] { "test", "unit" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.True(capturedContext!.IsTestMode);
            Assert.Equal("unit", capturedContext.TestVal);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesBatchSize()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(async (ctx) =>
            {
                capturedContext = ctx;
                return 0;
            });

            await root.Parse(new[] { "run", "script.sql", "-b", "5000" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal(5000, capturedContext!.BatchSize);
        }

        [Fact]
        public async Task CliOrchestrator_DoctorParsesJsonStrictAndFullProfile()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(7);
            });

            var exitCode = await root.Parse(new[] { "doctor", "--json", "--strict", "--profile", "full" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.Equal(7, exitCode);
            Assert.NotNull(capturedContext);
            Assert.Equal("doctor", capturedContext!.Command);
            Assert.True(capturedContext.IsJsonMode);
            Assert.True(capturedContext.DoctorStrict);
            Assert.Equal("full", capturedContext.DoctorProfile);
        }

        [Fact]
        public async Task CliOrchestrator_DoctorDefaultsToQuickProfile()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            var exitCode = await root.Parse(new[] { "doctor" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.Equal(0, exitCode);
            Assert.NotNull(capturedContext);
            Assert.False(capturedContext!.IsJsonMode);
            Assert.False(capturedContext.DoctorStrict);
            Assert.Equal("quick", capturedContext.DoctorProfile);
        }

        [Fact]
        public async Task CliOrchestrator_AdminDoctorRoutesToDoctorCommand()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[] { "admin", "doctor", "--profile", "full" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("doctor", capturedContext!.Command);
            Assert.Equal("full", capturedContext.DoctorProfile);
        }

        [Fact]
        public async Task CliOrchestrator_AdminSupportBundleParsesOutput()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[] { "admin", "support-bundle", "--output", "bundle.zip" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("admin-support-bundle", capturedContext!.Command);
            Assert.Equal("bundle.zip", capturedContext.BundleOutput);
        }

        [Fact]
        public async Task CliOrchestrator_InitParsesDirectoryAndForce()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[] { "init", "workspace", "--force" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("init", capturedContext!.Command);
            Assert.Equal("workspace", capturedContext.InitDirectory);
            Assert.True(capturedContext.InitForce);
        }

        [Fact]
        public async Task CliOrchestrator_AdminBackupParsesOutputDir()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[] { "admin", "backup", "--output-dir", "D:/backups" }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("admin-backup", capturedContext!.Command);
            Assert.Equal("D:/backups", capturedContext.BackupOutputDir);
        }

        [Fact]
        public async Task CliOrchestrator_AdminRestoreParsesFromKeysToAndValidate()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(
                new[] { "admin", "restore", "--from", "data.zip", "--keys", "keys.zip", "--to", "out", "--validate" },
                null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("admin-restore", capturedContext!.Command);
            Assert.Equal("data.zip", capturedContext.RestoreFrom);
            Assert.Equal("keys.zip", capturedContext.RestoreKeys);
            Assert.Equal("out", capturedContext.RestoreTo);
            Assert.True(capturedContext.RestoreValidateOnly);
        }

        [Fact]
        public async Task CliOrchestrator_AdminHaSoakValidateParsesEvidenceOptions()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[]
            {
                "admin", "ha-soak", "validate",
                "--run-root", "runs/phase6",
                "--required-gate", "All",
                "--required-commit", "abc123",
                "--allow-dirty",
                "--markdown-report", "certification-results/phase6/evidence-validation.md"
            }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("admin-ha-soak-validate", capturedContext!.Command);
            Assert.Equal("runs/phase6", capturedContext.HaSoakRunRoot);
            Assert.Equal("All", capturedContext.HaSoakRequiredGate);
            Assert.Equal("abc123", capturedContext.HaSoakRequiredCommit);
            Assert.True(capturedContext.HaSoakAllowDirty);
            Assert.Equal("certification-results/phase6/evidence-validation.md", capturedContext.HaSoakMarkdownReport);
        }

        [Fact]
        public async Task CliOrchestrator_AdminHaSoakLargeJobRunParsesRunnerOptions()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[]
            {
                "admin", "ha-soak", "large-job-run",
                "--run-root", "runs/phase6",
                "--plan", "runs/phase6/ha-large-job-soak-plan.json",
                "--output-root", "certification-results/ha-large-job-soak/run-1",
                "--duration-seconds", "2",
                "--force"
            }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("admin-ha-soak-large-job-run", capturedContext!.Command);
            Assert.Equal("runs/phase6", capturedContext.HaSoakRunRoot);
            Assert.Equal("runs/phase6/ha-large-job-soak-plan.json", capturedContext.HaSoakPlanPath);
            Assert.Equal("certification-results/ha-large-job-soak/run-1", capturedContext.HaSoakOutputRoot);
            Assert.Equal(2, capturedContext.HaSoakDurationSeconds);
            Assert.True(capturedContext.HaSoakForce);
        }

        [Fact]
        public async Task CliOrchestrator_EnterpriseEnrollParsesProtectedBootstrapOptions()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[]
            {
                "enterprise", "enroll",
                "--tenant", "corp-prod",
                "--policy-endpoint", "https://policy.example.test/etl-sql",
                "--signing-key", "policy.pem",
                "--client-certificate-thumbprint", new string('A', 40),
                "--service-identity", "NT SERVICE\\ETL-SQL",
                "--max-offline-hours", "12"
            }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("enterprise-enroll", capturedContext!.Command);
            Assert.Equal("corp-prod", capturedContext.EnterpriseTenant);
            Assert.Equal("https://policy.example.test/etl-sql", capturedContext.EnterprisePolicyEndpoint);
            Assert.Equal("policy.pem", capturedContext.EnterpriseSigningKeyPath);
            Assert.Equal(12, capturedContext.EnterpriseMaxOfflineHours);
            Assert.False(capturedContext.EnterpriseAllowOfflineFailure);
        }

        [Fact]
        public async Task CliOrchestrator_EnterpriseUnenrollRequiresExplicitParsedConfirmation()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[] { "enterprise", "unenroll", "--yes" }, null)
                .InvokeAsync(new InvocationConfiguration(), default);

            Assert.Equal("enterprise-unenroll", capturedContext!.Command);
            Assert.True(capturedContext.EnterpriseConfirm);
        }

        [Theory]
        [InlineData(false, false, false, 0)]
        [InlineData(false, false, true, 0)]
        [InlineData(false, true, false, 0)]
        [InlineData(true, false, false, 0)]
        [InlineData(true, false, true, 1)]
        [InlineData(true, true, false, 1)]
        public void DoctorExitCode_StrictOnlyFailsOnWarningsOrFailures(
            bool strict,
            bool hasFailures,
            bool hasWarnings,
            int expectedExitCode)
        {
            Assert.Equal(expectedExitCode, EngineRunner.DoctorExitCode(strict, hasFailures, hasWarnings));
        }
    }
}
