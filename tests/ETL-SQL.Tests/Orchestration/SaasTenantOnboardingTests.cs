using System.Text.Json;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.TestSupport;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "DeploymentProfile")]
public sealed class SaasTenantOnboardingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"saas-onboard-{Guid.NewGuid():N}");

    public SaasTenantOnboardingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SoloAndEnterpriseOnboarding_CreateDisjointHostFixedTenantBoundaries()
    {
        var sourceRoot = Path.Combine(_root, "source");
        var outputRoot = Path.Combine(_root, "tenants");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "pipelines"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "pipelines", "load.etlsql"),
            "SELECT 1 AS Value INTO #stage;");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "etlsql-policy.json"), "{}");

        var sourceStore = new SQLiteJobHistoryStore(Path.Combine(_root, "source-orchestrator.db"));
        await sourceStore.InitializeAsync();
        await sourceStore.SaveJobAsync(new JobDefinition(
            "load", "RUN SCRIPT 'pipelines/load.etlsql';", 1, "HOUR", null, null, null,
            JobType: JobTargetKind.Script, TargetPath: "pipelines/load.etlsql"));
        var package = await OrchestratorPromotionPackageService.ExportAsync(sourceStore, sourceStore, sourceStore);
        var packagePath = Path.Combine(_root, "promotion.json");
        await using (var packageStream = File.Create(packagePath))
            await OrchestratorPromotionPackageService.WriteAsync(package, packageStream);

        CliContext Context(string tenant, string profile, int jobs) => new()
        {
            SaasTenantId = tenant,
            SaasSourceProfile = profile,
            PromotionSource = sourceRoot,
            PromotionPackage = packagePath,
            SaasOutputRoot = outputRoot,
            SaasMaxConcurrentJobs = jobs,
            SaasMaxStorageMb = 2048,
            SaasMaxReportSessions = 8
        };

        var alpha = await SaasTenantOnboardingService.OnboardAsync(Context("tenant-alpha", "Solo", 2));
        var beta = await SaasTenantOnboardingService.OnboardAsync(Context("tenant-beta", "Enterprise", 5));

        Assert.Equal(SaasTenantOnboardingService.ManifestSchema, alpha.SchemaVersion);
        Assert.False(alpha.Activated);
        Assert.False(alpha.SupportAccessEnabled);
        Assert.Equal("tenant/tenant-alpha", alpha.SecretNamespace);
        Assert.Equal("etlsql.tenant.tenant-beta", beta.TelemetryNamespace);
        Assert.Equal(2, alpha.MaxConcurrentJobs);
        Assert.Equal(5, beta.MaxConcurrentJobs);
        Assert.Equal(alpha.PortableArtifacts.Select(a => (a.Path, a.Sha256)),
            beta.PortableArtifacts.Select(a => (a.Path, a.Sha256)));

        var alphaRoot = Path.Combine(outputRoot, "tenant-alpha");
        var betaRoot = Path.Combine(outputRoot, "tenant-beta");
        foreach (var boundary in alpha.Boundaries)
        {
            var alphaPath = Path.GetFullPath(Path.Combine(alphaRoot, boundary.RelativePath));
            var betaPath = Path.GetFullPath(Path.Combine(betaRoot, boundary.RelativePath));
            Assert.NotEqual(alphaPath, betaPath);
            Assert.False(betaPath.StartsWith(alphaRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        var alphaConfig = await File.ReadAllTextAsync(Path.Combine(alphaRoot, "config", "appsettings.tenant.json"));
        var betaConfig = await File.ReadAllTextAsync(Path.Combine(betaRoot, "config", "appsettings.tenant.json"));
        Assert.Contains("\"authorityMode\": \"HostFixed\"", alphaConfig);
        Assert.Contains("\"enabled\": false", alphaConfig);
        Assert.DoesNotContain("tenant-beta", alphaConfig);
        Assert.DoesNotContain("tenant-alpha", betaConfig);

        var alphaStore = new SQLiteJobHistoryStore(Path.Combine(alphaRoot, "databases", "orchestrator.db"));
        var betaStore = new SQLiteJobHistoryStore(Path.Combine(betaRoot, "databases", "orchestrator.db"));
        Assert.False(Assert.Single(await alphaStore.GetAllJobsAsync()).IsEnabled);
        Assert.False(Assert.Single(await betaStore.GetAllJobsAsync()).IsEnabled);
        await alphaStore.SaveJobAsync(new JobDefinition("alpha-only", "SELECT 1;", 1, "HOUR", null, null, null));
        Assert.Equal(2, (await alphaStore.GetAllJobsAsync()).Count());
        Assert.Single(await betaStore.GetAllJobsAsync());

        var runAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
        var alphaRun = await alphaStore.ImportJobHistoryAsync(new JobHistoryEntry(
            0, "alpha-only", runAt, runAt.AddMinutes(1), "FAILED", "quality gate",
            RowsProcessed: 10, RowsQuarantined: 1, DataQualityFailures: "email:not-null=1"));
        await alphaStore.SaveJobDataQualityFailuresAsync(alphaRun,
        [
            new DataQualityRuleFailureMetric("alpha.customers", "email", "not-null", "QUARANTINE", 1, "alpha-steward")
        ]);
        await alphaStore.SaveLineageAsync(
        [
            new LineageEntry("alpha.customers", "MERGE")
            {
                SourceTables = ["alpha.stage"],
                Metadata = new Dictionary<string, string> { ["classification"] = "confidential" }
            }
        ], "alpha-only", "pipelines/load.etlsql", runAt);
        Assert.Single(await alphaStore.GetRecentLineageAsync(), row => row.TargetTable == "alpha.customers");
        Assert.DoesNotContain(await betaStore.GetRecentLineageAsync(), row => row.TargetTable == "alpha.customers");
        Assert.Single(await alphaStore.GetDataQualityFailuresAsync(), row => row.TargetTable == "alpha.customers");
        Assert.DoesNotContain(await betaStore.GetDataQualityFailuresAsync(), row => row.TargetTable == "alpha.customers");

        var alphaPii = PiiSchemaScanner.BuildReport(
            [("tenant-alpha", "customers", (IReadOnlyList<string>)["EmailAddress"], 1)], null, DateTimeOffset.UnixEpoch);
        var betaPii = PiiSchemaScanner.BuildReport(
            [("tenant-beta", "inventory", (IReadOnlyList<string>)["StockKeepingUnit"], 1)], null, DateTimeOffset.UnixEpoch);
        Assert.Contains(alphaPii.Findings, finding => finding.Column == "EmailAddress");
        Assert.DoesNotContain(betaPii.Findings, finding => finding.Source == "tenant-alpha" || finding.Column == "EmailAddress");

        var alphaOutbox = new SecurityEventOutbox(new SecurityEventOutboxOptions
        {
            DatabasePath = Path.Combine(alphaRoot, "queues", "security", "events.db")
        });
        var betaOutbox = new SecurityEventOutbox(new SecurityEventOutboxOptions
        {
            DatabasePath = Path.Combine(betaRoot, "queues", "security", "events.db")
        });
        alphaOutbox.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Warning, SecurityEventType.OperationDenied, "alpha-user", "alpha-user",
            "tenant-alpha/report", SecurityEventDecision.Denied, "cross-tenant access denied") with
        {
            TenantId = "tenant-alpha"
        });
        Assert.Single(alphaOutbox.ClaimBatch(10, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        Assert.Empty(betaOutbox.ClaimBatch(10, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));

        var alphaCacheEntry = SaasTenantOnboardingService.ResolveTenantPath(alphaRoot, "cache/catalog/entry.json");
        Directory.CreateDirectory(Path.GetDirectoryName(alphaCacheEntry)!);
        await File.WriteAllTextAsync(alphaCacheEntry, "tenant-alpha-cache");
        Assert.False(File.Exists(SaasTenantOnboardingService.ResolveTenantPath(betaRoot, "cache/catalog/entry.json")));
        Assert.Throws<InvalidDataException>(() =>
            SaasTenantOnboardingService.ResolveTenantPath(alphaRoot, "../tenant-beta/cache/catalog/entry.json"));

        var quotaRoot = Path.Combine(_root, "quota-proof");
        Directory.CreateDirectory(quotaRoot);
        await File.WriteAllBytesAsync(Path.Combine(quotaRoot, "payload.bin"), new byte[1025]);
        Assert.Throws<InvalidOperationException>(() =>
            SaasTenantOnboardingService.EnsureStorageWithinQuota(quotaRoot, 1024));

        await Assert.ThrowsAsync<IOException>(() =>
            SaasTenantOnboardingService.OnboardAsync(Context("tenant-alpha", "Solo", 2)));

        await DeploymentCertificationEvidenceWriter.WriteAsync(
            "profile-saas-managed-dedicated",
            new
            {
                schemaVersion = "etl-sql.deployment-scenario-evidence/v1",
                scenarioId = "SaaSManagedDedicatedIsolation",
                kind = "Profile",
                sourceProfile = "Solo, Enterprise",
                targetProfile = "SaaS",
                topology = "Managed Dedicated (one host-fixed tenant runtime boundary per tenant)",
                artifactHashes = alpha.PortableArtifacts.Select(artifact => new
                {
                    artifact.Path,
                    before = artifact.Sha256,
                    after = beta.PortableArtifacts.Single(other => other.Path == artifact.Path).Sha256,
                    matched = artifact.Sha256 == beta.PortableArtifacts.Single(other => other.Path == artifact.Path).Sha256
                }),
                resources = new { imported = 2, skipped = 0, failed = 0 },
                mappingDecisions = Array.Empty<object>(),
                continuity = new { tenantBoundaries = alpha.Boundaries.Count, jobs = 1, lineage = 1, dataQuality = 1, reports = 0 },
                negativeIsolation = new[]
                {
                    new { boundary = "orchestrator database", result = "Passed" },
                    new { boundary = "lineage and quality history", result = "Passed" },
                    new { boundary = "audit outbox", result = "Passed" },
                    new { boundary = "cache and artifact paths", result = "Passed" },
                    new { boundary = "path traversal", result = "Passed" },
                    new { boundary = "storage quota", result = "Passed" },
                    new { boundary = "duplicate tenant activation", result = "Passed" }
                },
                rollback = new { attempted = false, result = "NotApplicableToOnboardingProfileProof" },
                claims = new
                {
                    managedDedicated = "Passed",
                    shared = "NotCertified"
                }
            });
    }
}
