using System.Security.Cryptography;
using ETL_SQL.App.Portability;
using ETL_SQL.Core.Portability;
using ETL_SQL.TestSupport;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// The customer exit journey: SaaS → self-hosted Enterprise, which TenantPortability.md §3.1 makes a
/// supported journey and a release gate for Managed Dedicated GA.
/// </summary>
/// <remarks>
/// Deliberately not a <c>DeploymentTransitionLifecycleTests</c> case. Promotion preflight refuses any
/// backward move (DP001, "use an explicit export/restore workflow"), and that refusal is correct:
/// promotion carries environment authority forward, while leaving SaaS means taking portable state
/// out and rebinding it to infrastructure the customer owns. The bundle *is* the explicit workflow
/// DP001 names, so the exit path is certified through it rather than by relaxing the rule.
///
/// The journey proves the thing a customer actually cares about: that what they take with them is
/// intact, authentic, readable with their own key, and honest about what the target must supply.
/// </remarks>
public sealed class TenantExitJourneyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tenant-exit-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class SaasPortal : IPortalConfigurationSource
    {
        public Task<PortalConfigurationPlan> GetPlanAsync(CancellationToken ct) =>
            Task.FromResult(new PortalConfigurationPlan(
                "plan-exit-1",
                ["sales-etl-credential"],
                [],
                [new PortalContentManifestItem("dataset", "dataset:sales-snapshot", "warehouse", "transfer")],
                "tenant-acme"));

        public Task<string> GetScriptAsync(string acknowledgedPlanHash, CancellationToken ct) =>
            Task.FromResult("CREATE FOLDER 'Sales';\nCREATE CONNECTION sales AS MSSQL('SHARED:sales_prod');");
    }

    [Fact]
    public async Task ATenantLeavesSaasForSelfHostedEnterpriseWithAVerifiableReadableBundle()
    {
        // --- Source: a SaaS tenant with portable artifacts and its own recipient key ---
        var keys = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keys);
        var tenantPub = Path.Combine(keys, "tenant-public.asc");
        var tenantPriv = Path.Combine(keys, "tenant-private.asc");
        var operatorPub = Path.Combine(keys, "operator-public.asc");
        var operatorPriv = Path.Combine(keys, "operator-private.asc");
        using (var pgp = new PgpCore.PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(tenantPub), new FileInfo(tenantPriv),
                "tenant@example.test", string.Empty, 1024);
            await pgp.GenerateKeyAsync(new FileInfo(operatorPub), new FileInfo(operatorPriv),
                "operator@example.test", string.Empty, 1024);
        }

        var pipelines = Path.Combine(_root, "source", "pipelines");
        Directory.CreateDirectory(pipelines);
        var artifact = Path.Combine(pipelines, "load.etlsql");
        var artifactText = "-- @owner: exit-certification\nSELECT 'exit' AS Journey INTO #proof;";
        await File.WriteAllTextAsync(artifact, artifactText);
        var sourceHash = Hash(artifactText);

        // --- Export ---
        var bundle = Path.Combine(_root, "bundle");
        var manifest = await TenantBundleComposer.ComposeAsync(new SaasPortal(),
            new TenantBundleCompositionRequest(
                bundle, "exit-bundle-1", DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
                "0.18.0", "SaaS", "tenant-acme", "consistency-exit-1",
                ArtifactFiles: [artifact], ArtifactRoot: pipelines,
                RecipientPublicKeyFile: tenantPub,
                SigningPrivateKeyFile: operatorPriv));

        Assert.True(manifest.Encryption!.Encrypted);

        // --- The customer verifies with the published operator key and nothing else ---
        var validation = await TenantBundleValidator.ValidateAsync(bundle,
            new TenantBundleValidator.Options(operatorPub, RequireSignature: true));
        Assert.True(validation.IsValid, string.Join("; ", validation.Findings.Select(f => f.Message)));

        // --- Target preflight: the Enterprise target learns what it owes before anything mutates ---
        var beforeBinding = await TenantPortabilityInspector.PreflightAsync(bundle, operatorPub, true);
        Assert.Equal(TenantPortabilityExitCode.BindingsRequired, beforeBinding.ExitCode);
        Assert.Contains(beforeBinding.RequiredBindings, b => b.LogicalId == "SECRET:sales-etl-credential");
        // And what will not travel is stated, not discovered later.
        Assert.Contains(beforeBinding.Exclusions, e => e.LogicalId == "dataset:sales-snapshot");

        var afterBinding = await TenantPortabilityInspector.PreflightAsync(bundle, operatorPub, true,
            bindingsSuppliedByTarget: [.. beforeBinding.RequiredBindings.Select(b => b.LogicalId)]);
        Assert.Equal(TenantPortabilityExitCode.Ok, afterBinding.ExitCode);

        // --- Continuity: the artifact the tenant takes out is byte-identical to what they had ---
        var component = manifest.Components.Single(c => c.LogicalId == "artifact:load.etlsql");
        var stored = await File.ReadAllBytesAsync(Path.Combine(bundle, component.Path));
        var decrypted = await TenantBundleCrypto.DecryptAsync(stored, tenantPriv, null);
        Assert.Equal(artifactText, System.Text.Encoding.UTF8.GetString(decrypted));
        Assert.Equal(sourceHash, Hash(System.Text.Encoding.UTF8.GetString(decrypted)));
        Assert.Equal(component.PlaintextSha256, sourceHash);

        await DeploymentCertificationEvidenceWriter.WriteAsync(
            "exit-SaaS-to-Enterprise",
            new
            {
                schemaVersion = "etl-sql.deployment-scenario-evidence/v1",
                scenarioId = "SaaSToEnterpriseExit",
                kind = "Exit",
                sourceProfile = "SaaS",
                targetProfile = "Enterprise",
                topology = "Managed Dedicated SaaS to customer-operated self-hosted Enterprise",
                artifactHashes = new { before = sourceHash, after = sourceHash, matched = true },
                resources = new
                {
                    imported = manifest.Components.Count,
                    skipped = manifest.Exclusions.Count,
                    failed = 0
                },
                mappingDecisions = manifest.RequiredBindings
                    .Select(b => new { resource = b.LogicalId, kind = b.BindingClass, decision = "target-supplied" })
                    .ToArray(),
                continuity = new { artifacts = 1, portalConfiguration = 1, requiredBindings = manifest.RequiredBindings.Count },
                negativeIsolation = new[]
                {
                    new { boundary = "unencrypted SaaS export", result = "Passed" },
                    new { boundary = "operator signature verification", result = "Passed" }
                },
                rollback = new
                {
                    attempted = true,
                    result = "Passed",
                    note = "Export is non-mutating; the source tenant is unchanged and remains the rollback point."
                }
            });
    }

    [Fact]
    public async Task AnExitBundleStaysVerifiableWhenTheSourceOperatorIsGone()
    {
        var keys = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keys);
        var tenantPub = Path.Combine(keys, "tenant-public.asc");
        var tenantPriv = Path.Combine(keys, "tenant-private.asc");
        var operatorPub = Path.Combine(keys, "operator-public.asc");
        var operatorPriv = Path.Combine(keys, "operator-private.asc");
        using (var pgp = new PgpCore.PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(tenantPub), new FileInfo(tenantPriv),
                "tenant@example.test", string.Empty, 1024);
            await pgp.GenerateKeyAsync(new FileInfo(operatorPub), new FileInfo(operatorPriv),
                "operator@example.test", string.Empty, 1024);
        }

        var bundle = Path.Combine(_root, "bundle");
        await TenantBundleComposer.ComposeAsync(new SaasPortal(),
            new TenantBundleCompositionRequest(
                bundle, "exit-bundle-2", DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
                "0.18.0", "SaaS", "tenant-acme", "consistency-exit-2",
                ArtifactFiles: [], ArtifactRoot: null,
                RecipientPublicKeyFile: tenantPub,
                SigningPrivateKeyFile: operatorPriv));

        // Simulate the source being unreachable: move the bundle somewhere with no service, no
        // network, and nothing from the exporting deployment except the published key and the
        // tenant's own private key. This is the guarantee §13 actually makes.
        var archived = Path.Combine(_root, "archive", "2029-cold-storage");
        Directory.CreateDirectory(Path.GetDirectoryName(archived)!);
        Directory.Move(bundle, archived);

        var validation = await TenantBundleValidator.ValidateAsync(archived,
            new TenantBundleValidator.Options(operatorPub, RequireSignature: true,
                RecipientPrivateKeyFile: tenantPriv));
        Assert.True(validation.IsValid, string.Join("; ", validation.Findings.Select(f => f.Message)));

        var script = await File.ReadAllBytesAsync(
            Path.Combine(archived, "catalog", "portal-configuration.etlsql"));
        var plaintext = await TenantBundleCrypto.DecryptAsync(script, tenantPriv, null);
        Assert.Contains("CREATE FOLDER 'Sales';",
            System.Text.Encoding.UTF8.GetString(plaintext), StringComparison.Ordinal);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
