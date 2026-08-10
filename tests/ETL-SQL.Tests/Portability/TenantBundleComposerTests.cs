using System.Text.Json;
using ETL_SQL.App.Portability;
using ETL_SQL.Core.Portability;
using ETL_SQL.Core.Security;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// Composition of the unified bundle from the exports that already exist (TenantPortability.md §5).
/// The Portal side is faked because <c>ETL-SQL.App</c> cannot reference the Portal — it only ever
/// speaks HTTP to it — and composition logic should not need a running Portal to be testable.
/// </summary>
public sealed class TenantBundleComposerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"bundle-compose-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakePortal(
        PortalConfigurationPlan plan, string script, bool refuseAcknowledgement = false)
        : IPortalConfigurationSource
    {
        public string? AcknowledgedWith { get; private set; }

        public Task<PortalConfigurationPlan> GetPlanAsync(CancellationToken ct) => Task.FromResult(plan);

        public Task<string> GetScriptAsync(string acknowledgedPlanHash, CancellationToken ct)
        {
            AcknowledgedWith = acknowledgedPlanHash;
            return refuseAcknowledgement
                ? throw new InvalidOperationException("409: the configuration changed after review.")
                : Task.FromResult(script);
        }
    }

    private static PortalConfigurationPlan Plan() => new(
        "plan-hash-abc",
        ["sales-etl-credential"],
        ["report:legacy-crystal-import"],
        [new PortalContentManifestItem("dataset", "dataset:sales-snapshot", "warehouse", "transfer")],
        "tenant-acme");

    private TenantBundleCompositionRequest Request(string bundleRoot) => new(
        bundleRoot,
        "bundle-1",
        DateTimeOffset.Parse("2026-08-09T12:00:00Z"),
        "0.18.0",
        "Enterprise",
        "tenant-acme",
        "consistency-1",
        ArtifactFiles: [],
        ArtifactRoot: null);

    [Fact]
    public async Task ComposesPortalConfigurationOrchestratorPackageAndArtifactsIntoOneBundle()
    {
        var scripts = Path.Combine(_root, "src");
        Directory.CreateDirectory(scripts);
        var artifact = Path.Combine(scripts, "daily_load.etlsql");
        await File.WriteAllTextAsync(artifact, "SELECT 1 AS Value INTO #proof;");

        var bundle = Path.Combine(_root, "bundle");
        var portal = new FakePortal(Plan(), "CREATE FOLDER 'Sales';");

        var manifest = await TenantBundleComposer.ComposeAsync(portal,
            Request(bundle) with { ArtifactFiles = [artifact], ArtifactRoot = scripts });

        // All three sources land as components, and nothing invented a fourth format.
        Assert.Contains(manifest.Components, c => c.LogicalId == "catalog:portal-configuration");
        Assert.Contains(manifest.Components, c => c.LogicalId == "catalog:portal-configuration-plan");
        Assert.Contains(manifest.Components, c => c.LogicalId == "artifact:daily_load.etlsql");

        // The required secret travels as a binding requirement, never as a value.
        var binding = Assert.Single(manifest.RequiredBindings);
        Assert.Equal("SECRET:sales-etl-credential", binding.LogicalId);

        // What the Portal said would not travel is recorded as an exclusion with a remediation, not
        // dropped on the floor.
        Assert.Contains(manifest.Exclusions,
            e => e.LogicalId == "report:legacy-crystal-import" && e.Remediation is not null);
        Assert.Contains(manifest.Exclusions,
            e => e.LogicalId == "dataset:sales-snapshot" && e.Remediation!.Contains("warehouse"));

        var result = await TenantBundleValidator.ValidateAsync(bundle);
        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
    }

    [Fact]
    public async Task DownloadAcknowledgesTheReviewedPlanHash()
    {
        var portal = new FakePortal(Plan(), "CREATE FOLDER 'Sales';");

        await TenantBundleComposer.ComposeAsync(portal, Request(Path.Combine(_root, "bundle")));

        Assert.Equal("plan-hash-abc", portal.AcknowledgedWith);
    }

    [Fact]
    public async Task EnvironmentKeyMaterialNeverEntersPortableBundle()
    {
        var secret = Convert.ToBase64String(Enumerable.Repeat((byte)93, 32).ToArray());
        var binding = new EnvironmentKeyMaterialBinding(
            "ETLSQL_DATASET_KEY_V4",
            new("environment", "dataset-key", "tenant-acme", KeyPurpose.Dataset, "v4"));
        var provider = new EnvironmentKeyMaterialProvider([binding], _ => secret);
        using (var resolved = await provider.ResolveAsync(new("tenant-acme", KeyPurpose.Dataset)))
            Assert.Equal(32, resolved.Bytes.Length);

        var script = "-- key binding ETLSQL_DATASET_KEY_V4; material is host-local\nCREATE FOLDER 'Sales';";
        var bundle = Path.Combine(_root, "key-safe-bundle");
        await TenantBundleComposer.ComposeAsync(new FakePortal(Plan(), script), Request(bundle));

        foreach (var file in Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.DoesNotContain(secret, Convert.ToBase64String(bytes), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ConfigurationChangingMidExportFailsInsteadOfBundlingSomethingUnreviewed()
    {
        var portal = new FakePortal(Plan(), "CREATE FOLDER 'Sales';", refuseAcknowledgement: true);
        var bundle = Path.Combine(_root, "bundle");

        var ex = await Assert.ThrowsAsync<TenantBundleCompositionException>(
            () => TenantBundleComposer.ComposeAsync(portal, Request(bundle)));

        Assert.Contains("plan-hash-abc", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(bundle, TenantBundle.ManifestFileName)));
    }

    [Fact]
    public async Task TheExportPlanTravelsBesideTheScriptBecauseTheScriptDoesNotSayWhatWasOmitted()
    {
        var bundle = Path.Combine(_root, "bundle");
        await TenantBundleComposer.ComposeAsync(
            new FakePortal(Plan(), "CREATE FOLDER 'Sales';"), Request(bundle));

        var planJson = await File.ReadAllTextAsync(
            Path.Combine(bundle, "catalog", "portal-configuration-plan.json"));
        var plan = JsonSerializer.Deserialize<PortalConfigurationPlan>(
            planJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("plan-hash-abc", plan!.PlanHash);
        Assert.Contains("report:legacy-crystal-import", plan.Skipped);
    }

    [Fact]
    public async Task AMissingPortableArtifactFailsRatherThanQuietlyShrinkingTheBundle()
    {
        var bundle = Path.Combine(_root, "bundle");
        var request = Request(bundle) with
        {
            ArtifactFiles = [Path.Combine(_root, "src", "does_not_exist.etlsql")]
        };

        var ex = await Assert.ThrowsAsync<TenantBundleCompositionException>(
            () => TenantBundleComposer.ComposeAsync(new FakePortal(Plan(), "x"), request));

        Assert.Contains("does_not_exist.etlsql", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaasExportRefusesCallerTenantThatDoesNotMatchThePortalHost()
    {
        var request = Request(Path.Combine(_root, "bundle")) with
        {
            SourceProfile = "SaaS",
            TenantExportIdentity = "tenant-beta"
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            TenantBundleComposer.ComposeAsync(new FakePortal(Plan(), "x"), request));

        Assert.Contains("cannot widen scope", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(request.BundleRoot, TenantBundle.ManifestFileName)));
    }

    [Fact]
    public async Task SaasExportRefusesPortalWithoutServerOwnedTenantIdentity()
    {
        var request = Request(Path.Combine(_root, "bundle")) with { SourceProfile = "SaaS" };
        var plan = Plan() with { TenantExportIdentity = null };

        var ex = await Assert.ThrowsAsync<TenantBundleCompositionException>(() =>
            TenantBundleComposer.ComposeAsync(new FakePortal(plan, "x"), request));

        Assert.Contains("server-owned tenant identity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposedSaasBundleIsEncryptedAndSigned()
    {
        var keys = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keys);
        var tenantPub = Path.Combine(keys, "tenant-public.asc");
        var tenantPriv = Path.Combine(keys, "tenant-private.asc");
        var opPub = Path.Combine(keys, "operator-public.asc");
        var opPriv = Path.Combine(keys, "operator-private.asc");
        using (var pgp = new PgpCore.PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(tenantPub), new FileInfo(tenantPriv),
                "tenant@example.test", string.Empty, 1024);
            await pgp.GenerateKeyAsync(new FileInfo(opPub), new FileInfo(opPriv),
                "operator@example.test", string.Empty, 1024);
        }

        var bundle = Path.Combine(_root, "bundle");
        var request = Request(bundle) with
        {
            SourceProfile = "SaaS",
            RecipientPublicKeyFile = tenantPub,
            SigningPrivateKeyFile = opPriv
        };

        var manifest = await TenantBundleComposer.ComposeAsync(
            new FakePortal(Plan(), "CREATE FOLDER 'Sales';"), request);

        Assert.True(manifest.Encryption!.Encrypted);
        Assert.Equal(TenantBundle.SignatureFileName, manifest.SignatureFile);

        var result = await TenantBundleValidator.ValidateAsync(bundle,
            new TenantBundleValidator.Options(opPub, RequireSignature: true));
        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));

        // The Portal script is tenant configuration and must not be readable from the bundle alone.
        var stored = await File.ReadAllTextAsync(
            Path.Combine(bundle, "catalog", "portal-configuration.etlsql"));
        Assert.DoesNotContain("CREATE FOLDER", stored, StringComparison.Ordinal);
    }
}
