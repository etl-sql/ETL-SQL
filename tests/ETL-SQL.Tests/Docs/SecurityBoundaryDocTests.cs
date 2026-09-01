using Xunit;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// v0.14.0 release gate: documentation must never claim OS-level containment against administrators,
/// and must mandate WDAC/AppLocker (or equivalent) where that boundary is required. This guard pins
/// the honest boundary language in the Administrators' Guide so a future edit cannot silently
/// overclaim it.
/// </summary>
public sealed class SecurityBoundaryDocTests
{
    private static readonly string RepoRoot =
        System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    // Collapse whitespace so assertions are robust to markdown line-wrapping.
    // The enrollment boundary language lives in enterprise-enrollment.md (split out of the former
    // native-admin-services.md monolith).
    private static string AdminGuide() =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(
                RepoRoot, "docs", "administration", "platform", "enterprise-enrollment.md")),
            @"\s+", " ");

    private static string EnterpriseReleaseGates() =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(
                RepoRoot, "docs", "architecture", "decisions", "enterprise-release-gates.md")),
            @"\s+", " ");

    private static string OperationDoc(string fileName) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "docs", "architecture", "decisions", fileName)),
            @"\s+", " ");

    private static string Todo() =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "TODO.md")),
            @"\s+", " ");

    [Fact]
    public void AdminGuide_StatesEnrollmentIsNotOsLevelContainment()
    {
        var text = AdminGuide();
        // The guide must acknowledge enrollment does not contain a determined local user/administrator.
        Assert.Contains("cannot stop a user from downloading, compiling, or running unrelated software",
            text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminGuide_MandatesWdacAppLockerForMandatoryEnforcement()
    {
        var text = AdminGuide();
        Assert.Contains("Windows Defender Application Control", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AppLocker", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseReleaseGates_PinReadOnlyFleetAndThreatReview()
    {
        var text = EnterpriseReleaseGates();
        Assert.Contains("Fleet aggregation is read-only by default", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires a separate approved design", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Threat model and security review", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No open high-severity findings", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseReleaseGates_PinCertificationSuitesAndPrioritization()
    {
        var text = EnterpriseReleaseGates();
        Assert.Contains("Workstream Prioritization", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-EnterpriseHardeningCertification.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test-lane.ps1 -Lane fast", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StandaloneRegressionTests", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SecurityBoundaryDocTests", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseReleaseGates_PinOsBoundaryLanguage()
    {
        var text = EnterpriseReleaseGates();
        Assert.Contains("does not provide OS-level containment", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows Defender Application Control", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AppLocker", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseSecurityReviewPacket_RequiresSignedReviewAndClosedHighFindings()
    {
        var text = OperationDoc("enterprise-security-review-packet.md");
        Assert.Contains("Status: Prepared, not signed off", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("senior security reviewer", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot close with any open high-severity finding",
            text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote mutation", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseReleaseEvidenceChecklist_PinsFullSuiteEvidenceRequirements()
    {
        var text = OperationDoc("enterprise-release-evidence-checklist.md");
        Assert.Contains("Status: Prepared checklist, not release evidence",
            text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test-lane.ps1 -Lane fast", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-PreRelease.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-EnterpriseHardeningCertification.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("etl-sql admin restore --validate --report", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ha-soak validate", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StandaloneRegressionTests", text, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The active release must track the evidence gates from
    /// <c>Enterprise_Release_Evidence_Checklist.md</c>, so a release cannot be cut without the
    /// recovery, HA and security-boundary evidence having been run.
    ///
    /// Deliberately version-agnostic. This previously pinned the literal "v0.16.0 Pre-Release
    /// Evidence" and so began failing the moment TODO.md rolled over to the next release — a guard
    /// that breaks on every rollover teaches people to ignore it, which is worse than no guard. The
    /// version is read from TODO.md itself rather than <c>VersionPrefix</c>, because Set-Version.ps1
    /// only bumps that at release time, so the props file still names the previous release while the
    /// next one is being prepared.
    /// </summary>
    [Fact]
    public void Todo_TracksReleaseSuiteEvidenceForTheActiveRelease()
    {
        var raw = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "TODO.md"));

        var release = System.Text.RegularExpressions.Regex.Match(
            // TODO.md numbers its top-level sections ("## 2. v0.19.0 Release Evidence Gates"), so the
            // number is optional here. Without it this guard has been red against the very document
            // it guards, and a guard that is always red is one people learn to scroll past.
            raw, @"^##\s+(?:\d+\.\s*)?v(\d+\.\d+\.\d+)\s+Release",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(release.Success,
            "TODO.md must name the active release with a '## vX.Y.Z Release' heading so the release " +
            "evidence below it is attributable to a version.");

        var text = Todo();
        string[] evidenceGates =
        [
            "enterprise-release-evidence-checklist.md",
            "test-lane.ps1",
            "Test-PreRelease.ps1",
            "Test-EnterpriseHardeningCertification.ps1",
            "admin restore --validate",
            "ha-soak validate",
            "SecurityBoundaryDocTests"
        ];

        foreach (var gate in evidenceGates)
        {
            Assert.True(text.Contains(gate, System.StringComparison.OrdinalIgnoreCase),
                $"TODO.md tracks release v{release.Groups[1].Value} but does not reference the " +
                $"'{gate}' evidence gate from Enterprise_Release_Evidence_Checklist.md.");
        }
    }
}
