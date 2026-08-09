using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Docs
{
    /// <summary>
    /// Verifies that every ```sql recipe in the cookbooks actually parses, so we never ship a
    /// broken example. Unlike <see cref="DocSanityTests"/> (which skips any block containing "...",
    /// "{", "}", or a &lt;placeholder&gt;, silently dropping most connection-string recipes), this
    /// test checks every block and keys exceptions on a content hash via <see cref="KnownBroken"/>.
    /// Each recipe is its own theory case keyed by file:line so a failure points at the exact block.
    ///
    /// Workflow for the punch-list: fixing a recipe changes its content hash, so its entry no longer
    /// matches any block — <see cref="KnownBroken_HasNoStaleEntries"/> then fails until the entry is
    /// removed, and the now-unlisted recipe is held to the strict "must parse" bar. So the list can
    /// only shrink, never silently rot.
    /// </summary>
    public class CookbookVerificationTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        // Directories, not a fixed file list: every recipe is its own file, and a new one has to be
        // covered the moment it is added rather than when somebody remembers to list it here.
        private static readonly string[] CookbookDirectories =
        {
            "docs/cookbooks/etl",
            "docs/cookbooks/report",
        };

        // Recipes that do not yet parse, keyed by content hash (stable across line shifts; changes the
        // moment the recipe is edited). Burn this down — every entry is a known-broken published example.
        private static readonly Dictionary<string, string> KnownBroken = new();

        public static IEnumerable<object[]> CookbookBlocks()
        {
            var any = false;
            foreach (var rel in CookbookDirectories)
            {
                var dir = Path.Combine(RepoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir))
                {
                    yield return new object[] { $"MISSING:{rel}", string.Empty };
                    any = true;
                    continue;
                }

                foreach (var path in Directory.GetFiles(dir, "*.md").OrderBy(p => p, StringComparer.Ordinal))
                {
                    // The collection index is prose and links, not recipes.
                    if (string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var (id, code) in ExtractSqlBlocks(File.ReadAllText(path), Path.GetFileName(path)))
                    {
                        any = true;
                        yield return new object[] { id, code };
                    }
                }
            }

            if (!any)
                yield return new object[] { "NO-COOKBOOK-BLOCKS", string.Empty };
        }

        [Theory]
        [MemberData(nameof(CookbookBlocks))]
        public void CookbookRecipe_ParsesWithoutError(string id, string code)
        {
            Assert.False(id.StartsWith("MISSING:"), $"Cookbook file not found: {id}");
            Assert.NotEqual("NO-COOKBOOK-BLOCKS", id);

            var errors = ParseErrors(code);
            var hash = Hash(code);

            if (KnownBroken.TryGetValue(hash, out var reason))
            {
                // Self-cleaning guard: a recipe on the punch-list must still actually be broken.
                Assert.True(errors.Count > 0,
                    $"{id} (hash {hash}) is listed in KnownBroken but now parses cleanly. " +
                    $"Remove it from KnownBroken. Listed reason: {reason}");
                return;
            }

            Assert.True(errors.Count == 0,
                $"{id} failed to parse ({errors.Count} error(s)):\n" +
                string.Join("\n", errors.Select(e => $"  line {e.Line}, col {e.Column} [{e.Code}]: {e.Message}")) +
                $"\n--- recipe ---\n{code}");
        }

        [Fact]
        public void KnownBroken_HasNoStaleEntries()
        {
            var liveHashes = new HashSet<string>();
            foreach (var rel in CookbookDirectories)
            {
                var dir = Path.Combine(RepoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                foreach (var path in Directory.GetFiles(dir, "*.md"))
                {
                    if (string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var (_, code) in ExtractSqlBlocks(File.ReadAllText(path), Path.GetFileName(path)))
                        liveHashes.Add(Hash(code));
                }
            }

            var stale = KnownBroken.Keys.Where(h => !liveHashes.Contains(h)).ToList();
            Assert.True(stale.Count == 0,
                "KnownBroken contains stale entries (the recipe was edited or removed - delete these):\n" +
                string.Join("\n", stale.Select(h => $"  {h}: {KnownBroken[h]}")));
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static List<Diagnostic> ParseErrors(string code)
        {
            var tokens = new Lexer(code).Tokenize();
            var script = new Parser(tokens, code).Parse();
            return script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        }

        private static string Hash(string code)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        }

        /// <summary>
        /// Yields (id, code) for every ```sql fenced block. The id is "File.md:NN" where NN is the
        /// 1-based line of the first content line, so a failing theory case names the exact recipe.
        /// </summary>
        private static IEnumerable<(string Id, string Code)> ExtractSqlBlocks(string markdown, string fileName)
        {
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().ToLowerInvariant() is not ("```sql" or "```etlsql" or "```rptsql"))
                    continue;

                var body = new List<string>();
                var j = i + 1;
                for (; j < lines.Length; j++)
                {
                    if (lines[j].TrimStart() == "```") break;
                    body.Add(lines[j]);
                }

                yield return ($"{fileName}:{i + 2}", string.Join("\n", body));
                i = j; // resume after the closing fence
            }
        }
    }
}
