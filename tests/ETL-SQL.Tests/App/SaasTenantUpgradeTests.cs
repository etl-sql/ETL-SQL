using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Tests.App;

[Trait("Category", "DeploymentProfile")]
public sealed class SaasTenantUpgradeTests : IDisposable
{
    private static string Release => SaasTenantUpgradeService.CurrentReleaseId;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"saas-upgrade-{Guid.NewGuid():N}");

    public SaasTenantUpgradeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Upgrade_CancelsQueuedWorkAppliesSignedCapacityAndResumesFencedJobs()
    {
        var tenantRoot = await CreateBoundaryAsync("tenant-alpha");
        var store = Store(tenantRoot);
        await store.InitializeAsync();
        await store.SaveJobAsync(Job("daily", "tenant-alpha", enabled: true));
        var ledger = Ledger(tenantRoot);
        var tenant = TenantContext.FromHostConfiguration("tenant-alpha");
        Assert.True(await ledger.EnqueueAsync("queued-alpha", tenant, Policy("tenant-alpha", 3)));

        var now = DateTimeOffset.UtcNow;
        var result = await SaasTenantUpgradeService.UpgradeAsync(
            Context(tenantRoot, "tenant-alpha", Release, 3, 4096, 12),
            Authority("tenant-alpha", "change-upgrade-1", Release, 3, 4096, 12, now),
            now);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.CancelledQueuedAdmissions);
        Assert.Empty(result.BlockingAdmissions);
        Assert.Equal(SandboxAdmissionState.Cancelled, (await ledger.ReadAsync("queued-alpha"))!.State);
        Assert.True((await store.GetJobAsync((string?)null, "daily"))!.IsEnabled);

        var config = await ReadConfigAsync(tenantRoot);
        Assert.Equal(Release, (string?)config["saasTenant"]?["deployment"]?["activeRelease"]);
        Assert.Equal(3, (int?)config["saasTenant"]?["resources"]?["maxConcurrentJobs"]);
        Assert.Equal(4096, (int?)config["saasTenant"]?["resources"]?["maxStorageMb"]);
        Assert.Equal(12, (int?)config["portal"]?["resources"]?["maxConcurrentReportExecutions"]);
        Assert.Equal(3, (int?)config["orchestration"]?["sandboxAdmission"]?["poolCapacities"]?["dedicated-tenant-alpha"]);

        var manifest = await ReadManifestAsync(tenantRoot);
        Assert.Equal(3, manifest.MaxConcurrentJobs);
        Assert.Equal(4096, manifest.MaxStorageMb);
        Assert.Equal(12, manifest.MaxReportSessions);
        Assert.True(File.Exists(Path.Combine(
            tenantRoot, "imports", "upgrades", result.OperationId, "tenant-manifest.before.json")));

        var repeated = await SaasTenantUpgradeService.UpgradeAsync(
            Context(tenantRoot, "tenant-alpha", Release, 3, 4096, 12),
            Authority("tenant-alpha", "change-upgrade-1", Release, 3, 4096, 12, now),
            now);
        Assert.Equal(result.OperationId, repeated.OperationId);
        Assert.Equal("Completed", repeated.Status);
        Assert.Equal(result.TargetRelease, repeated.TargetRelease);
        Assert.Equal(result.CompletedUtc, repeated.CompletedUtc);
    }

    [Fact]
    public async Task Upgrade_PreflightDoesNotFenceOrCancelWorkOrWriteReceipt()
    {
        var tenantRoot = await CreateBoundaryAsync("tenant-alpha");
        var store = Store(tenantRoot);
        await store.InitializeAsync();
        await store.SaveJobAsync(Job("daily", "tenant-alpha", enabled: true));
        var ledger = Ledger(tenantRoot);
        Assert.True(await ledger.EnqueueAsync(
            "queued-alpha", TenantContext.FromHostConfiguration("tenant-alpha"),
            Policy("tenant-alpha", 2)));
        var now = DateTimeOffset.UtcNow;

        var preflight = await SaasTenantUpgradeService.UpgradeAsync(
            Context(tenantRoot, "tenant-alpha", Release, 3, 4096, 12),
            Authority("tenant-alpha", "change-upgrade-preflight", Release, 3, 4096, 12, now),
            now, execute: false);

        Assert.Equal("Preflight", preflight.Status);
        Assert.True((await store.GetJobAsync((string?)null, "daily"))!.IsEnabled);
        Assert.Equal(SandboxAdmissionState.Queued, (await ledger.ReadAsync("queued-alpha"))!.State);
        Assert.Equal("unversioned", (string?)(await ReadConfigAsync(tenantRoot))["saasTenant"]?["deployment"]?["activeRelease"]);
        Assert.Empty(Directory.GetFiles(
            Path.Combine(tenantRoot, "queues", "audit"), "tenant-upgrade-*.json"));
    }

    [Fact]
    public async Task Upgrade_FencesThenWaitsForActiveAndRetainedAdmissions()
    {
        var tenantRoot = await CreateBoundaryAsync("tenant-alpha");
        var store = Store(tenantRoot);
        await store.InitializeAsync();
        await store.SaveJobAsync(Job("daily", "tenant-alpha", enabled: true));
        var ledger = Ledger(tenantRoot);
        var tenant = TenantContext.FromHostConfiguration("tenant-alpha");
        Assert.True(await ledger.EnqueueAsync("active-alpha", tenant, Policy("tenant-alpha", 2)));
        var fence = await ledger.TryActivateAsync(
            "active-alpha", "node-a", 2, TimeSpan.FromMinutes(5));
        Assert.NotNull(fence);

        var now = DateTimeOffset.UtcNow;
        var context = Context(tenantRoot, "tenant-alpha", Release, 2, 2048, 9);
        var authority = Authority("tenant-alpha", "change-upgrade-2", Release, 2, 2048, 9, now);
        var draining = await SaasTenantUpgradeService.UpgradeAsync(context, authority, now);

        Assert.Equal("Draining", draining.Status);
        Assert.Equal(["active-alpha"], draining.BlockingAdmissions);
        Assert.False((await store.GetJobAsync((string?)null, "daily"))!.IsEnabled);
        Assert.Equal("unversioned", (string?)(await ReadConfigAsync(tenantRoot))["saasTenant"]?["deployment"]?["activeRelease"]);

        Assert.True(await ledger.TryRetainAsync(
            "active-alpha", "node-a", fence!.Value, "runtime detach uncertain"));
        var retained = await SaasTenantUpgradeService.UpgradeAsync(context, authority, now);
        Assert.Equal("Draining", retained.Status);
        Assert.Equal(["active-alpha"], retained.BlockingAdmissions);

        Assert.True(await ledger.ReleaseRetainedAsync("active-alpha", fence.Value));
        var completed = await SaasTenantUpgradeService.UpgradeAsync(context, authority, now);
        Assert.Equal("Completed", completed.Status);
        Assert.True((await store.GetJobAsync((string?)null, "daily"))!.IsEnabled);
        Assert.Equal(Release, (string?)(await ReadConfigAsync(tenantRoot))["saasTenant"]?["deployment"]?["activeRelease"]);
    }

    [Fact]
    public async Task Upgrade_CutoverFailureRestoresConfigurationManifestAndScheduling()
    {
        var tenantRoot = await CreateBoundaryAsync("tenant-alpha");
        var store = Store(tenantRoot);
        await store.InitializeAsync();
        await store.SaveJobAsync(Job("daily", "tenant-alpha", enabled: true));
        var configPath = Path.Combine(tenantRoot, "config", "appsettings.tenant.json");
        var manifestPath = Path.Combine(tenantRoot, "tenant-manifest.json");
        var originalConfig = await File.ReadAllBytesAsync(configPath);
        var originalManifest = await File.ReadAllBytesAsync(manifestPath);
        var now = DateTimeOffset.UtcNow;
        var context = Context(tenantRoot, "tenant-alpha", Release, 7, 8192, 15);
        var authority = Authority("tenant-alpha", "change-upgrade-fault", Release, 7, 8192, 15, now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaasTenantUpgradeService.UpgradeAsync(
                context, authority, now, cutoverFault: _ =>
                    Task.FromException(new InvalidOperationException("injected cutover failure"))));

        Assert.Equal(originalConfig, await File.ReadAllBytesAsync(configPath));
        Assert.Equal(originalManifest, await File.ReadAllBytesAsync(manifestPath));
        Assert.True((await store.GetJobAsync((string?)null, "daily"))!.IsEnabled);
        var receipt = Assert.Single(Directory.GetFiles(
            Path.Combine(tenantRoot, "queues", "audit"), "tenant-upgrade-*.json"));
        Assert.Equal("Failed", (string?)JsonNode.Parse(await File.ReadAllTextAsync(receipt))?["status"]);

        var retried = await SaasTenantUpgradeService.UpgradeAsync(context, authority, now);
        Assert.Equal("Completed", retried.Status);
        Assert.Equal(Release, (string?)(await ReadConfigAsync(tenantRoot))["saasTenant"]?["deployment"]?["activeRelease"]);
    }

    [Fact]
    public async Task Upgrade_RefusesForeignBoundaryPoolAndCallerAssertions()
    {
        var tenantRoot = await CreateBoundaryAsync("tenant-alpha");
        var ledger = Ledger(tenantRoot);
        Assert.True(await ledger.EnqueueAsync(
            "foreign", TenantContext.FromHostConfiguration("tenant-beta"), Policy("tenant-alpha", 2)));
        var now = DateTimeOffset.UtcNow;
        var context = Context(tenantRoot, "tenant-alpha", Release, 2, 2048, 9);
        var authority = Authority("tenant-alpha", "change-upgrade-foreign", Release, 2, 2048, 9, now);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            SaasTenantUpgradeService.UpgradeAsync(context, authority, now, execute: false));

        var policy = EffectivePolicy(new SaasUpgradeAuthorizationPolicySection
        {
            Enabled = true,
            TenantId = "tenant-alpha",
            OperatorPrincipal = "operator@platform.test",
            AuthorizationReference = "change-assertions",
            Reason = "upgrade",
            TargetRelease = Release,
            MaxConcurrentJobs = 2,
            MaxStorageMb = 2048,
            MaxReportSessions = 9,
            ExpiresUtc = now.AddMinutes(10)
        });
        var wrongTenant = Context(tenantRoot, "tenant-beta", Release, 2, 2048, 9);
        Assert.Throws<UnauthorizedAccessException>(() =>
            SaasTenantUpgradeService.ResolveAuthorizedContext(wrongTenant, policy, now));
        var wrongCapacity = Context(tenantRoot, "tenant-alpha", Release, 99, 2048, 9);
        Assert.Throws<UnauthorizedAccessException>(() =>
            SaasTenantUpgradeService.ResolveAuthorizedContext(wrongCapacity, policy, now));

        var wrongReleasePolicy = EffectivePolicy(policy.Document!.SaasUpgrade with
        {
            TargetRelease = Release + "-not-running"
        });
        var wrongRelease = Context(tenantRoot, "tenant-alpha", Release + "-not-running", 2, 2048, 9);
        Assert.Throws<UnauthorizedAccessException>(() =>
            SaasTenantUpgradeService.ResolveAuthorizedContext(wrongRelease, wrongReleasePolicy, now));
    }

    [Fact]
    public async Task Upgrade_RefusesConcurrentBoundaryMutation()
    {
        var tenantRoot = await CreateBoundaryAsync("tenant-alpha");
        await using var held = new FileStream(
            Path.Combine(tenantRoot, "queues", "tenant-upgrade.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaasTenantUpgradeService.UpgradeAsync(
                Context(tenantRoot, "tenant-alpha", Release, 2, 2048, 8),
                Authority("tenant-alpha", "change-upgrade-lock", Release, 2, 2048, 8, now),
                now, execute: false));
    }

    private async Task<string> CreateBoundaryAsync(string tenant)
    {
        var source = Path.Combine(_root, $"source-{tenant}");
        var output = Path.Combine(_root, "tenants");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "portable.etlsql"), "SELECT 1 INTO #stage;");
        await File.WriteAllTextAsync(Path.Combine(source, "etlsql-policy.json"), "{}");
        var now = DateTimeOffset.UtcNow;
        await SaasTenantOnboardingService.OnboardAsync(new CliContext
        {
            SaasTenantId = tenant,
            SaasSourceProfile = "Solo",
            PromotionSource = source,
            SaasOutputRoot = output,
            SaasMaxConcurrentJobs = 2,
            SaasMaxStorageMb = 2048,
            SaasMaxReportSessions = 8
        }, TenantContext.FromPlatformGrant(
            PlatformAccessGrant.Issue(tenant, "provisioner@platform.test", $"onboard-{tenant}",
                "test onboarding", now.AddHours(1), now), now));
        return Path.Combine(output, tenant);
    }

    private static CliContext Context(
        string tenantRoot, string tenant, string release, int jobs, int storage, int reports) => new()
    {
        SaasTenantId = tenant,
        SaasUpgradeTenantRoot = tenantRoot,
        SaasUpgradeTargetRelease = release,
        SaasUpgradeMaxConcurrentJobs = jobs,
        SaasUpgradeMaxStorageMb = storage,
        SaasUpgradeMaxReportSessions = reports,
        SaasUpgradeExecute = true
    };

    private static SaasTenantUpgradeService.UpgradeAuthority Authority(
        string tenant, string reference, string release, int jobs, int storage, int reports,
        DateTimeOffset now) => new(
        TenantContext.FromPlatformGrant(
            PlatformAccessGrant.Issue(tenant, "operator@platform.test", reference,
                "upgrade dedicated tenant", now.AddHours(1), now), now),
        release, jobs, storage, reports);

    private static ResolvedSandboxAdmissionPolicy Policy(string tenant, int max) => new()
    {
        PoolId = $"dedicated-{tenant}",
        TenantWeight = 1,
        MaxConcurrentAttempts = max,
        MaxQueuedAttempts = 10
    };

    private static JobDefinition Job(string name, string tenant, bool enabled) => new(
        name, "SELECT 1;", 1, "HOUR", null, null, null,
        IsEnabled: enabled, TenantId: tenant);

    private static SQLiteJobHistoryStore Store(string tenantRoot) =>
        new(Path.Combine(tenantRoot, "databases", "orchestrator.db"));

    private static RelationalSandboxAdmissionLedger Ledger(string tenantRoot) =>
        new(new SqliteOrchestratorDialect(
            $"Data Source={Path.Combine(tenantRoot, "databases", "orchestrator.db")}"));

    private static async Task<JsonObject> ReadConfigAsync(string tenantRoot) =>
        JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(tenantRoot, "config", "appsettings.tenant.json")))!.AsObject();

    private static async Task<SaasTenantOnboardingService.Manifest> ReadManifestAsync(string tenantRoot)
    {
        await using var stream = File.OpenRead(Path.Combine(tenantRoot, "tenant-manifest.json"));
        return (await JsonSerializer.DeserializeAsync<SaasTenantOnboardingService.Manifest>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private static EffectiveEnterprisePolicy EffectivePolicy(SaasUpgradeAuthorizationPolicySection section) => new(
        true, true, "Available", "v1", "test", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow,
        new OrganizationPolicyDocument { SaasUpgrade = section },
        new Dictionary<string, string?>());
}
