using System.Text.RegularExpressions;

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

    public IReadOnlyList<DocumentationSearchResult> Search(string? query, int limit = 25)
    {
        var terms = SplitTerms(query);
        if (terms.Length == 0)
            return GetIndex()
                .Take(Math.Clamp(limit, 1, 100))
                .Select(entry => new DocumentationSearchResult(entry.Path, entry.Title, entry.Section, entry.Summary, 1))
                .ToArray();

        return GetIndex()
            .Select(entry => Score(entry, terms))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
    }

    public async Task<DocumentationDocument?> GetDocumentAsync(string relativePath, CancellationToken ct = default)
    {
        var root = FindDocsRoot();
        if (root is null)
            return null;

        var normalized = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;

        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!File.Exists(full))
            return null;

        var markdown = await File.ReadAllTextAsync(full, ct);
        return new DocumentationDocument(
            normalized.Replace('\\', '/'),
            TitleFromMarkdown(markdown, Path.GetFileNameWithoutExtension(full)),
            markdown);
    }

    private static IReadOnlyList<DocumentationEntry> BuildIndex()
    {
        var root = FindDocsRoot();
        if (root is null)
            return Array.Empty<DocumentationEntry>();

        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(IsIndexable)
            .Select(path => BuildEntry(root, path))
            .OrderBy(entry => entry.Section, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DocumentationEntry BuildEntry(string root, string path)
    {
        var markdown = File.ReadAllText(path);
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var title = TitleFromMarkdown(markdown, Path.GetFileNameWithoutExtension(path));
        var section = relative.Contains('/')
            ? relative[..relative.IndexOf('/')]
            : "General";
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
                var candidate = Path.Combine(dir.FullName, "Docs");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "README.md")))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }
}
