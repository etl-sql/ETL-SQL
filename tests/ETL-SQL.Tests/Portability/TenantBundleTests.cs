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
    public void WrittenBundleRoundTripsThroughTheStandaloneValidator()
    {
        var written = TenantBundleWriter.Write(_root, Request());
        var result = TenantBundleValidator.Validate(_root);

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
    public void TwoExportsOfUnchangedStateProduceTheSameDeterministicDigest()
    {
        var first = TenantBundleWriter.Write(Path.Combine(_root, "a"), Request("bundle-a"));
        var second = TenantBundleWriter.Write(Path.Combine(_root, "b"), Request("bundle-b"));

        // Bundle id and creation time are documented generation metadata and are excluded, so the
        // digests match even though the raw manifests differ.
        Assert.NotEqual(first.BundleId, second.BundleId);
        Assert.Equal(
            TenantBundleWriter.ComputeDeterministicDigest(first),
            TenantBundleWriter.ComputeDeterministicDigest(second));
    }

    [Fact]
    public void ChangedContentChangesTheDeterministicDigest()
    {
        var baseline = TenantBundleWriter.Write(Path.Combine(_root, "a"), Request());
        var changed = Request() with
        {
            Payloads =
            [
                new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
                    "artifacts/daily_load.etlsql", "SELECT 2 AS Value INTO #proof;")
            ]
        };
        var modified = TenantBundleWriter.Write(Path.Combine(_root, "b"), changed);

        Assert.NotEqual(
            TenantBundleWriter.ComputeDeterministicDigest(baseline),
            TenantBundleWriter.ComputeDeterministicDigest(modified));
    }

    [Fact]
    public void TamperedPayloadFailsValidationRatherThanImportingQuietly()
    {
        TenantBundleWriter.Write(_root, Request());
        File.WriteAllText(
            Path.Combine(_root, "artifacts", "daily_load.etlsql"),
            "DROP TABLE customers;");

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.payload.hash");
    }

    [Fact]
    public void TruncatedBundleFailsOnTheMissingPayload()
    {
        TenantBundleWriter.Write(_root, Request());
        File.Delete(Path.Combine(_root, "catalog", "orchestrator-promotion.json"));

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings,
            f => f.Code == "bundle.payload.missing" && f.Resource == "catalog:orchestrator-promotion");
    }

    [Fact]
    public void ManifestClaimingAPathOutsideTheBundleIsRejected()
    {
        TenantBundleWriter.Write(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace("artifacts/daily_load.etlsql", "../../escaped.etlsql", StringComparison.Ordinal));

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.path.escape");
    }

    [Fact]
    public void UnknownSchemaIsRefusedRatherThanInterpretedAsThisOne()
    {
        TenantBundleWriter.Write(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace(TenantBundle.SchemaVersion, "etl-sql.tenant-bundle/v99", StringComparison.Ordinal));

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.schema.unsupported");
    }

    [Fact]
    public void ResolvedSecretMaterialInTheManifestIsAnError()
    {
        TenantBundleWriter.Write(_root, Request());
        var manifestPath = Path.Combine(_root, TenantBundle.ManifestFileName);
        File.WriteAllText(manifestPath,
            File.ReadAllText(manifestPath)
                .Replace("Provision the secret at the target and rebind the reference.",
                    "password=hunter2", StringComparison.Ordinal));

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.manifest.secret-material");
    }

    [Fact]
    public void UnimplementedExportModeIsRefusedAtWriteTime()
    {
        var request = Request() with { ExportMode = TenantBundleExportMode.FullEligibleTenantExport };

        var ex = Assert.Throws<ArgumentException>(() => TenantBundleWriter.Write(_root, request));

        Assert.Contains("not implemented", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_root, TenantBundle.ManifestFileName)));
    }

    [Fact]
    public void DuplicateLogicalIdIsRefusedBecauseOneObjectWouldBeLost()
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

        var ex = Assert.Throws<ArgumentException>(() => TenantBundleWriter.Write(_root, request));

        Assert.Contains("script:daily_load", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyOnAnAbsentComponentFailsPreflightValidation()
    {
        var request = Request() with
        {
            Payloads =
            [
                new TenantBundlePayload("report:sales", "catalog", "application/json",
                    "catalog/sales.json", Encoding.UTF8.GetBytes("{}"), ["dataset:missing"])
            ]
        };
        TenantBundleWriter.Write(_root, request);

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "bundle.dependency.missing");
    }

    [Fact]
    public void MissingManifestIsReportedWithoutThrowing()
    {
        Directory.CreateDirectory(_root);

        var result = TenantBundleValidator.Validate(_root);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
        Assert.Contains(result.Findings, f => f.Code == "bundle.manifest.missing");
    }
}
