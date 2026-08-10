using System.Text;
using ETL_SQL.Core.Portability;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// Covers the minimum configuration/artifact bundle from
/// <c>docs/architecture/TenantPortability.md</c> §5, and the reference validator from §16 that lets a
/// customer verify an export without contacting the source operator.
/// </summary>
public sealed class TenantBundleTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"tenant-bundle-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static TenantBundleRequest Request(string bundleId = "bundle-1") => new(
        bundleId,
        DateTimeOffset.Parse("2026-08-09T12:00:00Z"),
        "0.18.0",
        "Enterprise",
        "tenant-acme",
        TenantBundleExportMode.ConfigurationAndArtifacts,
        "consistency-2026-08-09T12:00:00Z",
        [
            new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
                "artifacts/daily_load.etlsql", "SELECT 1 AS Value INTO #proof;"),
            new TenantBundlePayload("catalog:portal-configuration", "catalog", "application/json",
                "catalog/portal-configuration.json", "{\"folders\":[]}"),
            new TenantBundlePayload("catalog:orchestrator-promotion", "catalog", "application/json",
                "catalog/orchestrator-promotion.json",
                "{\"SchemaVersion\":\"etl-sql.orchestrator-promotion/v1\"}")
        ],
        [
            new TenantBundleRequiredBinding("SHARED:sales_prod", "connection",
                "Target must bind this alias to a connection it owns.")
        ],
        [
            new TenantBundleExclusion("secret:sales-etl-credential", "secret",
                "Resolved secret values are never portable.",
                "Provision the secret at the target and rebind the reference.")
        ]);

    [Fact]
    public async Task WrittenBundleRoundTripsThroughTheStandaloneValidator()
    {
        var written = await TenantBundleWriter.WriteAsync(_root, Request());
        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.NotNull(result.Manifest);
        Assert.Equal(TenantBundle.SchemaVersion, result.Manifest!.SchemaVersion);
        Assert.Equal(3, result.Manifest.Components.Count);
        Assert.Equal(written.Components.Count, result.Manifest.Components.Count);

        // Counts describe what a customer reconciles against.
        Assert.Equal(1, result.Manifest.Counts.Included["artifact"]);
        Assert.Equal(2, result.Manifest.Counts.Included["catalog"]);
        Assert.Equal(1, result.Manifest.Counts.Excluded["secret"]);

        // The requirement travels; the value does not.
        Assert.Equal("SHARED:sales_prod", Assert.Single(result.Manifest.RequiredBindings).LogicalId);
        Assert.Equal("secret", Assert.Single(result.Manifest.Exclusions).ResourceClass);
    }

    [Fact]
    public async Task TwoExportsOfUnchangedStateProduceTheSameDeterministicDigest()
    {
        var first = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "a"), Request("bundle-a"));
        var second = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "b"), Request("bundle-b"));

        // Bundle id and creation time are documented generation metadata and are excluded, so the
        // digests match even though the raw manifests differ.
        Assert.NotEqual(first.BundleId, second.BundleId);
        Assert.Equal(
            TenantBundleWriter.ComputeDeterministicDigest(first),
            TenantBundleWriter.ComputeDeterministicDigest(second));
    }

    [Fact]
    public async Task ChangedContentChangesTheDeterministicDigest()
    {
        var baseline = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "a"), Request());
        var changed = Request() with
        {
            Payloads =
            [
                new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
                    "artifacts/daily_load.etlsql", "SELECT 2 AS Value INTO #proof;")
            ]
        };
        var modified = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "b"), changed);

        Assert.NotEqual(
            TenantBundleWriter.ComputeDeterministicDigest(baseline),
            TenantBundleWriter.ComputeDeterministicDigest(modified));
    }

    [Fact]
    public async Task TamperedPayloadFailsValidationRatherThanImportingQuietly()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        File.WriteAllText(
            Path.Combine(_root, "artifacts", "daily_load.etlsql"),
            "DROP TABLE customers;");

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.payload.hash");
    }

    [Fact]
    public async Task TruncatedBundleFailsOnTheMissingPayload()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        File.Delete(Path.Combine(_root, "catalog", "orchestrator-promotion.json"));

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings,
            f => f.Code == "bundle.payload.missing" && f.Resource == "catalog:orchestrator-promotion");
    }

    [Fact]
    public async Task ManifestClaimingAPathOutsideTheBundleIsRejected()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace("artifacts/daily_load.etlsql", "../../escaped.etlsql", StringComparison.Ordinal));

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.path.escape");
    }

    [Fact]
    public async Task UnknownSchemaIsRefusedRatherThanInterpretedAsThisOne()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace(TenantBundle.SchemaVersion, "etl-sql.tenant-bundle/v99", StringComparison.Ordinal));

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.schema.unsupported");
    }

    [Fact]
    public async Task ResolvedSecretMaterialInTheManifestIsAnError()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace("Provision the secret at the target and rebind the reference.",
                    "password=hunter2", StringComparison.Ordinal));

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.manifest.secret-material");
    }

    [Fact]
    public async Task UnimplementedExportModeIsRefusedAtWriteTime()
    {
        var request = Request() with { ExportMode = TenantBundleExportMode.FullEligibleTenantExport };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => TenantBundleWriter.WriteAsync(_root, request));

        Assert.Contains("not implemented", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_root, TenantBundle.ManifestFileName)));
    }

    [Fact]
    public async Task DuplicateLogicalIdIsRefusedBecauseOneObjectWouldBeLost()
    {
        var request = Request() with
        {
            Payloads =
            [
                new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
                    "artifacts/one.etlsql", "SELECT 1;"),
                new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
                    "artifacts/two.etlsql", "SELECT 2;")
            ]
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => TenantBundleWriter.WriteAsync(_root, request));

        Assert.Contains("script:daily_load", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependencyOnAnAbsentComponentFailsPreflightValidation()
    {
        var request = Request() with
        {
            Payloads =
            [
                new TenantBundlePayload("report:sales", "catalog", "application/json",
                    "catalog/sales.json", Encoding.UTF8.GetBytes("{}"), ["dataset:missing"])
            ]
        };
        await TenantBundleWriter.WriteAsync(_root, request);

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.dependency.missing");
    }

    /// <summary>Generates a throwaway PGP keypair, as CreatePgpKeyPairStatementHandler does.</summary>
    private async Task<(string Public, string Private)> KeyPairAsync(string name)
    {
        var dir = Path.Combine(_root, "keys", name);
        Directory.CreateDirectory(dir);
        var pub = Path.Combine(dir, "public.asc");
        var priv = Path.Combine(dir, "private.asc");
        using var pgp = new PgpCore.PGP();
        await pgp.GenerateKeyAsync(new FileInfo(pub), new FileInfo(priv), $"{name}@example.test",
            string.Empty, 1024);
        return (pub, priv);
    }

    [Fact]
    public async Task EncryptedPayloadsAreCiphertextOnDiskAndDecryptBackToTheOriginal()
    {
        var tenant = await KeyPairAsync("tenant");
        var request = Request() with
        {
            SourceProfile = "SaaS",
            RecipientPublicKeyFile = tenant.Public
        };

        var manifest = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "b"), request);
        var stored = await File.ReadAllBytesAsync(
            Path.Combine(_root, "b", "artifacts", "daily_load.etlsql"));

        Assert.True(manifest.Encryption!.Encrypted);
        Assert.Equal("openpgp", manifest.Encryption.Algorithm);
        Assert.DoesNotContain("SELECT 1 AS Value", Encoding.UTF8.GetString(stored), StringComparison.Ordinal);

        // Hash-as-stored is verifiable with no key at all; the plaintext hash is what a decrypted
        // payload is checked against.
        var component = manifest.Components.Single(c => c.LogicalId == "script:daily_load");
        Assert.NotNull(component.PlaintextSha256);
        var result = await TenantBundleValidator.ValidateAsync(Path.Combine(_root, "b"));
        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));

        var plaintext = await TenantBundleCrypto.DecryptAsync(stored, tenant.Private, null);
        Assert.Equal("SELECT 1 AS Value INTO #proof;", Encoding.UTF8.GetString(plaintext));
    }

    [Fact]
    public async Task EncryptionIsDeterministicallyComparableEvenThoughCiphertextIsNot()
    {
        var tenant = await KeyPairAsync("tenant");
        var request = Request() with { SourceProfile = "SaaS", RecipientPublicKeyFile = tenant.Public };

        var first = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "a"), request);
        var second = await TenantBundleWriter.WriteAsync(Path.Combine(_root, "b"), request);

        // Fresh session key per run, so the stored bytes differ every time...
        Assert.NotEqual(
            first.Components.Single(c => c.LogicalId == "script:daily_load").Sha256,
            second.Components.Single(c => c.LogicalId == "script:daily_load").Sha256);
        // ...but "is this the same tenant state?" must still be answerable.
        Assert.Equal(
            TenantBundleWriter.ComputeDeterministicDigest(first),
            TenantBundleWriter.ComputeDeterministicDigest(second));
    }

    [Fact]
    public async Task SaasSourcedBundleCannotBeWrittenUnencrypted()
    {
        var request = Request() with { SourceProfile = "SaaS" };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => TenantBundleWriter.WriteAsync(_root, request));

        Assert.Contains("must be encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_root, TenantBundle.ManifestFileName)));
    }

    [Fact]
    public async Task SaasSourcedBundleThatClaimsToBeUnencryptedFailsValidation()
    {
        // Hand-edit an Enterprise bundle to claim a SaaS source, which is how an unencrypted export
        // would try to pass itself off as a legitimate SaaS one.
        await TenantBundleWriter.WriteAsync(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace("\"SourceProfile\": \"Enterprise\"", "\"SourceProfile\": \"SaaS\"",
                    StringComparison.Ordinal));

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.encryption.required");
    }

    [Fact]
    public async Task OperatorSignatureVerifiesAndSurvivesRoundTrip()
    {
        var operatorKeys = await KeyPairAsync("operator");
        var request = Request() with { SigningPrivateKeyFile = operatorKeys.Private };
        var bundle = Path.Combine(_root, "b");

        var manifest = await TenantBundleWriter.WriteAsync(bundle, request);

        Assert.Equal(TenantBundle.SignatureFileName, manifest.SignatureFile);
        Assert.True(File.Exists(Path.Combine(bundle, "signatures", "manifest.asc")));

        var result = await TenantBundleValidator.ValidateAsync(bundle,
            new TenantBundleValidator.Options(operatorKeys.Public, RequireSignature: true));

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
    }

    [Fact]
    public async Task SignatureOverADifferentManifestDoesNotVerify()
    {
        var operatorKeys = await KeyPairAsync("operator");
        var bundle = Path.Combine(_root, "b");
        await TenantBundleWriter.WriteAsync(bundle,
            Request() with { SigningPrivateKeyFile = operatorKeys.Private });

        // A real, correctly-formed signature — over content that is no longer what it covers.
        var manifestPath = Path.Combine(bundle, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath).Replace("tenant-acme", "tenant-attacker", StringComparison.Ordinal));

        var result = await TenantBundleValidator.ValidateAsync(bundle,
            new TenantBundleValidator.Options(operatorKeys.Public));

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.signature.invalid");
        // Verification precedes trust: nothing downstream ran, so no manifest is returned.
        Assert.Null(result.Manifest);
    }

    [Fact]
    public async Task SignatureFromAnotherKeyDoesNotVerifyAgainstTheOperatorKey()
    {
        var operatorKeys = await KeyPairAsync("operator");
        var impostor = await KeyPairAsync("impostor");
        var bundle = Path.Combine(_root, "b");
        await TenantBundleWriter.WriteAsync(bundle,
            Request() with { SigningPrivateKeyFile = impostor.Private });

        var result = await TenantBundleValidator.ValidateAsync(bundle,
            new TenantBundleValidator.Options(operatorKeys.Public));

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.signature.invalid");
    }

    [Fact]
    public async Task StrippedSignatureIsCaughtBothByTheManifestClaimAndByRequiringOne()
    {
        var operatorKeys = await KeyPairAsync("operator");
        var bundle = Path.Combine(_root, "b");
        await TenantBundleWriter.WriteAsync(bundle,
            Request() with { SigningPrivateKeyFile = operatorKeys.Private });
        File.Delete(Path.Combine(bundle, "signatures", "manifest.asc"));

        // With the key: verification fails outright.
        var verified = await TenantBundleValidator.ValidateAsync(bundle,
            new TenantBundleValidator.Options(operatorKeys.Public));
        Assert.False(verified.IsValid);
        Assert.Contains(verified.Findings, f => f.Code == "bundle.signature.invalid");

        // Without the key: the manifest still says it was signed, so the absence is still an error
        // rather than a bundle that quietly reads as unsigned.
        var unverified = await TenantBundleValidator.ValidateAsync(bundle);
        Assert.False(unverified.IsValid);
        Assert.Contains(unverified.Findings, f => f.Code == "bundle.signature.missing");
    }

    [Fact]
    public async Task RequiringASignatureWithoutSupplyingAKeyIsRefused()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var result = await TenantBundleValidator.ValidateAsync(_root,
            new TenantBundleValidator.Options(RequireSignature: true));

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.signature.unverified");
    }

    [Fact]
    public async Task MissingManifestIsReportedWithoutThrowing()
    {
        Directory.CreateDirectory(_root);

        var result = await TenantBundleValidator.ValidateAsync(_root);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
        Assert.Contains(result.Findings, f => f.Code == "bundle.manifest.missing");
    }
}
