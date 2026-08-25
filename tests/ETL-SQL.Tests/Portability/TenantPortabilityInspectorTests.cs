using ETL_SQL.App.Portability;
using ETL_SQL.Core.Portability;

namespace ETL_SQL.Tests.Portability;

/// <summary>
/// Preflight (tenant-portability.md §10) answers "can this target accept the bundle, and what must it
/// supply first?" — a different question from validation's "is this intact and authentic?".
/// </summary>
public sealed class TenantPortabilityInspectorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"preflight-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static TenantBundleRequest Request() => new(
        "bundle-1",
        DateTimeOffset.Parse("2026-08-10T09:00:00Z"),
        "0.18.0",
        "Enterprise",
        "tenant-acme",
        TenantBundleExportMode.ConfigurationAndArtifacts,
        "consistency-1",
        [new TenantBundlePayload("script:daily_load", "artifact", "text/plain",
            "artifacts/daily_load.etlsql", "SELECT 1;")],
        [
            new TenantBundleRequiredBinding("SECRET:sales-etl-credential", "secret", "Provision it."),
            new TenantBundleRequiredBinding("SHARED:sales_prod", "connection", "Bind it.")
        ],
        [new TenantBundleExclusion("secret:sales-etl-credential", "secret",
            "Resolved secrets are never portable.", "Provision at the target.")]);

    [Fact]
    public async Task AValidBundleWithUnsatisfiedBindingsIsNotAFailureButIsNotImportableEither()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var preflight = await TenantPortabilityInspector.PreflightAsync(_root);

        Assert.Equal(TenantPortabilityExitCode.BindingsRequired, preflight.ExitCode);
        Assert.False(preflight.CanProceed);
        // The bundle itself is fine — the target simply owes it two bindings.
        Assert.DoesNotContain(preflight.Findings, f => f.Severity == "Error");
        Assert.Equal(2, preflight.RequiredBindings.Count);
        Assert.Single(preflight.Exclusions);
    }

    [Fact]
    public async Task SupplyingEveryBindingClearsPreflight()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var preflight = await TenantPortabilityInspector.PreflightAsync(_root,
            bindingsSuppliedByTarget: ["SECRET:sales-etl-credential", "SHARED:sales_prod"]);

        Assert.Equal(TenantPortabilityExitCode.Ok, preflight.ExitCode);
        Assert.True(preflight.CanProceed);
        Assert.Empty(preflight.RequiredBindings);
    }

    [Fact]
    public async Task PartiallySuppliedBindingsReportOnlyWhatIsStillOutstanding()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());

        var preflight = await TenantPortabilityInspector.PreflightAsync(_root,
            bindingsSuppliedByTarget: ["secret:SALES-ETL-CREDENTIAL"]);

        // Matching is case-insensitive; a target that spells the alias differently has still bound it.
        var outstanding = Assert.Single(preflight.RequiredBindings);
        Assert.Equal("SHARED:sales_prod", outstanding.LogicalId);
    }

    [Fact]
    public async Task ATamperedBundleFailsAsInvalidRatherThanAsMissingBindings()
    {
        await TenantBundleWriter.WriteAsync(_root, Request());
        await File.WriteAllTextAsync(
            Path.Combine(_root, "artifacts", "daily_load.etlsql"), "DROP TABLE customers;");

        var preflight = await TenantPortabilityInspector.PreflightAsync(_root);

        Assert.Equal(TenantPortabilityExitCode.BundleInvalid, preflight.ExitCode);
        Assert.Empty(preflight.RequiredBindings);
    }

    [Fact]
    public async Task AnUnverifiableSignatureGetsItsOwnExitCodeSoARunbookCanTellThemApart()
    {
        var keyDir = Path.Combine(_root, "keys");
        Directory.CreateDirectory(keyDir);
        var pub = Path.Combine(keyDir, "operator-public.asc");
        var priv = Path.Combine(keyDir, "operator-private.asc");
        var impostorPub = Path.Combine(keyDir, "impostor-public.asc");
        var impostorPriv = Path.Combine(keyDir, "impostor-private.asc");
        using (var pgp = new PgpCore.PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(pub), new FileInfo(priv),
                "operator@example.test", string.Empty, 1024);
            await pgp.GenerateKeyAsync(new FileInfo(impostorPub), new FileInfo(impostorPriv),
                "impostor@example.test", string.Empty, 1024);
        }

        var bundle = Path.Combine(_root, "bundle");
        await TenantBundleWriter.WriteAsync(bundle,
            Request() with { SigningPrivateKeyFile = impostorPriv });

        var preflight = await TenantPortabilityInspector.PreflightAsync(bundle, pub);

        Assert.Equal(TenantPortabilityExitCode.SignatureUnverified, preflight.ExitCode);
    }

    [Fact]
    public async Task AMissingBundleDirectoryIsNotFoundRatherThanInvalid()
    {
        var preflight = await TenantPortabilityInspector.PreflightAsync(
            Path.Combine(_root, "nowhere"));

        Assert.Equal(TenantPortabilityExitCode.NotFound, preflight.ExitCode);
    }
}
