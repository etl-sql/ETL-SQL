namespace ETL_SQL.Tests.Orchestration;

public sealed class ProductionCanaryScriptContractTests
{
    [Fact]
    public void CertificationScriptPinsManifestEvidenceIsolationAndFailureBehavior()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRoot(), "scripts", "Test-ProductionCanaryCertification.ps1"));

        Assert.Contains("production-canary-plan.json", script, StringComparison.Ordinal);
        Assert.Contains("ETLSQL_CANARY_EVIDENCE_DIR", script, StringComparison.Ordinal);
        Assert.Contains("production-canary-certification/v1", script, StringComparison.Ordinal);
        Assert.Contains("expectedRunCount", script, StringComparison.Ordinal);
        Assert.Contains("runFindingCount", script, StringComparison.Ordinal);
        Assert.Contains("Get-ProductionCanaryBoundaryFindings", script, StringComparison.Ordinal);
        Assert.Contains("boundaryFindingCount", script, StringComparison.Ordinal);
        Assert.Contains("dirtyPaths", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $passed) { exit 1 }", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisioningAndCredentialEvidenceIsValidatedBeyondSummaryFlags()
    {
        var result = await ETL_SQL.Tests.Docs.ReleasePolicyHelperTests.RunHelper(
            "& './scripts/checks/fixtures/test-production-canary-evidence.ps1'",
            helperName: "ProductionCanaryEvidence.Helpers.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Canary provisioning and credential details reject corrupt receipts", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestContainsNoSecretMaterialOrCustomerIdentifiers()
    {
        var manifest = File.ReadAllText(Path.Combine(
            RepoRoot(), "tests", "fixtures", "production-canary-plan.json"));

        Assert.DoesNotContain("password", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer-", manifest, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETL-SQL.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
