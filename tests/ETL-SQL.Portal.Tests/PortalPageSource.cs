using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Reads a Portal page as its markup plus the page module it loads.
/// </summary>
/// <remarks>
/// <para>Each page's behaviour used to sit in an inline <c>&lt;script type="module"&gt;</c> block
/// inside the .html, and the checks that read a page read the .html alone. The blocks now live in
/// <c>wwwroot/js/pages/&lt;page&gt;.js</c> — a file the type gate, the linters and the parse check
/// can all see — so a check that still reads only the .html is asserting over the markup and would
/// pass no matter what happened to the code.</para>
///
/// <para>Joining them here is what keeps those assertions meaning what they meant, and keeps them
/// working whether a given page's code is inline or extracted.</para>
/// </remarks>
internal static class PortalPageSource
{
    private static readonly Regex PageModuleTag =
        new(@"<script\b[^>]*\bsrc=""(/js/pages/[^""?]+)(?:\?[^""]*)?""[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string WwwRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "ETL-SQL.Portal", "wwwroot");

    /// <summary>The page's markup, followed by the source of every page module it loads.</summary>
    public static string Read(string repoRoot, string page)
    {
        var wwwroot = WwwRoot(repoRoot);
        var name = Path.GetFileNameWithoutExtension(page);
        var html = File.ReadAllText(Path.Combine(wwwroot, $"{name}.html"));
        return string.Join("\n", new[] { html }.Concat(ModuleSources(wwwroot, html)));
    }

    /// <summary>The same, for a page already read from disk.</summary>
    public static string WithModules(string wwwroot, string html) =>
        string.Join("\n", new[] { html }.Concat(ModuleSources(wwwroot, html)));

    private static IEnumerable<string> ModuleSources(string wwwroot, string html) =>
        PageModuleTag.Matches(html)
            .Select(match => Path.Combine(wwwroot, match.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(File.ReadAllText);
}
