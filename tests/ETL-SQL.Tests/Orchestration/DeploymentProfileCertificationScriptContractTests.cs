using System.Text.RegularExpressions;

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
        Assert.Contains("\"SharedSaaS\"", script, StringComparison.Ordinal);
        Assert.Contains("sharedSaaS = \"Certified\"", script, StringComparison.Ordinal);
        Assert.Contains("etl-sql.deployment-profile-release-claims/v1", script, StringComparison.Ordinal);
        Assert.Contains("claims-index.json", script, StringComparison.Ordinal);
        Assert.Contains("claims-index.md", script, StringComparison.Ordinal);
        Assert.Contains("$markdown.Add('Only rows with `releaseEligible = True`", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Progressive SaaS Phase A requires the Enterprise lane to prove all eight hosted prerequisites
    /// in one run against one commit. Dropping one would not make the lane red — it would quietly
    /// narrow what "Enterprise certified" means, which is the failure mode the whole certification
    /// framework exists to prevent. So the prerequisite list, its enforcement, and the fact that each
    /// name is actually attached to a phase are all pinned here.
    /// </summary>
    [Fact]
    public void EnterpriseLaneProvesEveryHostedPrerequisiteForTheSaasPhaseAGate()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRoot(), "scripts", "Test-DeploymentProfileCertification.ps1"));

        string[] prerequisites =
        [
            "verifiable-caller-identity",
            "per-object-authorization",
            "shared-state-and-artifact-providers",
            "scoped-secret-and-policy-authority",
            "durable-audit",
            "high-availability",
            "backup-and-restore",
            "upgrade-and-promotion-evidence"
        ];

        foreach (var prerequisite in prerequisites)
        {
            // Once in $EnterpriseHostedPrerequisites, and at least once more as a phase tag.
            var occurrences = Regex.Matches(script, Regex.Escape($"\"{prerequisite}\"")).Count;
            Assert.True(occurrences >= 2,
                $"The Enterprise certification lane declares the hosted prerequisite '{prerequisite}' " +
                $"but no phase is tagged with it ({occurrences} occurrence(s)). A declared-but-unproven " +
                "prerequisite reports as unproven at runtime; wire it to a phase or remove the claim.");
        }

        // An unproven prerequisite must fail the lane, not merely annotate the evidence.
        Assert.Contains("$unprovenPrerequisites.Count -eq 0", script, StringComparison.Ordinal);
        Assert.Contains("Enterprise hosted prerequisite not proven:", script, StringComparison.Ordinal);
        Assert.Contains("hostedPrerequisites = $hostedPrerequisites", script, StringComparison.Ordinal);

        // The upgrade prerequisite must land concrete scenario evidence, not just a green phase.
        Assert.Contains("\"Enterprise\" { $expected.Add(\"EnterpriseUpgrade\") }", script, StringComparison.Ordinal);
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
