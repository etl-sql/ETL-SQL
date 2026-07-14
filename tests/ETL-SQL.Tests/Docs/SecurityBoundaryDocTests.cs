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
    private static string AdminGuide() =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "Docs", "Administrators_Guide.md")),
            @"\s+", " ");

    private static string EnterpriseReleaseGates() =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(
                RepoRoot, "Docs", "Operations", "Enterprise_Release_Gates.md")),
            @"\s+", " ");

    private static string OperationDoc(string fileName) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot, "Docs", "Operations", fileName)),
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
        var text = OperationDoc("Enterprise_Security_Review_Packet.md");
        Assert.Contains("Status: Prepared, not signed off", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("senior security reviewer", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot close with any open high-severity finding",
            text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote mutation", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterpriseReleaseEvidenceChecklist_PinsFullSuiteEvidenceRequirements()
    {
        var text = OperationDoc("Enterprise_Release_Evidence_Checklist.md");
        Assert.Contains("Status: Prepared checklist, not release evidence",
            text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test-lane.ps1 -Lane fast", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-PreRelease.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-EnterpriseHardeningCertification.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("etl-sql admin restore --validate --report", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ha-soak validate", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StandaloneRegressionTests", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Todo_TracksV016ReleaseSuiteEvidence()
    {
        var text = Todo();
        Assert.Contains("v0.16.0 Pre-Release Evidence", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Enterprise_Release_Evidence_Checklist.md", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-PreRelease.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-EnterpriseHardeningCertification.ps1", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ha-soak validate", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SecurityBoundaryDocTests", text, System.StringComparison.OrdinalIgnoreCase);
    }
}
