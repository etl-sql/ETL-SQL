namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "DeploymentProfile")]
public sealed class DeploymentProfileCertificationScriptContractTests
{
    [Fact]
    public void RunnerAggregatesConcreteEvidenceAndFailsClosedForReleaseClaims()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRoot(), "scripts", "Test-DeploymentProfileCertification.ps1"));

        Assert.Contains("ETLSQL_DEPLOYMENT_CERT_EVIDENCE_DIR", script, StringComparison.Ordinal);
        Assert.Contains("Get-ExpectedScenarioIds", script, StringComparison.Ordinal);
        Assert.Contains("Missing required scenario evidence", script, StringComparison.Ordinal);
        Assert.Contains("releaseEligible = $passed -and $dirtyLines.Count -eq 0", script, StringComparison.Ordinal);
        Assert.Contains("-and -not $releaseEligible", script, StringComparison.Ordinal);
        Assert.Contains("artifactHashes", script, StringComparison.Ordinal);
        Assert.Contains("mappingDecisions", script, StringComparison.Ordinal);
        Assert.Contains("continuity", script, StringComparison.Ordinal);
        Assert.Contains("negativeIsolation", script, StringComparison.Ordinal);
        Assert.Contains("rollback", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIndexKeepsManagedDedicatedSeparateFromSharedSaas()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRoot(), "scripts", "Test-DeploymentProfileCertification.ps1"));

        Assert.Contains("Managed Dedicated", script, StringComparison.Ordinal);
        Assert.Contains("sharedSaaS = \"NotCertified\"", script, StringComparison.Ordinal);
        Assert.Contains("etl-sql.deployment-profile-release-claims/v1", script, StringComparison.Ordinal);
        Assert.Contains("claims-index.json", script, StringComparison.Ordinal);
        Assert.Contains("claims-index.md", script, StringComparison.Ordinal);
        Assert.Contains("$markdown.Add('Only rows with `releaseEligible = True`", script, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
