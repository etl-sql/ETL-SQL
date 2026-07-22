using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Core.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/docs")]
[AllowAnonymous]
public class DocsController : ControllerBase
{
    private static readonly Lazy<List<DocItem>> CachedDocs = new(BuildDocsCatalog);

    public sealed record DocItem(string Path, string Title, string Section, string Markdown);
    public sealed record DocSearchResult(string Path, string Title, string Section, string Snippet);

    [HttpGet("search")]
    public IActionResult Search([FromQuery] string? q = null, [FromQuery] string? section = null, [FromQuery] int limit = 100)
    {
        var items = CachedDocs.Value.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(section) && !string.Equals(section, "All", StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(i => string.Equals(i.Section, section, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var query = q.Trim();
            items = items
                .Select(i => new { Item = i, Score = CalculateScore(i, query) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Title)
                .Select(x => x.Item);
        }
        else
        {
            items = items.OrderBy(i => i.Section).ThenBy(i => i.Title);
        }

        var results = items.Take(Math.Clamp(limit, 1, 300)).Select(i => new DocSearchResult(
            i.Path,
            i.Title,
            i.Section,
            GetSnippet(i.Markdown, q)
        ));

        return Ok(results);
    }

    [HttpGet("document")]
    public IActionResult GetDocument([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Path is required." });

        var doc = CachedDocs.Value.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
            return NotFound(new { error = "Document not found." });

        return Ok(new { path = doc.Path, title = doc.Title, section = doc.Section, markdown = doc.Markdown });
    }

    private static int CalculateScore(DocItem item, string query)
    {
        int score = 0;
        if (string.Equals(item.Title, query, StringComparison.OrdinalIgnoreCase)) score += 100;
        else if (item.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 60;
        else if (item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 40;

        if (item.Section.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 20;
        if (item.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 10;

        return score;
    }

    private static string GetSnippet(string markdown, string? query)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("#") && !l.StartsWith("```") && !l.StartsWith("- ["))
            .ToList();

        var firstText = lines.FirstOrDefault(l => l.Length > 10) ?? lines.FirstOrDefault() ?? string.Empty;
        if (firstText.Length > 140) firstText = firstText[..140] + "…";
        return firstText;
    }

    private static List<DocItem> BuildDocsCatalog()
    {
        var list = new List<DocItem>();
        var helpRegistry = new LanguageHelpRegistry();

        // 1. Load all embedded help topics from LanguageHelpRegistry
        var topics = helpRegistry.GetTopics().ToList();
        foreach (var topic in topics)
        {
            var topHelp = helpRegistry.GetHelp(topic);
            if (!string.IsNullOrWhiteSpace(topHelp))
            {
                var section = CategorizeTopic(topic);
                var title = FormatTitle(topic);
                list.Add(new DocItem($"{section}/{topic}", title, section, topHelp));
            }

            foreach (var sub in helpRegistry.GetSubTopics(topic))
            {
                var subHelp = helpRegistry.GetHelp(topic, sub);
                if (!string.IsNullOrWhiteSpace(subHelp))
                {
                    var section = CategorizeSubTopic(topic, sub);
                    var title = FormatTitle(sub);
                    list.Add(new DocItem($"{section}/{sub}", title, section, subHelp));
                }
            }
        }

        // 2. Also index markdown files in repository docs/ folder if available
        try
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            string? docsRoot = null;
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "docs");
                if (Directory.Exists(candidate) && System.IO.File.Exists(Path.Combine(candidate, "syntax-index.md")))
                {
                    docsRoot = candidate;
                    break;
                }
                current = current.Parent;
            }

            if (docsRoot is not null)
            {
                var files = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var relative = Path.GetRelativePath(docsRoot, file).Replace('\\', '/');
                        var content = System.IO.File.ReadAllText(file);
                        var firstLine = content.Split('\n').FirstOrDefault(l => l.StartsWith("#"))?.Trim('#', ' ') ?? Path.GetFileNameWithoutExtension(file);
                        var section = relative.Contains('/') ? relative.Split('/')[0].Replace('-', ' ') : "Guides";
                        section = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(section);

                        if (!list.Any(x => string.Equals(x.Path, relative, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(new DocItem(relative, firstLine, section, content));
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return list;
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

    private static string FormatTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        return raw.Replace('_', ' ');
    }
}
