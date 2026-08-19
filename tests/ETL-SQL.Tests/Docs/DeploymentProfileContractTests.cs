using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Docs;

public sealed class DeploymentProfileContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void CapabilityMatrix_CoversEveryProfileConcernWithStatusAndEvidence()
    {
        var text = Standard();
        string[] concerns =
        [
            "Authoring", "Execution", "Scheduling", "Connections and secrets", "Reports",
            "Quality and stewardship", "Identity", "Policy", "Audit", "Backup and recovery",
            "Observability", "High availability", "Tenant isolation"
        ];

        foreach (var concern in concerns)
        {
            var row = Regex.Match(text, $@"^\| {Regex.Escape(concern)} \|(?<cells>.+)\|$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            Assert.True(row.Success, $"Deployment profile matrix is missing '{concern}'.");
            var cells = row.Groups["cells"].Value.Split('|', StringSplitOptions.TrimEntries);
            Assert.Equal(5, cells.Length);
            foreach (var cell in cells)
            {
                Assert.Matches(@"\*\*(Green|Yellow|Red|N/A)\*\*", cell);
                if (cell.Contains("**N/A**", StringComparison.OrdinalIgnoreCase))
                    Assert.Contains("—", cell);
                else
                    Assert.Contains("](", cell); // non-N/A status must link current evidence
            }
        }
    }

    [Fact]
    public void CapabilityMatrix_SeparatesManagedDedicatedFromSharedSaasClaims()
    {
        var text = Standard();
        Assert.Contains("| Managed Dedicated SaaS | Shared SaaS |", text, StringComparison.Ordinal);

        var tenantIsolation = Regex.Match(text, @"^\| Tenant isolation \|(?<cells>.+)\|$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        Assert.True(tenantIsolation.Success);
        var cells = tenantIsolation.Groups["cells"].Value.Split('|', StringSplitOptions.TrimEntries);
        Assert.Contains("**Green**", cells[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("**Red**", cells[4], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dedicated evidence cannot satisfy this cell", cells[4], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandardPinsSmallestSafeFormsOverlaysAndReviewQuestions()
    {
        var text = Standard();
        foreach (var concern in new[] { "Authoring", "Execution", "Scheduling", "Connections/secrets",
            "Reports", "Quality/stewardship", "Identity", "Policy", "Audit", "Backup/recovery",
            "Observability", "HA", "Tenant isolation" })
            Assert.Contains($"| {concern} |", text, StringComparison.OrdinalIgnoreCase);

        foreach (var overlay in new[] { "Regulated", "Air-gapped", "High volume", "High availability",
            "Disaster recovery", "Data residency" })
            Assert.Contains(overlay, text, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("smallest safe profile", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("move upward unchanged", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile and transition tests", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseChecklistRequiresProfilePortabilityAndEvidenceReview()
    {
        var checklist = File.ReadAllText(Path.Combine(RepoRoot, "docs", "releases", "release-checklist.md"));
        Assert.Contains("Deployment-profile portability review", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("smallest-safe profiles", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profiles/transitions with current linked evidence", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("air-gapped", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("residency", checklist, StringComparison.OrdinalIgnoreCase);
    }

    private static string Standard() => File.ReadAllText(Path.Combine(
        RepoRoot, "docs", "architecture", "standards", "Deployment_Profile_Standards.md")).Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
