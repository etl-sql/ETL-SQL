using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Keeps the top-level navigation a single shared vocabulary rather than six copies of one.
///
/// <para>Before consolidation each page decided for itself which entry points to reveal, in five
/// different spellings, and the copies had already drifted: one page gated Orchestrator on a role
/// name (<c>Orchestrator</c>) that does not exist, so its rule was one role wider than everyone
/// else's and had been for as long as nobody diffed the pages. Two destinations could not be
/// decided client-side at all, and both were wrong on every page.</para>
///
/// <para>Copy-paste is the natural thing to do here — adding a page means starting from one that
/// works — so the invariant has to be enforced rather than remembered.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class PortalNavigationVocabularyTests
{
    private static string WebRoot() => Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot");

    /// <summary>The pages that carry the shared top bar. Others (login, previews) have no nav.</summary>
    private static IEnumerable<(string Name, string Html)> ShellPages() =>
        Directory.EnumerateFiles(WebRoot(), "*.html")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            .Where(page => page.Item2.Contains("class=\"topbar-nav\"", StringComparison.Ordinal));

    /// <summary>Destination ids the server decides, read from the controller rather than restated.</summary>
    private static IReadOnlyList<string> ServerDestinations()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.Portal", "Controllers", "NavigationController.cs"));
        var ids = Regex.Matches(source, @"""(?<id>[a-z][A-Za-z]*Nav)""")
            .Select(m => m.Groups["id"].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(ids);
        return ids;
    }

    /// <summary>
    /// No page may set the visibility of a server-decided destination itself. This is the rule that
    /// actually holds the consolidation together: reintroducing one line of local gating is easy,
    /// looks harmless in review, and silently takes that page back out of the shared answer.
    /// </summary>
    [Fact]
    public void NoPage_DecidesNavigationVisibilityForItself()
    {
        var destinations = ServerDestinations();
        var offenders = new List<string>();

        foreach (var (name, html) in ShellPages())
        {
            foreach (var id in destinations)
            {
                // getElementById('adminNav').style.display = ... , or via a local alias.
                var pattern = $@"getElementById\(['""]{id}['""]\)(?:\s*\)?)?\s*\.style\.display\s*=";
                if (Regex.IsMatch(html, pattern))
                    offenders.Add($"{name} sets {id} visibility directly");
            }
        }

        Assert.True(offenders.Count == 0,
            "These pages gate navigation locally instead of applying the shared answer:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nTwo of these destinations depend on module and capability state no token claim "
            + "carries, so a local rule cannot be right — it can only be wrong more quietly.");
    }

    /// <summary>Every page carrying the top bar applies the shared answer.</summary>
    [Fact]
    public void EveryShellPage_AppliesTheSharedNavigation()
    {
        var missing = ShellPages()
            .Where(page => !page.Html.Contains("portal-nav.js", StringComparison.Ordinal)
                        || !page.Html.Contains("applyNavigation", StringComparison.Ordinal))
            .Select(page => page.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "These pages render the shared top bar but never apply the navigation answer, so every "
            + "gated entry stays hidden on them:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// A destination the server decides must have somewhere to land on every page — otherwise the
    /// answer is computed, sent, and silently dropped. The exception is the page you are already
    /// on: it marks its own entry <c>active</c> and must keep showing it.
    /// </summary>
    [Fact]
    public void EveryShellPage_CarriesEveryServerDecidedDestination()
    {
        var destinations = ServerDestinations();
        var pages = ShellPages().ToList();

        // id -> href, learned from the pages themselves so the mapping is never restated here.
        var hrefs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, html) in pages)
        {
            foreach (Match match in Regex.Matches(html, @"<a href=""(?<href>[^""]+)""[^>]*id=""(?<id>\w+Nav)"""))
                hrefs.TryAdd(match.Groups["id"].Value, match.Groups["href"].Value);
        }

        var gaps = new List<string>();
        foreach (var (name, html) in pages)
        {
            var nav = Regex.Match(html, @"<nav class=""topbar-nav"">(?<body>.*?)</nav>", RegexOptions.Singleline);
            Assert.True(nav.Success, $"{name} has no topbar-nav block.");
            var body = nav.Groups["body"].Value;

            foreach (var id in destinations)
            {
                if (body.Contains($"id=\"{id}\"", StringComparison.Ordinal)) continue;

                // Acceptable only when this page *is* that destination and marks it active.
                var isThisPage = hrefs.TryGetValue(id, out var href)
                    && Regex.IsMatch(body, $@"<a href=""{Regex.Escape(href)}""[^>]*class=""active""");
                if (!isThisPage)
                    gaps.Add($"{name} has no {id} entry");
            }
        }

        Assert.True(gaps.Count == 0,
            "The server decides these destinations, but the markup gives them nowhere to apply:\n  "
            + string.Join("\n  ", gaps));
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
