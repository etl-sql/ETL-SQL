using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Metadata;

public record SnippetDef(
    string Trigger,
    string Label,
    string Description,
    string TuiBody,
    string LspBody
);

public class SnippetLibrary
{
    private static string? _userSnippetsPath;
    private static readonly Lazy<SnippetLibrary> _instance = new(() => new SnippetLibrary());
    public static SnippetLibrary Instance => _instance.Value;

    private readonly IReadOnlyList<SnippetDef> _snippets;

    // Call once at startup (before Instance is first accessed) to enable user snippets.
    public static void Initialize(string? userSnippetsPath)
    {
        _userSnippetsPath = userSnippetsPath;
    }

    public SnippetLibrary() => _snippets = Load(_userSnippetsPath);

    public SnippetLibrary(string? userSnippetsPath) => _snippets = Load(userSnippetsPath);

    public IReadOnlyList<SnippetDef> GetAll() => _snippets;

    public IEnumerable<SnippetDef> GetByPrefix(string prefix) =>
        _snippets.Where(s => s.Trigger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SnippetDef> Load(string? userSnippetsPath)
    {
        var assembly = typeof(SnippetLibrary).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("ETL_SQL.Core.Resources.Help.Snippets.") && n.EndsWith(".md"));

        var result = new List<SnippetDef>();
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var def = ParseSnippet(reader.ReadToEnd());
            if (def != null) result.Add(def);
        }

        // User snippets: any .md file in the configured directory is loaded and merged.
        // User snippets with the same trigger as a built-in override the built-in.
        if (!string.IsNullOrWhiteSpace(userSnippetsPath) && Directory.Exists(userSnippetsPath))
        {
            foreach (var file in Directory.EnumerateFiles(userSnippetsPath, "*.md", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var def = ParseSnippet(File.ReadAllText(file));
                    if (def == null) continue;
                    var existing = result.FindIndex(s => s.Trigger.Equals(def.Trigger, StringComparison.OrdinalIgnoreCase));
                    if (existing >= 0)
                        result[existing] = def;
                    else
                        result.Add(def);
                }
                catch (IOException) { }
            }
        }

        return result.OrderBy(s => s.Trigger).ToList();
    }

    public static SnippetDef? ParseSnippet(string content)
    {
        if (!content.StartsWith("---")) return null;

        var end = content.IndexOf("\n---", 3);
        if (end < 0) return null;

        var frontmatter = content.Substring(3, end - 3).Trim();
        var body = content.Substring(end + 4).TrimStart('\r', '\n');

        string? trigger = null, label = null, description = null;
        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("trigger:"))
                trigger = line.Substring("trigger:".Length).Trim();
            else if (line.StartsWith("label:"))
                label = line.Substring("label:".Length).Trim();
            else if (line.StartsWith("description:"))
                description = line.Substring("description:".Length).Trim();
        }

        if (trigger == null || label == null) return null;

        var tuiBody = body.TrimEnd();
        var lspBody = ConvertToLspTabStops(tuiBody);

        return new SnippetDef(trigger, label, description ?? label, tuiBody, lspBody);
    }

    // «placeholder text» → ${N:placeholder text}
    public static string ConvertToLspTabStops(string body)
    {
        int n = 0;
        return Regex.Replace(body, @"«([^»]*)»", m => $"${{{++n}:{m.Groups[1].Value}}}");
    }
}
