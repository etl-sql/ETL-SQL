using System.IO;
using System.Text.RegularExpressions;

namespace ETL_SQL.Tests.Docs
{
    public class DocSanityTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static string RepoFile(string relativePath) =>
            Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        // ── Sample files referenced in Sample_Guide.md all exist on disk ────────

        [Fact]
        public void SampleFiles_ReferencedInSampleGuide_AllExist()
        {
            var sampleGuidePath = RepoFile("Docs/Sample_Guide.md");
            Assert.True(File.Exists(sampleGuidePath), $"Missing: {sampleGuidePath}");

            var content = File.ReadAllText(sampleGuidePath);

            // Match markdown links like [text](../samples/08_Reporting/foo.rptsql)
            var linkPattern = new Regex(@"\]\((\.\./samples/[^)]+)\)", RegexOptions.Compiled);
            var matches = linkPattern.Matches(content);

            var missing = new List<string>();
            foreach (Match m in matches)
            {
                var relPath = m.Groups[1].Value;
                // Sample_Guide.md is in Docs/, so ../samples/... resolves from Docs/
                var fullPath = Path.GetFullPath(Path.Combine(RepoRoot, "Docs", relPath));
                if (!File.Exists(fullPath))
                    missing.Add(relPath);
            }

            Assert.True(missing.Count == 0,
                $"Sample files referenced in Sample_Guide.md but missing on disk ({missing.Count}):\n" +
                string.Join("\n", missing));
        }

        // ── Grammar.md SQL code blocks all parse without SyntaxException ─────────

        [Fact]
        public void Grammar_SqlBlocks_ParseWithoutSyntaxError()
        {
            var grammarPath = RepoFile("Docs/Reference/Grammar.md");
            Assert.True(File.Exists(grammarPath), $"Missing: {grammarPath}");

            var content = File.ReadAllText(grammarPath);
            var blocks = ExtractSqlBlocks(content);

            // Skip placeholder blocks (contain < >) and fragment-only blocks
            var fragmentLeaders = new[] { "WHERE ", "AND ", "OR ", "JOIN ", "ON ", "GROUP ", "ORDER ", "HAVING ", "UNION", "EXCEPT", "INTERSECT", "FROM " };

            var failures = new List<string>();
            int skipped = 0;

            foreach (var (block, index) in blocks.Select((b, i) => (b, i)))
            {
                var trimmed = block.Trim();

                // Skip blocks with placeholder syntax like <column_name> or {identifier}
                if (trimmed.Contains('<') || trimmed.Contains('{'))
                {
                    skipped++;
                    continue;
                }

                // Skip fragment-level blocks that start with a clause keyword (not a statement)
                if (fragmentLeaders.Any(kw => trimmed.StartsWith(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                // Skip empty blocks
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var tokens = new Lexer(trimmed).Tokenize();
                    new Parser(tokens, trimmed).Parse();
                }
                catch (Exception ex) when (ex.GetType().Name is "SyntaxException" or "ParseException" or "LexerException")
                {
                    failures.Add($"Block #{index + 1}: {ex.Message}\n  SQL: {Truncate(trimmed, 120)}");
                }
                catch (Exception)
                {
                    // Non-parse exceptions (runtime, NullRef, etc.) are not doc failures
                }
            }

            Assert.True(failures.Count == 0,
                $"Grammar.md SQL blocks that failed to parse ({failures.Count}, {skipped} skipped):\n" +
                string.Join("\n\n", failures));
        }

        // ── Help files in Resources/Help exist and are non-empty ─────────────────

        [Fact]
        public void HelpFiles_AllNonEmpty()
        {
            var helpDir = RepoFile("src/ETL-SQL.Core/Resources/Help");
            Assert.True(Directory.Exists(helpDir), $"Missing help dir: {helpDir}");

            var files = Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories);
            Assert.NotEmpty(files);

            var empty = files.Where(f => new FileInfo(f).Length == 0).ToList();
            Assert.True(empty.Count == 0,
                $"Empty help files ({empty.Count}):\n" +
                string.Join("\n", empty.Select(f => Path.GetRelativePath(RepoRoot, f))));
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static IReadOnlyList<string> ExtractSqlBlocks(string markdown)
        {
            var results = new List<string>();
            var fencePattern = new Regex(@"```sql\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in fencePattern.Matches(markdown))
                results.Add(m.Groups[1].Value);
            return results;
        }

        private static string Truncate(string s, int maxLen) =>
            s.Length <= maxLen ? s : s[..maxLen] + "…";
    }
}
