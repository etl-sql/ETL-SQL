using ETL_SQL.App.Portability;
using ETL_SQL.Core.Portability;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// Import (TenantPortability.md §11). The Portal and Orchestrator targets are faked so the ordering
/// and refusal rules can be asserted without a Portal or an engine — those rules are the substance,
/// and they are exactly what a mocked-out integration test would hide.
/// </summary>
public sealed class TenantBundleImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tenant-import-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakePortal(params PortalPlanEntry[] plan) : IPortalConfigurationTarget
    {
        public bool Applied { get; private set; }
        public string? AppliedScript { get; private set; }

        public Task<IReadOnlyList<PortalPlanEntry>> PlanAsync(
            string script, IReadOnlyDictionary<string, string> bindings, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PortalPlanEntry>>(plan);

        public Task ApplyAsync(string script, IReadOnlyDictionary<string, string> bindings, CancellationToken ct)
        {
            Applied = true;
            AppliedScript = script;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOrchestrator : IOrchestratorPackageTarget
    {
        public bool? LeftDisabled { get; private set; }

        public Task<int> ImportAsync(
            OrchestratorPromotionPackageService.Package package, bool leaveDisabled, CancellationToken ct)
        {
            LeftDisabled = leaveDisabled;
            return Task.FromResult(package.Jobs.Count);
        }
    }

    private static TenantBundleRequest Request(string? recipientKey = null) => new(
        "bundle-1",
        DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
        "0.18.0",
        recipientKey is null ? "Enterprise" : "SaaS",
        "tenant-acme",
        TenantBundleExportMode.ConfigurationAndArtifacts,
        "consistency-1",
        [new TenantBundlePayload("catalog:portal-configuration", "catalog", "application/x-etlsql",
            "catalog/portal-configuration.etlsql", "CREATE FOLDER 'Sales';")],
        [new TenantBundleRequiredBinding("SECRET:sales", "secret", "Provision it.")],
        [],
        RecipientPublicKeyFile: recipientKey);

    private static TenantImportOptions Options(
        TenantImportCollisionPolicy policy = TenantImportCollisionPolicy.Fail) =>
        new(new Dictionary<string, string> { ["SECRET:sales"] = "provisioned" }, CollisionPolicy: policy);

    [Fact]
    public async Task NothingIsAppliedWhenTheTargetHasNotSuppliedEveryBinding()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var portal = new FakePortal();

        var result = await TenantBundleImporter.ImportAsync(_root,
            new TenantImportOptions(new Dictionary<string, string>()), portal);

        Assert.Equal(TenantPortabilityExitCode.BindingsRequired, result.ExitCode);
        Assert.False(result.Applied);
        Assert.False(portal.Applied);
        Assert.Contains("SECRET:sales", result.RefusalReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATamperedBundleNeverReachesTheTarget()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        await File.WriteAllTextAsync(
            Path.Combine(_root, "catalog", "portal-configuration.etlsql"), "DROP TABLE customers;");
        var portal = new FakePortal();

        var result = await TenantBundleImporter.ImportAsync(_root, Options(), portal);

        Assert.Equal(TenantPortabilityExitCode.BundleInvalid, result.ExitCode);
        Assert.False(portal.Applied);
    }

    [Fact]
    public async Task ACollisionRefusesByDefaultAndNothingIsApplied()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var portal = new FakePortal(new PortalPlanEntry("Folder", "Sales", "Collision"));

        var result = await TenantBundleImporter.ImportAsync(_root, Options(), portal);

        Assert.False(result.Applied);
        Assert.False(portal.Applied);
        Assert.Contains("Folder 'Sales'", result.RefusalReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProceedAppliesDespiteACollision()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var portal = new FakePortal(new PortalPlanEntry("Folder", "Sales", "Collision"));

        var result = await TenantBundleImporter.ImportAsync(
            _root, Options(TenantImportCollisionPolicy.Proceed), portal);

        Assert.True(result.Applied);
        Assert.True(portal.Applied);
    }

    [Fact]
    public async Task ACleanPlanAppliesTheScriptTheBundleActuallyCarries()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var portal = new FakePortal(new PortalPlanEntry("Folder", "Sales", "Create"));

        var result = await TenantBundleImporter.ImportAsync(_root, Options(), portal);

        Assert.Equal(TenantPortabilityExitCode.Ok, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Equal("CREATE FOLDER 'Sales';", portal.AppliedScript);
    }

    [Fact]
    public async Task ADryRunComputesThePlanWithoutApplyingIt()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var portal = new FakePortal(new PortalPlanEntry("Folder", "Sales", "Create"));

        var result = await TenantBundleImporter.ImportAsync(_root,
            Options() with { DryRun = true }, portal);

        Assert.Equal(TenantPortabilityExitCode.Ok, result.ExitCode);
        Assert.False(result.Applied);
        Assert.False(portal.Applied);
        Assert.Single(result.Plan);
    }

    [Fact]
    public async Task OrchestratorObjectsAlwaysArriveDisabled()
    {
        var package = new OrchestratorPromotionPackageService.Package(
            "etl-sql.orchestrator-promotion/v1", DateTimeOffset.UtcNow,
            [], [], [], [], [], [], [], [], []);
        var request = Request() with
        {
            Payloads =
            [
                new TenantBundlePayload("catalog:portal-configuration", "catalog", "application/x-etlsql",
                    "catalog/portal-configuration.etlsql", "CREATE FOLDER 'Sales';"),
                new TenantBundlePayload("catalog:orchestrator-promotion", "catalog", "application/json",
                    "catalog/orchestrator-promotion.json",
                    System.Text.Json.JsonSerializer.Serialize(package))
            ]
        };
        await TenantBundleWriter.WriteAsync(_root, request);
        var orchestrator = new FakeOrchestrator();

        await TenantBundleImporter.ImportAsync(_root, Options(),
            new FakePortal(new PortalPlanEntry("Folder", "Sales", "Create")), orchestrator);

        // Not configurable, and that is the point: an import must not start running the tenant's
        // pipelines against a freshly bound environment.
        Assert.True(orchestrator.LeftDisabled);
    }

    [Fact]
    public async Task AnEncryptedBundleWithoutTheTenantKeyCannotBeImported()
    {
        var keys = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keys);
        var pub = Path.Combine(keys, "tenant-public.asc");
        var priv = Path.Combine(keys, "tenant-private.asc");
        using (var pgp = new PgpCore.PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(pub), new FileInfo(priv),
                "tenant@example.test", string.Empty, 1024);
        }

        var bundle = Path.Combine(_root, "bundle");
        await TenantBundleWriter.WriteAsync(bundle, Request(pub));

        var ex = await Assert.ThrowsAsync<TenantBundleCompositionException>(() =>
            TenantBundleImporter.ImportAsync(bundle, Options(), new FakePortal()));

        Assert.Contains("no private key was supplied", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEncryptedBundleImportsWithTheTenantKey()
    {
        var keys = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keys);
        var pub = Path.Combine(keys, "tenant-public.asc");
        var priv = Path.Combine(keys, "tenant-private.asc");
        using (var pgp = new PgpCore.PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(pub), new FileInfo(priv),
                "tenant@example.test", string.Empty, 1024);
        }

        var bundle = Path.Combine(_root, "bundle");
        await TenantBundleWriter.WriteAsync(bundle, Request(pub));
        var portal = new FakePortal(new PortalPlanEntry("Folder", "Sales", "Create"));

        var result = await TenantBundleImporter.ImportAsync(bundle,
            Options() with { RecipientPrivateKeyFile = priv }, portal);

        Assert.True(result.Applied);
        // Decrypted to exactly what was exported, verified against the manifest's plaintext hash.
        Assert.Equal("CREATE FOLDER 'Sales';", portal.AppliedScript);
    }

    [Fact]
    public async Task DeltaWithWrongTargetBaseFailsBeforePlanningOrMutation()
    {
        var payload = new TenantBundlePayload("catalog:portal-configuration", "catalog",
            "application/x-etlsql", "catalog/portal-configuration.etlsql", "CREATE FOLDER 'Sales';");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload.Content))
            .ToLowerInvariant();
        var point = TenantExportConsistencyCoordinator.Declare("tenant-acme",
            [new("portal", "revision-2")], [], 0, false, DateTimeOffset.UtcNow);
        var request = Request() with
        {
            ExportMode = TenantBundleExportMode.IncrementalDelta,
            ConsistencyPoint = point.Digest,
            Payloads = [payload],
            DeclaredConsistencyPoint = point,
            Inventory = [new("catalog:portal-configuration", "catalog",
                TenantInventoryDisposition.Included, payload.Content.Length, hash, "user:owner", [],
                null, null, "tenant-acme")],
            BaseConsistencyPointDigest = "certified-base"
        };
        await TenantBundleWriter.WriteAsync(_root, request);
        var portal = new FakePortal();

        var result = await TenantBundleImporter.ImportAsync(_root,
            Options() with { ExpectedBaseConsistencyPointDigest = "different-base" }, portal);

        Assert.False(result.Applied);
        Assert.False(portal.Applied);
        Assert.Empty(result.Plan);
        Assert.Contains("out-of-order", result.RefusalReason!, StringComparison.Ordinal);
    }
}
