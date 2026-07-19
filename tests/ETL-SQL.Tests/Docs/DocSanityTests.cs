using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Connectors.MockDb;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Docs
{
    public class DocSanityTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static readonly Dictionary<(string Connector, string Option), string[]> StrictConnectorOptionValues =
            new()
            {
                [("FLATFILE", "FORMAT")] = new[] { "DELIMITED", "FIXED" },
            };

        private static string RepoFile(string relativePath) =>
            Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        // ── Sample files referenced in Sample_Guide.md all exist on disk ────────

        [Fact]
        public void SampleFiles_ReferencedInSampleGuide_AllExist()
        {
            var sampleGuidePath = RepoFile("docs/guides/sample-guide.md");
            Assert.True(File.Exists(sampleGuidePath), $"Missing: {sampleGuidePath}");

            var content = File.ReadAllText(sampleGuidePath);

            // Match markdown links like [text](../samples/08_Reporting/foo.rptsql)
            var linkPattern = new Regex(@"\]\((\.\./samples/[^)]+)\)", RegexOptions.Compiled);
            var matches = linkPattern.Matches(content);

            var missing = new List<string>();
            foreach (Match m in matches)
            {
                var relPath = m.Groups[1].Value;
                // sample-guide.md is in docs/guides/, so ../../samples/... resolves from docs/guides/
                var fullPath = Path.GetFullPath(Path.Combine(RepoRoot, "docs", "guides", relPath));
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
            var refDir = RepoFile("docs/reference");
            var statementFiles = Directory.GetFiles(Path.Combine(refDir, "statements"), "*.md")
                .Concat(Directory.GetFiles(Path.Combine(refDir, "control-flow"), "*.md"))
                .ToArray();

            Assert.NotEmpty(statementFiles);

            var failures = FindSqlBlockParseFailures(statementFiles);

            Assert.True(failures.Count == 0,
                $"Reference SQL blocks that failed to parse ({failures.Count}):\n" +
                string.Join("\n\n", failures));
        }

        [Fact]
        public void SyntaxIndexAndHelp_SqlBlocks_ParseWithoutSyntaxError()
        {
            var syntaxIndexPath = RepoFile("docs/syntax-index.md");
            var helpDir = RepoFile("docs/reference");
            Assert.True(File.Exists(syntaxIndexPath), $"Missing: {syntaxIndexPath}");
            Assert.True(Directory.Exists(helpDir), $"Missing help dir: {helpDir}");

            var markdownFiles = new[] { syntaxIndexPath }
                .Concat(Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories))
                .ToArray();

            var failures = FindSqlBlockParseFailures(markdownFiles);

            Assert.True(failures.Count == 0,
                $"syntax-index.md/help SQL blocks that failed to parse ({failures.Count}):\n" +
                string.Join("\n\n", failures));
        }

        [Fact]
        public void GeneralDocs_SqlBlocks_ParseWithoutSyntaxError()
        {
            var docsDir = RepoFile("docs");
            var docsFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("architecture"));
            var rootFiles = Directory.GetFiles(RepoRoot, "*.md", SearchOption.TopDirectoryOnly);

            var docFiles = docsFiles.Concat(rootFiles).ToArray();

            var failures = FindSqlBlockParseFailures(docFiles);

            Assert.True(failures.Count == 0,
                $"General documentation SQL blocks that failed to parse ({failures.Count}):\n" +
                string.Join("\n\n", failures));
        }

        [Fact]
        public void CreateConnectionValidation_IsRobustToPollutedGlobalRegistry()
        {
            // Regression for the CI order-dependent failure: a sibling test class installs a reduced
            // mock registry into the mutable static ConnectorRegistry.Instance. Documentation
            // validation must resolve a complete registry from DI, so a polluted static must not make
            // a real connector look "unknown". This deterministically reproduces the pollution that
            // only surfaced under coverage test ordering, so the fast lane catches any regression.
            var original = ConnectorRegistry.Instance;
            try
            {
                ConnectorRegistry.Instance = new ConnectorRegistry(new List<IConnector> { new MockDbConnector() });

                const string sql = "CREATE CONNECTION db AS POSTGRES(HOST='h', DATABASE='d');";
                var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

                var failures = FindUnsupportedConnectionOptions("polluted-registry regression", 1, script).ToList();

                Assert.DoesNotContain(failures, f => f.Contains("unknown connector", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                ConnectorRegistry.Instance = original;
            }
        }

        [Fact]
        public void GeneralDocs_CreateConnectionOptions_AreSupportedByConnector()
        {
            var docsDir = RepoFile("docs");
            var helpDir = RepoFile("docs/reference");
            var markdownFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(RepoRoot, "*.md", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(f =>
                {
                    var parts = f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !parts.Contains("architecture") && !parts.Contains("templates");
                })
                .ToArray();

            var failures = FindUnsupportedConnectionOptions(markdownFiles);

            Assert.True(failures.Count == 0,
                $"Documentation CREATE CONNECTION blocks use unsupported connector options ({failures.Count}):\n" +
                string.Join("\n", failures));
        }

        [Fact]
        public void ConnectorAwareDocValidation_RejectsUnsupportedOptionName()
        {
            const string sql = "CREATE CONNECTION db AS MSSQL(HOST='sql01', DATABASE='Sales');";
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

            var failures = FindUnsupportedConnectionOptions("inline regression example", 1, script);

            Assert.Contains(failures, failure =>
                failure.Contains("MSSQL", StringComparison.OrdinalIgnoreCase) &&
                failure.Contains("HOST", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ConnectorAwareDocValidation_RejectsUnsupportedOptionValue()
        {
            const string sql = "CREATE CONNECTION file AS FLATFILE(PATH='data.csv', FORMAT='CSV');";
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

            var failures = FindUnsupportedConnectionOptions("inline regression example", 1, script);

            Assert.Contains(failures, failure =>
                failure.Contains("FORMAT", StringComparison.OrdinalIgnoreCase) &&
                failure.Contains("CSV", StringComparison.OrdinalIgnoreCase));
        }

        // ── Help files in docs/reference exist and are non-empty ─────────────────

        [Fact]
        public void HelpFiles_AllNonEmpty()
        {
            var helpDir = RepoFile("docs/reference");
            Assert.True(Directory.Exists(helpDir), $"Missing help dir: {helpDir}");

            var files = Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories);
            Assert.NotEmpty(files);

            var empty = files.Where(f => new FileInfo(f).Length == 0).ToList();
            Assert.True(empty.Count == 0,
                $"Empty help files ({empty.Count}):\n" +
                string.Join("\n", empty.Select(f => Path.GetRelativePath(RepoRoot, f))));
        }

        [Fact]
        public void SyntaxIndex_HelpLinksExistOrUseExplicitNoHelpMarker()
        {
            var syntaxIndexPath = RepoFile("docs/syntax-index.md");
            Assert.True(File.Exists(syntaxIndexPath), $"Missing: {syntaxIndexPath}");

            var missing = new List<string>();
            int helpColumnIndex = -1;

            foreach (var (line, lineNo) in File.ReadLines(syntaxIndexPath).Select((line, i) => (line, i + 1)))
            {
                if (!line.StartsWith('|'))
                {
                    helpColumnIndex = -1;
                    continue;
                }

                if (line.Contains(":---"))
                    continue;

                var cells = line.Split('|').Select(c => c.Trim()).ToArray();

                if (line.Contains("Help File", StringComparison.OrdinalIgnoreCase) || line.Contains("Help", StringComparison.OrdinalIgnoreCase))
                {
                    helpColumnIndex = Array.FindIndex(cells, c =>
                        c.Equals("Help File", StringComparison.OrdinalIgnoreCase) ||
                        c.Equals("Help", StringComparison.OrdinalIgnoreCase) ||
                        c.Equals("Help File/Docs", StringComparison.OrdinalIgnoreCase) ||
                        c.Equals("Help File / Docs", StringComparison.OrdinalIgnoreCase));
                    continue;
                }

                if (helpColumnIndex == -1 || helpColumnIndex >= cells.Length)
                    continue;

                var helpCell = cells[helpColumnIndex];
                if (string.IsNullOrEmpty(helpCell) || helpCell == "-" || helpCell.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (Match match in Regex.Matches(helpCell, @"\]\(([^)#]+)(?:#[^)]+)?\)"))
                {
                    var relativeLink = match.Groups[1].Value;
                    if (relativeLink.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(syntaxIndexPath)!, relativeLink));
                    if (!File.Exists(target))
                        missing.Add($"Line {lineNo}: {relativeLink}");
                }
            }

            Assert.True(missing.Count == 0,
                $"syntax-index.md help links missing on disk ({missing.Count}):\n" +
                string.Join("\n", missing));
        }

        [Fact]
        public void ReferenceDocs_DoNotContainStaleRoadmapLanguage()
        {
            var referenceDir = RepoFile("docs/reference");
            Assert.True(Directory.Exists(referenceDir), $"Missing reference dir: {referenceDir}");

            var scannedFiles = Directory.GetFiles(referenceDir, "*.md", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { RepoFile("docs/guides/report-sql.md") })
                .Where(File.Exists)
                .ToArray();

            var stalePatterns = new[]
            {
                new Regex(@"\bbacklog\b", RegexOptions.IgnoreCase),
                new Regex(@"\broadmap\b", RegexOptions.IgnoreCase),
                new Regex(@"\bremaining work\b", RegexOptions.IgnoreCase),
                new Regex(@"\bfuture\b", RegexOptions.IgnoreCase),
                new Regex(@"\bplanned\b", RegexOptions.IgnoreCase),
                new Regex(@"\bnot yet implemented\b", RegexOptions.IgnoreCase),
                new Regex(@"\bphase\s+\d+\b", RegexOptions.IgnoreCase),
            };

            var findings = new List<string>();
            foreach (var path in scannedFiles)
            {
                var relativePath = Path.GetRelativePath(RepoRoot, path);
                foreach (var (line, lineNo) in File.ReadLines(path).Select((line, i) => (line, i + 1)))
                {
                    if (stalePatterns.Any(pattern => pattern.IsMatch(line)))
                        findings.Add($"{relativePath}:{lineNo}: {line.Trim()}");
                }
            }

            Assert.True(findings.Count == 0,
                $"Reference docs contain roadmap/backlog language that belongs in strategy/TODO docs ({findings.Count}):\n" +
                string.Join("\n", findings));
        }

        [Fact]
        public void HelpFiles_LinkBackToCanonicalReferenceDocs()
        {
            var helpDir = RepoFile("docs/reference");
            Assert.True(Directory.Exists(helpDir), $"Missing help dir: {helpDir}");

            // Canonical navigation targets. Post-restructure the "canonical guide" a help page links
            // back to is its folder README or one of the top-level indexes (README-as-index model),
            // not the retired monolith docs (standard-library.md, grammar.md).
            var canonicalReferences = new[]
            {
                "docs/syntax-index.md",
                "docs/task-index.md",
                "docs/reference/README.md",
                "docs/reference/functions/README.md",
                "docs/reference/statements/README.md",
                "docs/reference/connectors/README.md",
                "docs/reference/connectors/data-connectors.md",
                "docs/reference/cli/README.md",
                "docs/reference/file-operations/README.md",
                "docs/reference/performance/performance.md",
                "docs/reference/configuration/settings.md",
                "docs/reference/portal-admin/README.md",
                "docs/reference/portal-admin/service-accounts.md",
                "docs/reference/visuals-reporting/README.md",
                "docs/reference/data-types.md",
                "docs/administration/platform/README.md",
                "docs/administration/portal/README.md",
                "docs/administration/orchestration/README.md",
                "docs/guides/getting-started.md",
                "docs/guides/report-sql.md"
            };

            var missing = new List<string>();
            var helpFiles = Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories)
                .Where(p => !Path.GetFileName(p).Equals("README.md", StringComparison.OrdinalIgnoreCase));

            foreach (var path in helpFiles)
            {
                var content = File.ReadAllText(path).Replace('\\', '/');
                var helpFileDir = Path.GetDirectoryName(path)!;

                bool hasLink = false;
                foreach (var canonicalReference in canonicalReferences)
                {
                    var canonicalFullPath = Path.GetFullPath(Path.Combine(RepoRoot, canonicalReference));
                    var expectedRelativeLink = Path.GetRelativePath(helpFileDir, canonicalFullPath).Replace('\\', '/');
                    if (content.Contains(expectedRelativeLink, StringComparison.OrdinalIgnoreCase) ||
                        content.Contains(canonicalReference, StringComparison.OrdinalIgnoreCase))
                    {
                        hasLink = true;
                        break;
                    }
                }

                // A link to the page's folder README/index (or any *-index) also counts as linking
                // back to a canonical navigation doc — the README-as-index navigation model.
                if (!hasLink &&
                    (content.Contains("README.md)", StringComparison.OrdinalIgnoreCase) ||
                     content.Contains("index.md)", StringComparison.OrdinalIgnoreCase)))
                {
                    hasLink = true;
                }

                if (!hasLink)
                {
                    missing.Add(Path.GetRelativePath(RepoRoot, path));
                }
            }

            Assert.True(missing.Count == 0,
                $"Help files missing a link to any canonical reference guide ({missing.Count}):\n" +
                string.Join("\n", missing));
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static IReadOnlyList<string> ExtractSqlBlocks(string markdown)
        {
            var results = new List<string>();
            var fencePattern = new Regex(@"```(?:sql|etlsql|rptsql)\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in fencePattern.Matches(markdown))
                results.Add(m.Groups[1].Value);
            return results;
        }

        private static List<string> FindSqlBlockParseFailures(IEnumerable<string> markdownFiles)
        {
            var failures = new List<string>();

            foreach (var path in markdownFiles)
            {
                var relativePath = Path.GetRelativePath(RepoRoot, path);
                var blocks = ExtractSqlBlocks(File.ReadAllText(path));
                foreach (var (block, index) in blocks.Select((b, i) => (b, i)))
                {
                    var trimmed = block.Trim();
                    if (ShouldSkipSqlBlock(trimmed))
                        continue;

                    try
                    {
                        var tokens = new Lexer(trimmed).Tokenize();
                        new Parser(tokens, trimmed).Parse();
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{relativePath} block #{index + 1}: [{ex.GetType().Name}] {ex.Message}\n  SQL: {Truncate(trimmed, 120)}");
                    }
                }
            }

            return failures;
        }

        private static List<string> FindUnsupportedConnectionOptions(IEnumerable<string> markdownFiles)
        {
            var failures = new List<string>();

            foreach (var path in markdownFiles)
            {
                var relativePath = Path.GetRelativePath(RepoRoot, path);
                var blocks = ExtractSqlBlocks(File.ReadAllText(path));
                foreach (var (block, index) in blocks.Select((b, i) => (b, i)))
                {
                    var trimmed = block.Trim();
                    if (ShouldSkipSqlBlock(trimmed))
                        continue;

                    try
                    {
                        var script = new Parser(new Lexer(trimmed).Tokenize(), trimmed).Parse();
                        failures.AddRange(FindUnsupportedConnectionOptions(relativePath, index + 1, script));
                    }
                    catch
                    {
                        // Parse failures are reported by the syntax-focused documentation tests.
                    }
                }
            }

            return failures;
        }

        private static IEnumerable<string> FindUnsupportedConnectionOptions(string source, int blockNumber, Script script)
        {
            // Resolve the full registry from the test service provider's DI singleton rather than the
            // mutable static ConnectorRegistry.Instance. Other test classes reassign that static to a
            // reduced mock registry (e.g. SuggestTests), so reading it made this test order-dependent —
            // every real connector showed as "unknown" when it ran after the polluter (seen in CI under
            // coverage ordering). The DI singleton is built from all connectors and is unaffected.
            var registry = ETL_SQL.Program.ServiceProvider?.GetService<IConnectorRegistry>()
                ?? throw new InvalidOperationException("Connector registry was not initialized for documentation tests.");

            foreach (var statement in EnumerateStatements(script.Statements))
            {
                if (statement is not CreateConnectionStatement create ||
                    string.IsNullOrWhiteSpace(create.ConnectionType) ||
                    create.Options == null)
                {
                    continue;
                }

                var connector = registry.GetConnector(create.ConnectionType);
                if (connector == null)
                {
                    yield return $"{source} block #{blockNumber}: unknown connector '{create.ConnectionType}'.";
                    continue;
                }

                var supported = connector.GetSupportedOptions().Keys
                    .Append("TEMPLATE")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var option in create.Options.Keys.Where(option => !supported.Contains(option)))
                {
                    yield return $"{source} block #{blockNumber}: {create.ConnectionType} does not support option '{option}'.";
                }

                foreach (var (option, expression) in create.Options)
                {
                    if (!StrictConnectorOptionValues.TryGetValue(
                            (connector.Name.ToUpperInvariant(), option.ToUpperInvariant()),
                            out var allowedValues) ||
                        !TryGetStaticOptionValue(expression, out var actualValue) ||
                        allowedValues.Contains(actualValue, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    yield return $"{source} block #{blockNumber}: {create.ConnectionType} option '{option}' " +
                        $"does not support value '{actualValue}'. Expected one of: {string.Join(", ", allowedValues)}.";
                }
            }
        }

        private static bool TryGetStaticOptionValue(Expression expression, out string value)
        {
            switch (expression)
            {
                case LiteralExpression literal when literal.Value != null:
                    value = Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    return true;
                case IdentifierExpression identifier:
                    value = identifier.Name;
                    return true;
                default:
                    value = "";
                    return false;
            }
        }

        private static IEnumerable<Statement> EnumerateStatements(IEnumerable<Statement> statements)
        {
            foreach (var statement in statements)
            {
                yield return statement;

                if (statement is BlockStatement block)
                {
                    foreach (var nested in EnumerateStatements(block.Statements))
                        yield return nested;
                }
                else if (statement is TryCatchStatement tryCatch)
                {
                    foreach (var nested in EnumerateStatements(new[] { tryCatch.TryBody, tryCatch.CatchBody }))
                        yield return nested;
                }
            }
        }

        private static bool ShouldSkipSqlBlock(string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return true;

            // Skip template/placeholder snippets and unquoted ellipses. Quoted values
            // such as PASSWORD='...' remain valid examples and should still be checked.
            var withoutQuotedStrings = Regex.Replace(trimmed, @"'(?:''|[^'])*'", "''");
            if (withoutQuotedStrings.Contains("...") ||
                Regex.IsMatch(trimmed, @"<[a-zA-Z_][a-zA-Z0-9_\-\s]*>") ||
                trimmed.Contains('{') ||
                trimmed.Contains('}'))
                return true;

            // Skip HTML block structures (e.g. text visuals with raw HTML snippets)
            if (trimmed.StartsWith("<") && trimmed.Contains(">") && (trimmed.Contains("</") || trimmed.Contains("/>")))
                return true;

            var fragmentLeaders = new[]
            {
                "WHERE ", "AND ", "OR ", "JOIN ", "ON ", "GROUP ", "ORDER ", "HAVING ",
                "UNION", "EXCEPT", "INTERSECT", "FROM ", "WHEN ", "ELSE ", "THEN "
            };

            return fragmentLeaders.Any(kw => trimmed.StartsWith(kw, StringComparison.OrdinalIgnoreCase));
        }

        private static string Truncate(string s, int maxLen) =>
            s.Length <= maxLen ? s : s[..maxLen] + "…";

        [Fact]
        public void MarkdownLinks_AllResolveCleanly()
        {
            var mdFiles = Directory.GetFiles(RepoRoot, "*.md", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var parts = f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !parts.Contains("Docs_Legacy") &&
                           !parts.Contains("node_modules") &&
                           !parts.Contains(".git") &&
                           !parts.Contains("bin") &&
                           !parts.Contains("obj") &&
                           !parts.Contains("Help_Legacy") &&
                           !parts.Contains(".claude") &&
                           !parts.Contains(".worktrees") &&
                           !parts.Contains(".vscode-test") &&
                           !f.EndsWith("TEMPLATE.md", StringComparison.OrdinalIgnoreCase) &&
                           !f.EndsWith("CLAUDE.md", StringComparison.OrdinalIgnoreCase) &&
                           !f.EndsWith("GEMINI.md", StringComparison.OrdinalIgnoreCase) &&
                           !f.EndsWith("AGENTS.md", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            var brokenLinks = new List<string>();
            var linkRegex = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

            foreach (var file in mdFiles)
            {
                var relativePath = Path.GetRelativePath(RepoRoot, file);
                var content = File.ReadAllText(file);
                var matches = linkRegex.Matches(content);
                var fileDir = Path.GetDirectoryName(file)!;

                foreach (Match match in matches)
                {
                    var link = match.Groups[2].Value;

                    // Skip absolute, mailto, or page-internal anchor links
                    if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                        link.StartsWith("#"))
                    {
                        continue;
                    }

                    // Strip any anchor fragment
                    var cleanLink = link;
                    var hashIdx = link.IndexOf('#');
                    if (hashIdx != -1)
                    {
                        cleanLink = link.Substring(0, hashIdx);
                    }

                    // Handle file:/// URL scheme
                    if (cleanLink.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileUriPath = cleanLink.Substring(8).Replace('/', Path.DirectorySeparatorChar);
                        if (Path.IsPathRooted(fileUriPath) && File.Exists(fileUriPath))
                        {
                            continue;
                        }
                        var rootResolved = Path.GetFullPath(Path.Combine(RepoRoot, fileUriPath));
                        if (File.Exists(rootResolved))
                        {
                            continue;
                        }
                        brokenLinks.Add($"{relativePath}: Broken file link: {link}");
                        continue;
                    }

                    // Try to resolve relative to current file
                    var targetPath = Path.GetFullPath(Path.Combine(fileDir, cleanLink.Replace('/', Path.DirectorySeparatorChar)));
                    if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                    {
                        brokenLinks.Add($"{relativePath}: Broken relative link: {link}");
                    }
                }
            }

            Assert.True(brokenLinks.Count == 0,
                $"Found broken relative markdown links in repository ({brokenLinks.Count}):\n" +
                string.Join("\n", brokenLinks));
        }

        [Fact]
        public void EveryDirectoryWithMoreThanFiveMarkdownFiles_HasAReadme()
        {
            var docsDir = RepoFile("docs");
            Assert.True(Directory.Exists(docsDir), $"Missing docs dir: {docsDir}");

            var missing = new List<string>();
            foreach (var dir in Directory.GetDirectories(docsDir, "*", SearchOption.AllDirectories))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.Equals("assets", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly);
                if (mdFiles.Length > 5)
                {
                    var readmePath = Path.Combine(dir, "README.md");
                    if (!File.Exists(readmePath))
                    {
                        missing.Add(Path.GetRelativePath(RepoRoot, dir));
                    }
                }
            }

            Assert.True(missing.Count == 0,
                $"The following directories contain >5 markdown files but lack a README.md index ({missing.Count}):\n" +
                string.Join("\n", missing));
        }
    }
}
