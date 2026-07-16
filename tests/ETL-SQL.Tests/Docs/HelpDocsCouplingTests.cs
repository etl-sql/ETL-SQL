using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ETL_SQL.Core.Metadata;
using Xunit;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Guards the load-bearing coupling introduced by the docs restructure: pages under
/// <c>docs/reference/**</c> are embedded into <c>ETL-SQL.Core</c> as the runtime help corpus
/// (see the <c>EmbeddedResource</c> block in <c>ETL-SQL.Core.csproj</c>) and served by
/// <see cref="LanguageHelpRegistry"/> to CLI help, LSP hover, and autocomplete.
///
/// These tests fail fast — with an actionable message — when a reference page is renamed or moved
/// out of its embedded category folder, which would otherwise silently drop a help keyword or break
/// the build. Restructure work that touches <c>docs/reference/**</c> filenames must keep these green.
/// </summary>
[Trait("Category", "Docs")]
public sealed class HelpDocsCouplingTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string CoreProjectDir =
        Path.Combine(RepoRoot, "src", "ETL-SQL.Core");

    // Every EmbeddedResource Include in ETL-SQL.Core.csproj that points into the docs/ or snippets/
    // trees must still resolve to at least one file (globs) or an existing file (explicit paths).
    [Fact]
    public void EmbeddedHelpResources_AllResolveOnDisk()
    {
        var csproj = Path.Combine(CoreProjectDir, "ETL-SQL.Core.csproj");
        Assert.True(File.Exists(csproj), $"Expected {csproj}");

        var doc = XDocument.Load(csproj);
        var includes = doc.Descendants("EmbeddedResource")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => v is not null)
            .Select(v => v!.Replace('\\', '/'))
            .Where(v => v.Contains("docs/", StringComparison.OrdinalIgnoreCase)
                     || v.Contains("snippets/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(includes.Count > 0, "No docs/snippets EmbeddedResource entries found — did the csproj move?");

        var unresolved = new List<string>();
        foreach (var include in includes)
        {
            if (!Resolves(include))
                unresolved.Add(include);
        }

        Assert.True(unresolved.Count == 0,
            "EmbeddedResource paths in ETL-SQL.Core.csproj no longer resolve on disk (a reference page "
            + "was renamed/moved/deleted — update the csproj Include and any LanguageHelpRegistry mapping):\n"
            + string.Join("\n", unresolved.Select(u => "  " + u)));
    }

    private static bool Resolves(string include)
    {
        // Includes are relative to the csproj directory (src/ETL-SQL.Core/), e.g. "../../docs/...".
        var starIndex = include.IndexOf('*');
        if (starIndex < 0)
        {
            var fullPath = Path.GetFullPath(Path.Combine(CoreProjectDir, include));
            return File.Exists(fullPath);
        }

        var recursive = include.Contains("**", StringComparison.Ordinal);
        // Base directory is everything before the first wildcard segment.
        var lastSlashBeforeStar = include.LastIndexOf('/', starIndex);
        var baseRel = lastSlashBeforeStar >= 0 ? include[..lastSlashBeforeStar] : ".";
        var filePattern = include[(lastSlashBeforeStar + 1)..]; // e.g. "*.md", "@@*.md", "**/*.md"
        if (recursive)
            filePattern = filePattern[(filePattern.LastIndexOf('/') + 1)..]; // "**/*.md" -> "*.md"

        var baseDir = Path.GetFullPath(Path.Combine(CoreProjectDir, baseRel));
        if (!Directory.Exists(baseDir))
            return false;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(baseDir, filePattern, option).Any();
    }

    // The registry must actually serve help across every embedded category. Uses stable, well-known
    // keyword/page names so a broken embed (emptied category, renamed key page) fails here.
    [Fact]
    public void LanguageHelpRegistry_ServesHelpAcrossAllCategories()
    {
        var registry = new LanguageHelpRegistry();

        // Top-level topics are the keyword/statement pages plus the category parents (FUNCTION,
        // VISUAL, CONNECTION, VARIABLES, REPORT); the large function/connector/visual sets live under
        // their sub-topic maps and are checked below.
        var topics = registry.GetTopics().ToList();
        Assert.True(topics.Count > 50,
            $"LanguageHelpRegistry loaded only {topics.Count} top-level topics — the docs/reference embed is likely broken.");

        // Statements & control flow (KEYWORDS category → topic addressable directly).
        foreach (var keyword in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "IF", "WHILE", "DECLARE", "SHOW", "SET" })
            AssertHelp(registry, keyword, () => registry.GetHelp(keyword));

        // Functions (FUNCTIONS category → FUNCTION/<name>).
        Assert.True(registry.GetSubTopics("FUNCTION").Count() > 100, "FUNCTION help category is nearly empty.");
        AssertHelp(registry, "FUNCTION/UPPER", () => registry.GetHelp("FUNCTION", "UPPER"));

        // Visuals (VISUALS category → VISUAL/<name>).
        Assert.True(registry.GetSubTopics("VISUAL").Any(), "VISUAL help category is empty.");
        AssertHelp(registry, "VISUAL/HBAR", () => registry.GetHelp("VISUAL", "HBAR"));

        // Connectors (CONNECTORS category → CONNECTION/<name>).
        Assert.True(registry.GetSubTopics("CONNECTION").Any(), "CONNECTION help category is empty.");

        // Variables (VARIABLES category → VARIABLES/<name>).
        Assert.True(registry.GetSubTopics("VARIABLES").Any(), "VARIABLES help category is empty.");
    }

    private static void AssertHelp(LanguageHelpRegistry registry, string label, Func<string?> get)
    {
        var help = get();
        Assert.False(string.IsNullOrWhiteSpace(help),
            $"No help served for {label} — its reference page is missing from the embedded help corpus.");
    }
}
