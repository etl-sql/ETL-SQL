using System.Text.Json;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "FaultCertification")]
public sealed class ProviderNeutralFaultCertificationScriptContractTests
{
    [Fact]
    public void RunnerFailsClosedAndRetainsRepeatedEvidence()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRoot(), "scripts", "Test-ProviderNeutralFaultCertification.ps1"));

        Assert.Contains("[ValidateRange(2, 100)]", script, StringComparison.Ordinal);
        Assert.Contains("etl-sql.provider-neutral-fault-matrix/v1", script, StringComparison.Ordinal);
        Assert.Contains("etl-sql.provider-neutral-fault-certification/v1", script, StringComparison.Ordinal);
        Assert.Contains("-not $_.faultActivated -or -not $_.invariants.passed", script, StringComparison.Ordinal);
        Assert.Contains("$reports.Count -eq $selected.Count", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $passed) { exit 1 }", script, StringComparison.Ordinal);
    }

    [Fact]
    public void MatrixCoversEverySupportedProfileAndAdapterKind()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "tests", "fixtures", "provider-neutral-fault-matrix.json")));
        var rows = document.RootElement.GetProperty("profiles").EnumerateArray().ToArray();

        Assert.Equal(["Enterprise", "SaaS", "SharedSaaS", "Solo", "Team"],
            rows.Select(row => row.GetProperty("profile").GetString()).OrderBy(value => value));
        Assert.Equal(["cloud", "docker", "local"],
            rows.Select(row => row.GetProperty("adapter").GetString()).Distinct().OrderBy(value => value));
    }

    [Fact]
    public async Task EveryDeploymentProfileLaneIncludesItsFaultMatrix()
    {
        var script = File.ReadAllText(Path.Combine(
            RepoRoot(), "scripts", "Test-DeploymentProfileCertification.ps1"));

        var result = await ETL_SQL.Tests.Docs.ReleasePolicyHelperTests.RunHelper("""
            @(foreach ($profile in (Get-DeploymentCertificationLanes Profile)) {
                [pscustomobject]@{ profile = $profile; faults = @((Get-DeploymentCertificationPlan -Kind Profile -Names $profile) | Where-Object faultProfile | ForEach-Object faultProfile) }
            }) | ConvertTo-Json -Depth 5
            """, helperName: "DeploymentCertification.Plan.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        using var plan = JsonDocument.Parse(result.Output);
        Assert.Equal(new[] { "Solo", "Team", "Enterprise", "SaaS", "SharedSaaS" },
            plan.RootElement.EnumerateArray().Select(item => item.GetProperty("profile").GetString()));
        foreach (var item in plan.RootElement.EnumerateArray())
            Assert.Equal(new[] { item.GetProperty("profile").GetString() }, item.GetProperty("faults").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("Test-ProviderNeutralFaultCertification.ps1", script, StringComparison.Ordinal);
        Assert.Contains("provider-neutral-faults", script, StringComparison.Ordinal);
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
