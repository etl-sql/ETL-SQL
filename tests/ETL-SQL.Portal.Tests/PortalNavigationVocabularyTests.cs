using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>Guards the single rendered Portal header and server-owned navigation vocabulary.</summary>
[Trait("Category", "Portal")]
public sealed class PortalNavigationVocabularyTests
{
    private static string WebRoot() => Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot");

    private static IEnumerable<(string Name, string Html)> ShellPages() =>
        Directory.EnumerateFiles(WebRoot(), "*.html")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            .Where(page => page.Item2.Contains("data-portal-header", StringComparison.Ordinal));

    private static IReadOnlyList<string> ServerDestinations()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.Portal", "Controllers", "NavigationController.cs"));
        var ids = Regex.Matches(source, "\"(?<id>[a-z][A-Za-z]*Nav)\"")
            .Select(match => match.Groups["id"].Value).Distinct().ToList();
        Assert.NotEmpty(ids);
        return ids;
    }

    [Fact]
    public void EveryShellPage_RendersExactlyOneSharedHeaderAndAppliesServerNavigation()
    {
        var pages = ShellPages().ToList();
        Assert.Equal(6, pages.Count);
        foreach (var (name, html) in pages)
        {
            Assert.Single(Regex.Matches(html, @"<header\s+data-portal-header").Cast<Match>());
            Assert.DoesNotContain("class=\"topbar-nav\"", html, StringComparison.Ordinal);
            Assert.Contains("portal-header.js", html, StringComparison.Ordinal);
            Assert.Contains("renderPortalHeader()", html, StringComparison.Ordinal);
            Assert.Contains("portal-nav.js", html, StringComparison.Ordinal);
            Assert.Contains("applyNavigation", html, StringComparison.Ordinal);
            Assert.Matches(@"data-active=""(reports|admin|orchestrator|studio|docs)""", html);
        }
    }

    [Fact]
    public void SharedHeader_CarriesEveryServerDestinationAndStartsGatedEntriesHidden()
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), "js", "portal-header.js"));
        foreach (var id in ServerDestinations())
            Assert.Contains($"'{id}'", source, StringComparison.Ordinal);
        Assert.Contains("['studio', 'docs', 'orchestrator', 'admin']", source, StringComparison.Ordinal);
        Assert.Contains("style=\"display:none\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPage_DecidesNavigationVisibilityForItself()
    {
        var offenders = new List<string>();
        foreach (var (name, html) in ShellPages())
        foreach (var id in ServerDestinations())
        {
            var pattern = $@"getElementById\(['""]{id}['""]\)(?:\s*\)?)?\s*\.style\.display\s*=";
            if (Regex.IsMatch(html, pattern)) offenders.Add($"{name} sets {id}");
        }
        Assert.Empty(offenders);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
