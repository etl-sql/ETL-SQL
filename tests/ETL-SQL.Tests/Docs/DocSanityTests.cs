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
            var sampleGuidePath = RepoFile("docs/guides/patterns/sample-guide.md");
            Assert.True(File.Exists(sampleGuidePath), $"Missing: {sampleGuidePath}");

            var content = File.ReadAllText(sampleGuidePath);

            // Match markdown links like [text](../samples/08_Reporting/foo.rptsql)
            var linkPattern = new Regex(@"\]\((\.\./\.\./\.\./samples/[^)]+)\)", RegexOptions.Compiled);
            var matches = linkPattern.Matches(content);

            var missing = new List<string>();
            foreach (Match m in matches)
            {
                var relPath = m.Groups[1].Value;
                var fullPath = Path.GetFullPath(Path.Combine(
                    RepoRoot, "docs", "guides", "patterns", relPath));
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
            var statementFiles = Directory.GetFiles(Path.Combine(refDir, "statements"), "*.md", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(refDir, "control-flow"), "*.md", SearchOption.AllDirectories))
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

        /// <summary>
        /// Documents that describe <em>proposed</em> syntax rather than shipped behavior. Their SQL
        /// blocks deliberately reference options and statement forms that do not exist yet, so
        /// validating them against the current connector metadata would make planning work fail the
        /// release gate. Keep this list to genuinely forward-looking documents — anything describing
        /// behavior a user can actually run belongs in the validated set.
        /// </summary>
        private static readonly HashSet<string> ForwardLookingDocs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ROADMAP.md",
                // Planning work moves ROADMAP -> TODO when a track starts, carrying its proposed
                // syntax with it. The exemption follows the document's purpose, not its filename:
                // neither file describes behavior a user can run today.
                "TODO.md",
            };

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
                    return !parts.Contains("architecture")
                        && !parts.Contains("templates")
                        && !ForwardLookingDocs.Contains(Path.GetFileName(f));
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
                "docs/reference/portal-commands/README.md",
                "docs/reference/portal-commands/service-accounts.md",
                "docs/reference/visuals-reporting/README.md",
                "docs/reference/data-types.md",
                "docs/administration/platform/README.md",
                "docs/administration/portal/README.md",
                "docs/administration/orchestration/README.md",
                "docs/guides/onboarding/getting-started.md",
                "docs/guides/feature-guides/report-sql.md"
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
                    var trimmed = NormalizeSqlBlock(block);
                    if (ShouldSkipSqlBlock(relativePath, trimmed))
                        continue;

                    try
                    {
                        var tokens = new Lexer(trimmed).Tokenize();
                        var script = new Parser(tokens, trimmed).Parse();
                        foreach (var diagnostic in script.Diagnostics)
                        {
                            failures.Add(
                                $"{relativePath} block #{index + 1}: [{diagnostic.Code}] {diagnostic.Message}\n" +
                                $"  SQL: {Truncate(trimmed, 120)}");
                        }
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
                    var trimmed = NormalizeSqlBlock(block);
                    if (ShouldSkipSqlBlock(relativePath, trimmed))
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

        private static string NormalizeSqlBlock(string block)
        {
            // Some guides use Markdown quote markers inside fenced examples. They are presentation
            // markup, not ETL-SQL, so remove them before sending the example to the production parser.
            return Regex.Replace(block.Trim(), @"(?m)^\s*> ?", "");
        }

        private static bool ShouldSkipSqlBlock(string relativePath, string trimmed)
        {
            if (string.IsNullOrWhiteSpace(trimmed))
                return true;

            var normalizedPath = relativePath.Replace('\\', '/');
            if (normalizedPath.StartsWith("docs/releases/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith("docs/templates/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.EndsWith("migration-guide.md", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath is "ROADMAP.md" or "TODO.md" or "AGENTS.md")
                return true;

            // Skip template/placeholder snippets and unquoted ellipses. Quoted values
            // such as PASSWORD='...' remain valid examples and should still be checked.
            var withoutQuotedStrings = Regex.Replace(trimmed, @"'(?:''|[^'])*'", "''");
            var withoutInterpolations = Regex.Replace(withoutQuotedStrings, @"\$\{@?[a-zA-Z0-9_]+\}", "");
            if (withoutQuotedStrings.Contains("...") ||
                Regex.IsMatch(trimmed, @"<[a-zA-Z_][a-zA-Z0-9_\-\s]*>") ||
                Regex.IsMatch(withoutQuotedStrings, @"\b(?:ON|OFF|TRUE|FALSE)\|(?:ON|OFF|TRUE|FALSE)\b", RegexOptions.IgnoreCase) ||
                withoutQuotedStrings.Contains('|') ||
                withoutQuotedStrings.Contains("[,", StringComparison.Ordinal) ||
                trimmed.Contains("-- Wrong", StringComparison.OrdinalIgnoreCase) ||
                withoutInterpolations.Contains('{') ||
                withoutInterpolations.Contains('}'))
                return true;

            // Skip HTML block structures (e.g. text visuals with raw HTML snippets)
            if (trimmed.StartsWith("<") && trimmed.Contains(">") && (trimmed.Contains("</") || trimmed.Contains("/>")))
                return true;

            var fragmentLeaders = new[]
            {
                "WHERE ", "AND ", "OR ", "JOIN ", "ON ", "GROUP ", "ORDER ", "HAVING ",
                "UNION", "EXCEPT", "INTERSECT", "FROM ", "WHEN ", "ELSE ", "THEN ",
                "ON_CLICK ", "ON_CHANGE ", "BEGIN CATCH"
            };

            if (fragmentLeaders.Any(kw => trimmed.StartsWith(kw, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Function signatures, column annotations, and property fragments are useful syntax
            // documentation but are not standalone scripts. A copy-pasteable block begins with a
            // statement keyword (comments are ignored when finding that first line).
            var firstCodeLine = trimmed.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));
            if (firstCodeLine == null)
                return true;

            var statementLeaders = new[]
            {
                "ALTER ", "ANALYZE ", "ASSERT ", "BACKUP ", "BEGIN ", "BREAK", "BULK ",
                "CHECKPOINT", "CLEAR ", "COMMIT", "COMPRESS ", "CONFIG ", "CONTINUE", "COPY ",
                "CREATE ", "DECLARE ", "DECOMPRESS ", "DELETE ", "DROP ", "ENCRYPT ", "EXEC ",
                "EXECUTE ", "EXPECT ", "EXPORT ", "FOREACH ", "GENERATE ", "GRANT ", "HELP ",
                "IF ", "IMPORT ", "INSERT ", "KILL ", "LINEAGE ", "LINT ", "MERGE ", "MOVE ",
                "PARALLEL ", "PIVOT ", "PRINT ", "PUBLISH ", "RAISEERROR ", "RECEIVE ",
                "REFRESH ", "RENAME ", "REQUIRE ", "RESTORE ", "RETURN", "REVOKE ", "ROLLBACK",
                "RUN ", "SELECT ", "SEND ", "SET ", "SHOW ", "TAG ", "TEST ", "THROW ",
                "TRANSFORM ", "TRUNCATE ", "UPDATE ", "USE ", "VALIDATE ", "WAIT ", "WAITFOR ",
                "WHILE "
            };
            return !statementLeaders.Any(leader => firstCodeLine.StartsWith(leader, StringComparison.OrdinalIgnoreCase));
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
                           !parts.Contains("artifacts") &&
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

        /// <summary>Files git tracks, repo-relative with forward slashes.</summary>
        private static HashSet<string> TrackedFiles()
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Source and tooling must not embed one developer's checkout location. v0.16.0 was blocked
        /// mid-release by 101 doc links that resolved only against the author's own filesystem, and
        /// the same pattern reappeared in the UI sandbox's fixture data. CI runners have neither
        /// path, so anything that depends on one is broken everywhere except the machine it was
        /// written on.
        /// </summary>
        [Fact]
        public void SourceAndTooling_DoNotEmbedDeveloperSpecificPaths()
        {
            // Documentation deliberately shows absolute paths, because the security sandbox requires
            // them — those are illustrative examples, not dependencies on a real location.
            var searchRoots = new[] { "src", "scripts", "tools", "tests" };
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".ps1", ".psm1", ".sh", ".js", ".mjs", ".ts", ".json", ".yml", ".yaml"
            };

            // A drive-qualified or POSIX home path pointing at a named user's directory.
            //
            // The POSIX forms require that nothing path-like precedes them, because a home
            // directory is the *root* of an absolute path. Without that, the pattern also matched
            // REST routes such as "/api/admin/users/42/reset-password", which are not paths on
            // anyone's machine — a false positive that would push authors to contort real routes.
            var pattern = new Regex(
                @"([A-Za-z]:[\\/]Users[\\/][A-Za-z0-9._-]+" +
                @"|(?<![A-Za-z0-9._-])/home/[A-Za-z0-9._-]+/" +
                @"|(?<![A-Za-z0-9._-])/Users/[A-Za-z0-9._-]+/)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Paths that look developer-specific but are not a dependency on anyone's machine:
            // well-known OS locations, and deliberately synthetic identities in test fixtures.
            var allowed = new Regex(
                @"[\\/]Users[\\/](Public|Default|All Users)\b|[\\/](alice|bob|carol|dave|testuser|user)\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // Only committed files matter: downloaded caches such as .vscode-test contain vendored
            // third-party binaries full of other people's home directories, and they are gitignored
            // precisely because they are not ours. Asking git avoids maintaining a blacklist that
            // silently rots as new tool caches appear.
            var tracked = TrackedFiles();

            var offenders = new List<string>();
            foreach (var root in searchRoots)
            {
                var dir = RepoFile(root);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (!extensions.Contains(Path.GetExtension(file))) continue;
                    var relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
                    if (!tracked.Contains(relative)) continue;

                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var match = pattern.Match(lines[i]);
                        if (!match.Success) continue;
                        if (allowed.IsMatch(match.Value)) continue;
                        offenders.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1}: {match.Value}");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                $"Developer-specific absolute paths found in source/tooling ({offenders.Count}):\n" +
                string.Join("\n", offenders) +
                "\n\nUse a relative path, a runtime-resolved path, or a generic placeholder. CI runners " +
                "do not have these directories.");
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

        // ── Every registered standard function has a reference page ──────────────

        /// <summary>
        /// Functions that are registered but deliberately have no page of their own under
        /// <c>docs/reference/functions/</c>. This list is a ratchet: it may shrink, never grow.
        /// A new function must ship with its reference page, because <c>docs/reference</c> is the
        /// embedded runtime help surfaced by <c>HELP</c>.
        /// </summary>
        private static readonly HashSet<string> FunctionsWithoutOwnReferencePage =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Documented together as a statement form in
                // docs/reference/statements/dml/unnest.md rather than as function pages.
                "FLATTEN",
                "UNNEST",

                // Row-level-security predicates, documented as a group in
                // docs/administration/platform/row-level-security.md. They are only meaningful in
                // an RLS policy context, so a standalone function page would be misleading on its
                // own. Tracked for per-function HELP coverage as follow-up work.
                "HAS_GROUP",
                "HAS_ROLE",
                "USER_GROUPS",
                "USER_ROLES",
            };

        [Fact]
        public void EveryRegisteredFunction_HasAReferencePage()
        {
            var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.StandardFunctions.Register(registry);

            var functionsDir = RepoFile("docs/reference/functions");
            Assert.True(Directory.Exists(functionsDir), $"Missing functions doc dir: {functionsDir}");

            var documented = Directory
                .GetFiles(functionsDir, "*.md", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n) &&
                            !n!.Equals("README", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var undocumented = registry.GetRegisteredNames()
                .Where(n => !documented.Contains(n))
                .Where(n => !FunctionsWithoutOwnReferencePage.Contains(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.True(undocumented.Count == 0,
                $"Registered functions with no page under docs/reference/functions/ ({undocumented.Count}):\n" +
                string.Join("\n", undocumented) +
                "\n\ndocs/reference is the embedded runtime help. Add <name>.md in the matching " +
                "category folder and a row in that folder's README.md index.");
        }

        [Fact]
        public void FunctionsWithoutOwnReferencePage_AreAllStillRegistered()
        {
            // Keeps the exemption list honest: an entry for a function that no longer exists
            // (renamed or removed) would silently mask a genuinely undocumented function later.
            var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.StandardFunctions.Register(registry);
            var registered = registry.GetRegisteredNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stale = FunctionsWithoutOwnReferencePage
                .Where(n => !registered.Contains(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.True(stale.Count == 0,
                $"Exempted from function-doc coverage but no longer registered ({stale.Count}):\n" +
                string.Join("\n", stale) +
                "\n\nRemove these from FunctionsWithoutOwnReferencePage.");
        }

        [Fact]
        public void VsCodeExtensionDocsAndVersion_AreCorrectAndSynchronized()
        {
            // 1. Resolve current version from Directory.Build.props
            var buildPropsPath = RepoFile("Directory.Build.props");
            Assert.True(File.Exists(buildPropsPath), $"Missing Directory.Build.props");
            var buildProps = File.ReadAllText(buildPropsPath);
            var versionMatch = Regex.Match(buildProps, @"<VersionPrefix>(\d+\.\d+\.\d+)</VersionPrefix>");
            Assert.True(versionMatch.Success, "Could not parse version from Directory.Build.props");
            var currentVersion = versionMatch.Groups[1].Value;

            // 2. Verify version in VS Code package.json
            var packageJsonPath = RepoFile("src/etl-sql-vscode/package.json");
            Assert.True(File.Exists(packageJsonPath), $"Missing VS Code package.json");
            var packageJson = File.ReadAllText(packageJsonPath);
            Assert.Contains($"\"version\": \"{currentVersion}\"", packageJson);

            // 3. Verify version badge in VS Code README.md
            var readmePath = RepoFile("src/etl-sql-vscode/README.md");
            Assert.True(File.Exists(readmePath), $"Missing VS Code README.md");
            var readme = File.ReadAllText(readmePath);
            Assert.Contains($"ETL--SQL-v{currentVersion}-5C6BC0", readme);

            // 4. Verify WelcomeView.ts uses correct restructured documentation links
            var welcomeViewPath = RepoFile("src/etl-sql-vscode/src/WelcomeView.ts");
            Assert.True(File.Exists(welcomeViewPath), $"Missing WelcomeView.ts");
            var welcomeView = File.ReadAllText(welcomeViewPath);

            // Every document the welcome page opens has to exist. Pinning the literal paths here
            // instead only proves the strings have not changed — which is how the getting-started
            // link went on pointing at a file the docs restructure had already moved, opening a
            // dead link from the extension's front page while this test stayed green.
            var openedPaths = System.Text.RegularExpressions.Regex
                .Matches(welcomeView, @"resolveProductUri\(this\._extensionUri,\s*'([^']+)'\)")
                .Select(m => m.Groups[1].Value)
                .ToList();

            Assert.NotEmpty(openedPaths);
            foreach (var opened in openedPaths)
            {
                // resolveProductUri resolves against the extension root, not against the source file.
                var resolved = Path.GetFullPath(Path.Combine(
                    RepoFile("src/etl-sql-vscode"), opened.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(resolved) || Directory.Exists(resolved),
                    $"WelcomeView.ts opens '{opened}', which does not exist at {resolved}.");
            }

            // The cookbook is one of them, and is reached through its collection index.
            Assert.Contains("docs/cookbooks/etl/README.md", welcomeView);

            // Should not reference legacy path casing or retired files
            Assert.DoesNotContain("Docs/User_Manual.md", welcomeView);
            Assert.DoesNotContain("Docs/Cookbook.md", welcomeView);

            // 5. Verify README.md link paths casing and names
            Assert.DoesNotContain("Docs/", readme);
            Assert.DoesNotContain("User_Manual.md", readme);
            Assert.DoesNotContain("Spec_Driven_Development.md", readme);
        }
    }
}
