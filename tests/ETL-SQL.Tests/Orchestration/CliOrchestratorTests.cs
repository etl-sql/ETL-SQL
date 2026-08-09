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
        public async Task CliOrchestrator_RunParsesQualityEvidenceOptions()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            var exitCode = await root.Parse(new[]
            {
                "run", "pipeline.etlsql", "--quality-summary", "--output-json", "evidence/run.json"
            }).InvokeAsync();

            Assert.Equal(0, exitCode);
            Assert.NotNull(capturedContext);
            Assert.True(capturedContext.QualitySummary);
            Assert.Equal("evidence/run.json", capturedContext.OutputJsonPath);
        }

        [Fact]
        public async Task CliOrchestrator_RecordFlagOverridesConfiguration()
        {
            var ctx = await ParseRunAsync("run", "s.etlsql", "--record");
            Assert.True(ctx.RecordRun);
        }

        [Fact]
        public async Task CliOrchestrator_NoRecordFlagOverridesConfiguration()
        {
            var ctx = await ParseRunAsync("run", "s.etlsql", "--no-record");
            Assert.False(ctx.RecordRun);
        }

        /// <summary>Absent means "leave Engine:AuditAdHocRuns in charge", not "do not record".</summary>
        [Fact]
        public async Task CliOrchestrator_AbsentRecordFlagsLeaveTheDecisionUnset()
        {
            var ctx = await ParseRunAsync("run", "s.etlsql");
            Assert.Null(ctx.RecordRun);
        }

        /// <summary>The safe reading of a contradictory command line is to record less, not more.</summary>
        [Fact]
        public async Task CliOrchestrator_NoRecordWinsOverRecord()
        {
            var ctx = await ParseRunAsync("run", "s.etlsql", "--record", "--no-record");
            Assert.False(ctx.RecordRun);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesJobName()
        {
            var ctx = await ParseRunAsync("run", "s.etlsql", "--job-name", "nightly-load-eu");
            Assert.Equal("nightly-load-eu", ctx.JobName);
        }

        [Fact]
        public async Task CliOrchestrator_AbsentJobNameKeepsTheFileNameDefault()
        {
            var ctx = await ParseRunAsync("run", "s.etlsql");
            Assert.Null(ctx.JobName);
        }

        // ── admin identity verbs ────────────────────────────────────────────────

        [Fact]
        public async Task CliOrchestrator_ParsesIdempotenceFlagsForCreateAndDelete()
        {
            var create = await ParseRunAsync("admin", "user", "create", "--username", "jsmith", "--if-not-exists");
            Assert.True(create.IfNotExists);
            Assert.Equal("jsmith", create.AdminUsername);

            var delete = await ParseRunAsync("admin", "user", "delete", "--username", "jsmith", "--if-exists");
            Assert.True(delete.IfExists);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesIfVersionForGuardedWrites()
        {
            var ctx = await ParseRunAsync("admin", "user", "disable", "--username", "jsmith", "--if-version", "7");
            Assert.Equal(7L, ctx.IfVersion);
        }

        /// <summary>Absent means "carry through the version just read", not "overwrite blindly".</summary>
        [Fact]
        public async Task CliOrchestrator_AbsentIfVersionLeavesTheExpectationUnset()
        {
            var ctx = await ParseRunAsync("admin", "user", "disable", "--username", "jsmith");
            Assert.Null(ctx.IfVersion);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesPasswordStdinAndNeverAPasswordArgument()
        {
            var ctx = await ParseRunAsync("admin", "user", "create", "--username", "jsmith", "--password-stdin");
            Assert.True(ctx.PasswordStdin);
        }

        /// <summary>
        /// A --password flag must not exist. If one is ever added, the secret lands in shell history
        /// and CI logs, which is the failure this verb family was designed to avoid.
        /// </summary>
        [Fact]
        public async Task CliOrchestrator_RejectsAPasswordOnTheCommandLine()
        {
            CliContext? captured = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                captured = ctx;
                return Task.FromResult(0);
            });

            var parse = root.Parse(new[] { "admin", "user", "create", "--username", "jsmith", "--password", "hunter2" });

            Assert.NotEmpty(parse.Errors);
            Assert.Null(captured);
        }

        [Fact]
        public async Task CliOrchestrator_CreateUsesAnAssignRoleNotTheListFilter()
        {
            var ctx = await ParseRunAsync("admin", "user", "create", "--username", "jsmith", "--role", "Publisher");
            Assert.Equal("Publisher", ctx.AdminRole);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesGroupMembershipVerbs()
        {
            var add = await ParseRunAsync("admin", "group", "add-member", "--name", "Finance", "--username", "jsmith");
            Assert.Equal("Finance", add.AdminGroupName);
            Assert.Equal("jsmith", add.AdminUsername);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesPortalUrlAndClientIdButNoSecret()
        {
            var ctx = await ParseRunAsync("admin", "portal-whoami",
                "--portal-url", "https://portal.example.com", "--client-id", "sa_abc");

            Assert.Equal("https://portal.example.com", ctx.PortalUrl);
            Assert.Equal("sa_abc", ctx.PortalClientId);
        }

        [Fact]
        public async Task CliOrchestrator_HasNoClientSecretOption()
        {
            CliContext? captured = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                captured = ctx;
                return Task.FromResult(0);
            });

            var parse = root.Parse(new[] { "admin", "portal-whoami", "--client-secret", "sas_leak" });

            Assert.NotEmpty(parse.Errors);
            Assert.Null(captured);
        }

        /// <summary>Only the fields actually supplied are sent, so an update cannot blank a field silently.</summary>
        [Fact]
        public async Task CliOrchestrator_UserUpdateCarriesOnlyTheSuppliedFields()
        {
            var ctx = await ParseRunAsync("admin", "user", "update", "--username", "jsmith", "--email", "j@corp.local");

            Assert.Equal("j@corp.local", ctx.AdminEmail);
            Assert.Null(ctx.AdminFirstName);
            Assert.Null(ctx.AdminLastName);
            Assert.Null(ctx.AdminRole);
        }

        [Fact]
        public async Task CliOrchestrator_GroupUpdateKeepsTheLookupNameSeparateFromTheNewName()
        {
            var ctx = await ParseRunAsync("admin", "group", "update", "--name", "Finance", "--new-name", "Finance EU");

            Assert.Equal("Finance", ctx.AdminGroupName);
            Assert.Equal("Finance EU", ctx.AdminNewName);
        }

        [Fact]
        public async Task CliOrchestrator_CapabilityFlagIsRepeatable()
        {
            var ctx = await ParseRunAsync("admin", "group", "set-capabilities", "--name", "Finance",
                "--capability", "studio.author", "--capability", "studio.publish");

            Assert.Equal(["studio.author", "studio.publish"], ctx.AdminCapabilities);
        }

        /// <summary>
        /// An empty set is meaningful — it revokes every capability — so it must be distinguishable
        /// from the flag being absent.
        /// </summary>
        [Fact]
        public async Task CliOrchestrator_AbsentCapabilityFlagIsNotAnEmptyGrant()
        {
            var ctx = await ParseRunAsync("admin", "group", "capabilities", "--name", "Finance");
            Assert.Null(ctx.AdminCapabilities);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesServiceAccountCreateWithoutASecretArgument()
        {
            var ctx = await ParseRunAsync("admin", "service-account", "create",
                "--name", "nightly-loader", "--owner", "tenant-admin",
                "--scope", "admin.identity", "--scope", "portal.read",
                "--role", "Admin", "--secret-out", "nightly-loader.secret");

            Assert.Equal("admin-service-account-create", ctx.Command);
            Assert.Equal("nightly-loader", ctx.ServiceAccountName);
            Assert.Equal("tenant-admin", ctx.ServiceAccountOwner);
            Assert.Equal(["admin.identity", "portal.read"], ctx.ServiceAccountScopes);
            Assert.Equal(["Admin"], ctx.ServiceAccountRoles);
            Assert.Equal("nightly-loader.secret", ctx.ServiceAccountSecretOutput);
        }

        [Fact]
        public async Task CliOrchestrator_MachineStoreScopeIsExplicitForSecretsAndConnections()
        {
            var secret = await ParseRunAsync("admin", "machine", "secret", "set",
                "--name", "warehouse_password", "--value", "test-only");
            Assert.Equal("admin-machine-secret-set", secret.Command);
            Assert.Equal("warehouse_password", secret.SecretName);

            var connection = await ParseRunAsync("admin", "machine", "connection", "set",
                "--alias", "warehouse", "--type", "MSSQL", "--option", "SERVER=sql01");
            Assert.Equal("admin-machine-connection-set", connection.Command);
            Assert.Equal("warehouse", connection.ConnectionAlias);

            var root = CliOrchestrator.BuildRootCommand(_ => Task.FromResult(0));
            Assert.NotEmpty(root.Parse(new[] { "admin", "set-secret", "--name", "ambiguous" }).Errors);
            Assert.NotEmpty(root.Parse(new[] { "admin", "list-connections" }).Errors);
        }

        [Fact]
        public async Task CliOrchestrator_ParsesServiceAccountUpdateAndRotation()
        {
            var update = await ParseRunAsync("admin", "service-account", "update",
                "--name", "nightly-loader", "--disable", "--clear-expiry",
                "--clear-capabilities", "--if-version", "4");
            Assert.True(update.ServiceAccountDisable);
            Assert.True(update.ServiceAccountClearExpiry);
            Assert.True(update.ServiceAccountClearCapabilities);
            Assert.Equal(4L, update.IfVersion);

            var rotate = await ParseRunAsync("admin", "service-account", "rotate-secret",
                "--name", "nightly-loader", "--secret-out", "rotated.secret");
            Assert.Equal("admin-service-account-rotate-secret", rotate.Command);
            Assert.Equal("rotated.secret", rotate.ServiceAccountSecretOutput);
        }

        [Fact]
        public void CliOrchestrator_ServiceAccountHasNoClientSecretArgument()
        {
            var root = CliOrchestrator.BuildRootCommand(_ => Task.FromResult(0));
            var parse = root.Parse(new[]
            {
                "admin", "service-account", "create", "--name", "nightly-loader",
                "--client-secret", "sas_leak"
            });

            Assert.NotEmpty(parse.Errors);
        }

        [Fact]
        public async Task CliOrchestrator_ResetPasswordHasNoPasswordArgument()
        {
            CliContext? captured = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                captured = ctx;
                return Task.FromResult(0);
            });

            var parse = root.Parse(new[]
            {
                "admin", "user", "reset-password", "--username", "jsmith", "--password", "hunter2"
            });

            Assert.NotEmpty(parse.Errors);
            Assert.Null(captured);
        }

        private static async Task<CliContext> ParseRunAsync(params string[] args)
        {
            CliContext? captured = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                captured = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(args).InvokeAsync();

            Assert.NotNull(captured);
            return captured!;
        }

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
        public async Task CliOrchestrator_AdminHaSoakFaultRunParsesRunnerOptions()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            await root.Parse(new[]
            {
                "admin", "ha-soak", "fault-run",
                "--run-root", "runs/phase6",
                "--plan", "runs/phase6/ha-fault-injection-plan.json",
                "--output-root", "certification-results/ha-fault-injection/run-1",
                "--force"
            }, null).InvokeAsync(new InvocationConfiguration(), default);

            Assert.NotNull(capturedContext);
            Assert.Equal("admin-ha-soak-fault-run", capturedContext!.Command);
            Assert.Equal("runs/phase6", capturedContext.HaSoakRunRoot);
            Assert.Equal("runs/phase6/ha-fault-injection-plan.json", capturedContext.HaSoakPlanPath);
            Assert.Equal("certification-results/ha-fault-injection/run-1", capturedContext.HaSoakOutputRoot);
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

        [Fact]
        public async Task CliOrchestrator_ScanParsesSchemaOnlyOptions()
        {
            CliContext? capturedContext = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                capturedContext = ctx;
                return Task.FromResult(0);
            });

            var exitCode = await root.Parse(new[]
            {
                "scan", "SHARED:warehouse", "--pii", "--table", "sales.customers", "--json"
            }).InvokeAsync();

            Assert.Equal(0, exitCode);
            Assert.NotNull(capturedContext);
            Assert.Equal("scan", capturedContext.Command);
            Assert.Equal("SHARED:warehouse", capturedContext.ScanSource);
            Assert.Equal("sales.customers", capturedContext.ScanTable);
            Assert.True(capturedContext.ScanPii);
            Assert.True(capturedContext.IsJsonMode);
        }

        [Fact]
        public async Task CliOrchestrator_AdminPromotionPreflightParsesProfilesAndPaths()
        {
            CliContext? parsed = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                parsed = ctx;
                return Task.FromResult(0);
            });

            var exit = await root.Parse(new[]
            {
                "admin", "promotion", "preflight",
                "--source", "workspace",
                "--from-profile", "Solo",
                "--to-profile", "Team",
                "--output", "preflight.json"
            }).InvokeAsync();

            Assert.Equal(0, exit);
            Assert.NotNull(parsed);
            Assert.Equal("admin-promotion-preflight", parsed.Command);
            Assert.Equal("workspace", parsed.PromotionSource);
            Assert.Equal("Solo", parsed.PromotionFromProfile);
            Assert.Equal("Team", parsed.PromotionToProfile);
            Assert.Equal("preflight.json", parsed.PromotionOutput);
        }

        [Fact]
        public async Task CliOrchestrator_AdminPromotionValidateParsesPackageBindingsAndReport()
        {
            CliContext? parsed = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                parsed = ctx;
                return Task.FromResult(0);
            });

            var exit = await root.Parse(new[]
            {
                "admin", "promotion", "validate", "--package", "promotion.json",
                "--bind", "SHARED:dev=SHARED:prod", "--output", "validation.json"
            }).InvokeAsync();

            Assert.Equal(0, exit);
            Assert.NotNull(parsed);
            Assert.Equal("admin-promotion-validate", parsed.Command);
            Assert.Equal("promotion.json", parsed.PromotionPackage);
            Assert.Equal(["SHARED:dev=SHARED:prod"], parsed.PromotionBindings);
            Assert.Equal("validation.json", parsed.PromotionOutput);
        }

        [Fact]
        public async Task CliOrchestrator_SaasOnboardParsesTenantBoundaryOptions()
        {
            CliContext? parsed = null;
            var root = CliOrchestrator.BuildRootCommand(ctx =>
            {
                parsed = ctx;
                return Task.FromResult(0);
            });

            var exit = await root.Parse(new[]
            {
                "admin", "promotion", "saas-onboard", "--tenant", "tenant-alpha",
                "--source-profile", "Solo", "--source", "workspace", "--package", "promotion.json",
                "--output-root", "tenants", "--max-concurrent-jobs", "3", "--max-storage-mb", "2048",
                "--max-report-sessions", "9"
            }).InvokeAsync();

            Assert.Equal(0, exit);
            Assert.NotNull(parsed);
            Assert.Equal("admin-promotion-saas-onboard", parsed.Command);
            Assert.Equal("tenant-alpha", parsed.SaasTenantId);
            Assert.Equal("Solo", parsed.SaasSourceProfile);
            Assert.Equal(3, parsed.SaasMaxConcurrentJobs);
            Assert.Equal(2048, parsed.SaasMaxStorageMb);
            Assert.Equal(9, parsed.SaasMaxReportSessions);
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
