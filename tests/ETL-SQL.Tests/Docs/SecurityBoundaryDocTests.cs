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
}
