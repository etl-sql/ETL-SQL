using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Docs
{
    /// <summary>
    /// Lints all user-facing documentation for removed or never-valid ETL-SQL syntax patterns.
    ///
    /// This complements <see cref="CookbookVerificationTests"/> (which parses whole runnable recipes):
    /// the reference/guide/help docs are full of intentional fragments and function signatures that
    /// can't be parsed standalone, so a "must parse" check is the wrong tool there. Instead this scans
    /// for the *presence* of specific known-bad constructs — the exact forms the cookbook audit found
    /// shipped in the docs (e.g. CREATE PAGE ... WITH PARAMETERS, EXEC @sql ON conn). Pattern matching
    /// catches them anywhere, including inside fragments and blockquoted code, with near-zero false
    /// positives.
    ///
    /// To extend: when a syntax is removed or corrected, add a <see cref="Rule"/> here so no doc can
    /// reintroduce it. Strategy/ docs are excluded because they intentionally discuss old syntax in
    /// upgrade/migration context.
    /// </summary>
    public class DocForbiddenSyntaxTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private sealed record Rule(string Name, Regex Pattern, string Why, string Fix);

        private static readonly Rule[] Rules =
        {
            new("encrypt-password-overwrite",
                new Regex(@"PASSWORD\s*\([^)]*\bOVERWRITE\b", RegexOptions.IgnoreCase),
                "ENCRYPT/DECRYPT FILE: OVERWRITE cannot live inside PASSWORD(...).",
                "PASSWORD('secret') WITH(OVERWRITE=ON)"),

            new("exec-var-on-conn",
                new Regex(@"\bEXEC(?:UTE)?\s+@\w+\s+ON\b", RegexOptions.IgnoreCase),
                "Dynamic SQL against a connection has no 'ON' form (ParseExecute only accepts AT).",
                "EXEC (@sql) AT conn"),

            new("create-page-with-parameters",
                new Regex(@"\)\s*WITH\s+PARAMETERS\b", RegexOptions.IgnoreCase),
                "CREATE PAGE no longer takes a trailing WITH PARAMETERS clause.",
                "DECLARE @x <TYPE> INPUT = <default> at the top of the script"),

            new("declare-as-type",
                new Regex(@"\bDECLARE\s+@\w+\s+AS\s+[A-Za-z]", RegexOptions.IgnoreCase),
                "DECLARE does not take 'AS' before the type.",
                "DECLARE @x <TYPE> [INPUT] = <value>"),
        };

        [Fact]
        public void Documentation_DoesNotUseRemovedOrInvalidSyntax()
        {
            var findings = new List<string>();

            foreach (var path in DocFiles())
            {
                string text;
                try { text = File.ReadAllText(path).Replace("\r\n", "\n"); } catch { continue; }
                var rel = Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

                foreach (var rule in Rules)
                {
                    foreach (Match m in rule.Pattern.Matches(text))
                    {
                        var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                        var snippet = m.Value.Replace('\n', ' ').Trim();
                        findings.Add($"  {rel}:{line} [{rule.Name}] \"{snippet}\"  =>  use: {rule.Fix}");
                    }
                }
            }

            Assert.True(findings.Count == 0,
                $"Documentation uses removed/invalid ETL-SQL syntax ({findings.Count} occurrence(s)):\n" +
                string.Join("\n", findings) +
                "\n\nRule reasons:\n" +
                string.Join("\n", Rules.Select(r => $"  [{r.Name}] {r.Why}")));
        }

        private static IEnumerable<string> DocFiles()
        {
            var docs = Path.Combine(RepoRoot, "Docs");
            var help = Path.Combine(RepoRoot, "src", "ETL-SQL.Core", "Resources", "Help");
            var strategy = Path.Combine(docs, "Strategy") + Path.DirectorySeparatorChar;

            var files = new List<string>();
            if (Directory.Exists(docs))
                files.AddRange(Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories)
                    .Where(f => !f.StartsWith(strategy, StringComparison.OrdinalIgnoreCase)));
            if (Directory.Exists(help))
                files.AddRange(Directory.GetFiles(help, "*.md", SearchOption.AllDirectories));
            return files.Distinct().OrderBy(f => f, StringComparer.Ordinal);
        }
    }
}
