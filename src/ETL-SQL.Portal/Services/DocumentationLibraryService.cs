using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Portal.Services;

public sealed record DocumentationEntry(
    string Path,
    string Title,
    string Section,
    string Summary);

public sealed record DocumentationSearchResult(
    string Path,
    string Title,
    string Section,
    string Snippet,
    int Score);

public sealed record DocumentationDocument(
    string Path,
    string Title,
    string Markdown);

public sealed class DocumentationLibraryService
{
    private static readonly Regex HeadingRegex = new(@"^\s*#\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private readonly Lazy<IReadOnlyList<DocumentationEntry>> index;

    public DocumentationLibraryService()
    {
        index = new Lazy<IReadOnlyList<DocumentationEntry>>(BuildIndex);
    }

    public IReadOnlyList<DocumentationEntry> GetIndex() => index.Value;

    public IReadOnlyList<DocumentationSearchResult> Search(string? query, int limit = 25) =>
        Search(query, null, limit);

    public IReadOnlyList<DocumentationSearchResult> Search(string? query, string? section, int limit = 25)
    {
        var allEntries = GetIndex();

        if (!string.IsNullOrWhiteSpace(section) && !string.Equals(section, "All", StringComparison.OrdinalIgnoreCase))
        {
            allEntries = allEntries
                .Where(e => string.Equals(e.Section, section, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var terms = SplitTerms(query);
        if (terms.Length == 0)
            return allEntries
                .Take(Math.Clamp(limit, 1, 1000))
                .Select(entry => new DocumentationSearchResult(entry.Path, entry.Title, entry.Section, entry.Summary, 1))
                .ToArray();

        return allEntries
            .Select(entry => Score(entry, terms))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToArray();
    }

    public async Task<DocumentationDocument?> GetDocumentAsync(string relativePath, CancellationToken ct = default)
    {
        var normalized = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        // 1. Check local disk docs/ directory
        var root = FindDocsRoot();
        if (root is not null)
        {
            var full = Path.GetFullPath(Path.Combine(root, normalized));
            var rootFull = Path.GetFullPath(root);
            if (full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            {
                var markdown = await File.ReadAllTextAsync(full, ct);
                return new DocumentationDocument(
                    normalized.Replace('\\', '/'),
                    TitleFromMarkdown(markdown, Path.GetFileNameWithoutExtension(full)),
                    markdown);
            }
        }

        // 2. Check embedded LanguageHelpRegistry
        try
        {
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            var topicName = Path.GetFileNameWithoutExtension(normalized);
            var help = helpRegistry.GetHelp(topicName);

            if (string.IsNullOrWhiteSpace(help) && normalized.Contains('/'))
            {
                var parts = normalized.Split('/');
                help = helpRegistry.GetHelp(parts[0], topicName);
            }

            if (!string.IsNullOrWhiteSpace(help))
            {
                return new DocumentationDocument(
                    normalized.Replace('\\', '/'),
                    TitleFromMarkdown(help, FormatTitle(topicName)),
                    help);
            }
        }
        catch { }

        return null;
    }

    private static IReadOnlyList<DocumentationEntry> BuildIndex()
    {
        var list = new List<DocumentationEntry>();
        var indexedTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Index local disk files
        var root = FindDocsRoot();
        if (root is not null)
        {
            var diskEntries = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                .Where(IsIndexable)
                .Select(path =>
                {
                    indexedTopics.Add(Path.GetFileNameWithoutExtension(path));
                    return BuildEntry(root, path);
                });
            list.AddRange(diskEntries);
        }

        // 2. Index embedded LanguageHelpRegistry topics
        try
        {
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            foreach (var topic in helpRegistry.GetTopics())
            {
                var topHelp = helpRegistry.GetHelp(topic);
                if (!string.IsNullOrWhiteSpace(topHelp))
                {
                    var section = CategorizeTopic(topic);
                    var title = FormatTitle(topic);
                    var summary = SummaryFromMarkdown(topHelp);
                    var path = $"{section}/{topic}.md";
                    if (!indexedTopics.Contains(topic) && !list.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new DocumentationEntry(path, title, section, summary));
                    }
                }

                foreach (var sub in helpRegistry.GetSubTopics(topic))
                {
                    var subHelp = helpRegistry.GetHelp(topic, sub);
                    if (!string.IsNullOrWhiteSpace(subHelp))
                    {
                        var section = CategorizeSubTopic(topic, sub);
                        var title = FormatTitle(sub);
                        var summary = SummaryFromMarkdown(subHelp);
                        var path = $"{section}/{sub}.md";
                        if (!indexedTopics.Contains(sub) && !list.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(new DocumentationEntry(path, title, section, summary));
                        }
                    }
                }
            }
        }
        catch { }

        return list
            .OrderBy(entry => entry.Section, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DocumentationEntry BuildEntry(string root, string path)
    {
        var markdown = File.ReadAllText(path);
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var title = TitleFromMarkdown(markdown, Path.GetFileNameWithoutExtension(path));
        var section = GetSectionForRelativePath(relative);
        var summary = SummaryFromMarkdown(markdown);
        return new DocumentationEntry(relative, title, section, summary);
    }

    private static DocumentationSearchResult Score(DocumentationEntry entry, string[] terms)
    {
        var haystack = $"{entry.Title}\n{entry.Section}\n{entry.Summary}\n{entry.Path}";
        var score = 0;
        foreach (var term in terms)
        {
            if (entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 8;
            if (entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 4;
            if (entry.Summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 2;
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1;
        }

        return new DocumentationSearchResult(entry.Path, entry.Title, entry.Section, entry.Summary, score);
    }

    private static string TitleFromMarkdown(string markdown, string fallback)
    {
        var match = HeadingRegex.Match(markdown);
        return match.Success ? Clean(match.Groups[1].Value) : fallback.Replace('_', ' ');
    }

    private static string SummaryFromMarkdown(string markdown)
    {
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("```", StringComparison.Ordinal))
                continue;
            return Clean(line).Length > 220 ? Clean(line)[..220] + "..." : Clean(line);
        }

        return string.Empty;
    }

    private static string Clean(string value) =>
        value.Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static bool IsIndexable(string path)
    {
        var normalized = path.Replace('\\', '/');
        return !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/release-validation/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private static string[] SplitTerms(string? query) =>
        (query ?? string.Empty)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? FindDocsRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidateDocs = Path.Combine(dir.FullName, "docs");
                if (Directory.Exists(candidateDocs) && (File.Exists(Path.Combine(candidateDocs, "README.md")) || File.Exists(Path.Combine(candidateDocs, "syntax-index.md"))))
                    return candidateDocs;

                var candidateDocsCap = Path.Combine(dir.FullName, "Docs");
                if (Directory.Exists(candidateDocsCap) && (File.Exists(Path.Combine(candidateDocsCap, "README.md")) || File.Exists(Path.Combine(candidateDocsCap, "syntax-index.md"))))
                    return candidateDocsCap;

                dir = dir.Parent;
            }
        }

        return null;
    }

    private static string CategorizeTopic(string topic) => topic.ToUpperInvariant() switch
    {
        "FUNCTION" => "Functions",
        "VISUAL" => "Visuals",
        "CONNECTION" => "Connectors",
        "VARIABLES" => "Variables",
        "REPORT" => "Report-SQL",
        _ => "Keywords"
    };

    private static string CategorizeSubTopic(string topic, string sub) => topic.ToUpperInvariant() switch
    {
        "FUNCTION" => "Functions",
        "VISUAL" => "Visuals",
        "CONNECTION" => "Connectors",
        "VARIABLES" => "Variables",
        "REPORT" => "Report-SQL",
        _ => "Reference"
    };

    private static string FormatTitle(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? raw : raw.Replace('_', ' ');

    private static string GetSectionForRelativePath(string relative)
    {
        var normalized = relative.Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/');
        if (parts.Length == 0) return "General";

        var first = parts[0].ToLowerInvariant();
        if (first == "reference")
        {
            if (parts.Length > 1)
            {
                var sub = parts[1].ToLowerInvariant();
                if (sub.Contains("connector")) return "Connectors";
                if (sub.Contains("function")) return "Functions";
                if (sub.Contains("statement")) return "Keywords";
                if (sub.Contains("variable") || sub.Contains("parameter")) return "Variables";
                if (sub.Contains("visual") || sub.Contains("report")) return "Report-SQL";
            }
            return "Keywords";
        }

        if (first.Equals("spec-import", StringComparison.OrdinalIgnoreCase)) return "Spec Import";
        if (first.Equals("report-sql", StringComparison.OrdinalIgnoreCase)) return "Report-SQL";
        return char.ToUpper(first[0]) + first[1..].ToLowerInvariant();
    }
}
