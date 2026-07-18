using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Keeps the docs tree link-clean after the IA restructure: every relative Markdown link to a
/// <c>.md</c> page must resolve on disk, and no link may point at the legacy <c>.worktrees/…</c>
/// tree that the restructure retired. These are the automated backstops for the "guide-link churn"
/// risk in <c>docs/DOCUMENTATION_IA_PLAN.md</c> §7 — a move/rename/delete that strands an inbound
/// link fails here with the exact file and target.
/// </summary>
[Trait("Category", "Docs")]
public sealed class DocsLinkIntegrityTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string DocsRoot = Path.Combine(RepoRoot, "docs");

    // Markdown inline links: ](target) — target may carry a #anchor and/or a "title".
    private static readonly Regex LinkPattern = new(@"\]\(([^)]+)\)", RegexOptions.Compiled);

    private static IEnumerable<string> DocFiles() =>
        Directory.EnumerateFiles(DocsRoot, "*.md", SearchOption.AllDirectories);

    // Returns the bare link target: strips a "title", strips a #anchor.
    private static string CleanTarget(string raw)
    {
        var t = raw.Trim();
        int sp = t.IndexOf(' ');
        if (sp >= 0) t = t[..sp];          // drop optional "title"
        int hash = t.IndexOf('#');
        if (hash >= 0) t = t[..hash];      // drop #anchor
        return t;
    }

    [Fact]
    public void AllRelativeMarkdownLinks_ResolveOnDisk()
    {
        Assert.True(Directory.Exists(DocsRoot), $"docs root not found at {DocsRoot}");

        var broken = new List<string>();

        foreach (var file in DocFiles())
        {
            var dir = Path.GetDirectoryName(file)!;
            var text = File.ReadAllText(file);

            foreach (Match m in LinkPattern.Matches(text))
            {
                var target = CleanTarget(m.Groups[1].Value);
                if (target.Length == 0) continue;

                // Only validate relative links to Markdown pages. Skip external/absolute/in-page.
                if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                if (target.StartsWith("/")) continue;

                var resolved = Path.GetFullPath(Path.Combine(dir, target));
                if (!File.Exists(resolved))
                    broken.Add($"{Path.GetRelativePath(RepoRoot, file)} -> {target}");
            }
        }

        Assert.True(broken.Count == 0,
            "Broken relative Markdown links in docs/ (fix the link or restore the target):\n  " +
            string.Join("\n  ", broken));
    }

    [Fact]
    public void NoLinksIntoRetiredWorktreeTree()
    {
        var offenders = new List<string>();

        foreach (var file in DocFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in LinkPattern.Matches(text))
            {
                var target = m.Groups[1].Value;
                if (target.Contains(".worktrees/", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)} -> {CleanTarget(target)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "docs/ links point into the retired .worktrees/ tree; repoint them to the current docs pages:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoAbsoluteFileUrlLinks()
    {
        // Absolute file:/// URLs only resolve on the author's machine. Use repo-relative paths so
        // links work on GitHub and every clone.
        var offenders = new List<string>();

        foreach (var file in DocFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in LinkPattern.Matches(text))
            {
                if (m.Groups[1].Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot, file)} -> {CleanTarget(m.Groups[1].Value)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "docs/ contains absolute file:/// links (only work on the author's machine); make them repo-relative:\n  " +
            string.Join("\n  ", offenders));
    }
}
