using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    public class DocumentationSyntaxTests
    {
        private static string FindRepoRoot()
        {
            var current = AppDomain.CurrentDomain.BaseDirectory;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")))
                {
                    return current;
                }
                current = Path.GetDirectoryName(current);
            }
            throw new DirectoryNotFoundException("Could not locate repository root containing ETL-SQL.slnx.");
        }

        private static List<Token> Tokenize(string sql)
        {
            var lexer = new Lexer(sql);
            return lexer.Tokenize();
        }

        [Fact]
        public void ValidateDocumentationSnippets()
        {
            var repoRoot = FindRepoRoot();
            var tree = DefaultGrammar.Build();

            var mdFiles = new List<string>();

            // Gather all markdown files in docs/
            var docsDir = Path.Combine(repoRoot, "docs");
            if (Directory.Exists(docsDir))
            {
                var files = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                    .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("architecture"));
                mdFiles.AddRange(files);
            }

            Assert.NotEmpty(mdFiles);

            int checkedSnippets = 0;
            int skippedSnippets = 0;

            foreach (var file in mdFiles)
            {
                var content = File.ReadAllText(file);

                foreach (var block in ExtractSqlBlocks(content))
                {
                    var sqlBlock = block.Trim();

                    List<Token> allTokens;
                    try
                    {
                        allTokens = Tokenize(sqlBlock);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"SQL block in '{Path.GetRelativePath(repoRoot, file)}' could not be tokenized: {ex.Message}");
                        throw;
                    }

                    var statements = SplitTokensIntoStatements(allTokens);

                    foreach (var tokens in statements)
                    {
                        var firstToken = tokens.FirstOrDefault(t => t.Type != TokenType.EOF);

                        if (firstToken == null) continue;

                        // Check if the starting keyword is registered in our grammar tree.
                        // For now, we only validate snippets starting with keywords we support.
                        // For CREATE/ALTER statements, only validate if they contain the CONNECTION keyword.
                        bool shouldValidate = false;
                        if (tree.GetStartNode(firstToken.Value) != null)
                        {
                            // Portal-native commands inside EXECUTE portal blocks have their own
                            // grammar and must not be certified as top-level ETL-SQL statements.
                            bool isEmbeddedPortalCommand = firstToken.Value.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
                                && tokens.Skip(1).FirstOrDefault()?.Value.Equals("SMTP", StringComparison.OrdinalIgnoreCase) == true;

                            // Skip template placeholders like <Provider>, [optional], or |
                            bool hasPlaceholders = tokens.Any(t =>
                                t.Type == TokenType.LESS_THAN ||
                                t.Type == TokenType.GREATER_THAN ||
                                t.Value == "[" ||
                                t.Value == "]" ||
                                t.Value == "|" ||
                                t.Value.Contains("<") ||
                                t.Value.Contains(">")
                            );

                            bool isReplaceFunction = firstToken.Value.Equals("REPLACE", StringComparison.OrdinalIgnoreCase)
                                && tokens.Skip(1).FirstOrDefault()?.Value == "(";

                            if (!hasPlaceholders && !isEmbeddedPortalCommand && !isReplaceFunction)
                            {
                                shouldValidate = true;
                            }
                        }

                        if (shouldValidate)
                        {
                            // Documentation often splits compound blocks into fragments for transition
                            // coverage. Production-parser acceptance is covered by complete grammar tests.
                            bool success = tree.ValidateSequence(tokens, out var errorMessage, requireComplete: false);

                            // Include file name and snippet details in the failure message for debugging ease
                            Assert.True(success,
                                $"Syntax error in documentation file '{Path.GetRelativePath(repoRoot, file)}':\n" +
                                $"Snippet:\n{string.Join(" ", tokens.Select(t => t.Value))}\n\n" +
                                $"Error: {errorMessage}");

                            checkedSnippets++;
                        }
                        else
                        {
                            skippedSnippets++;
                        }
                    }
                }
            }

            // The floor is a coverage ratchet, not a smoke check. The previous column-zero
            // extractor reached 3457 validated statements; honouring indented fences took it to
            // 3495. Anything that drops back below this line has removed validation, which is
            // exactly the failure mode a "make the gate green" change produces.
            Assert.True(checkedSnippets >= 3400,
                $"Expected broad documentation grammar coverage, but checked only {checkedSnippets} snippets ({skippedSnippets} skipped).");
        }

        /// <summary>
        /// An indented fence must end at its own closing fence, not run on to the next
        /// unindented one. This is asserted directly because the symptom in the aggregate gate
        /// was a tokenizer error in unrelated prose, which reads as a documentation defect.
        /// </summary>
        [Fact]
        public void ExtractSqlBlocks_BoundsAnIndentedFenceAtItsOwnClosingFence()
        {
            const string markdown = """
                * Route failures:

                  ```sql
                  SELECT 1
                  INTO #x;
                  ```

                Prose mentioning ETL-SQL's apostrophe.

                ```sql
                SELECT 2;
                ```
                """;

            var blocks = DocumentationSyntaxTests.ExtractSqlBlocks(markdown);

            Assert.Equal(2, blocks.Count);
            // Dedented to column zero, and stopping before the prose that follows it.
            Assert.Equal("SELECT 1\nINTO #x;", blocks[0]);
            Assert.Equal("SELECT 2;", blocks[1]);
            Assert.DoesNotContain("apostrophe", blocks[0]);
            Assert.DoesNotContain("```", blocks[0]);
        }

        /// <summary>
        /// The four indented fences in the troubleshooting guide were the reported failure. They
        /// must now be extracted as four separate blocks that carry no prose.
        /// </summary>
        [Fact]
        public void ExtractSqlBlocks_ValidatesTheIndentedFencesInTheTroubleshootingGuide()
        {
            var path = Path.Combine(FindRepoRoot(), "docs", "guides", "patterns", "troubleshooting-syntax-and-dialect.md");
            var content = File.ReadAllText(path);

            var indentedFences = content
                .Replace("\r\n", "\n")
                .Split('\n')
                .Count(l => l.StartsWith(" ", StringComparison.Ordinal) && l.TrimStart().StartsWith("```sql", StringComparison.Ordinal));
            Assert.Equal(4, indentedFences);

            var blocks = ExtractSqlBlocks(content);
            Assert.All(blocks, b => Assert.DoesNotContain("```", b));
            Assert.All(blocks, b => Assert.DoesNotContain("ETL-SQL's", b));
        }

        // A fenced block opens with an optional indent, three or more backticks, and the bare
        // `sql` info string. CommonMark lets a fence sit inside a list item, in which case both
        // fences and the body carry the list's content indentation.
        private static readonly Regex OpenFenceRegex =
            new(@"^(?<indent>[ \t]*)(?<fence>`{3,})sql[ \t]*$", RegexOptions.Compiled);

        // The closing fence is at least as long as the opening one and carries no info string.
        private static readonly Regex CloseFenceRegex =
            new(@"^(?<indent>[ \t]*)(?<fence>`{3,})[ \t]*$", RegexOptions.Compiled);

        /// <summary>
        /// Returns the body of every fenced <c>sql</c> block in a markdown document.
        /// </summary>
        /// <remarks>
        /// This is line-oriented rather than a single multi-line regex on purpose. The previous
        /// regex required the closing fence to start at column zero, so an indented block — the
        /// normal shape inside a list item — never found its own end: it ran on to the next
        /// unindented fence and swallowed the intervening prose, which then failed to tokenize.
        /// Honouring the indent both fixes that and brings the indented blocks, which were
        /// silently mis-scoped, into real validation. The opening fence's indentation is stripped
        /// from each body line, as CommonMark specifies.
        /// </remarks>
        internal static List<string> ExtractSqlBlocks(string content)
        {
            var blocks = new List<string>();
            var lines = content.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var open = OpenFenceRegex.Match(lines[i]);
                if (!open.Success) continue;

                var indent = open.Groups["indent"].Value.Length;
                var fenceLength = open.Groups["fence"].Value.Length;

                var body = new List<string>();
                int j = i + 1;
                for (; j < lines.Length; j++)
                {
                    var close = CloseFenceRegex.Match(lines[j]);
                    if (close.Success &&
                        close.Groups["fence"].Value.Length >= fenceLength &&
                        close.Groups["indent"].Value.Length <= indent + 3)
                    {
                        break;
                    }

                    body.Add(StripIndent(lines[j], indent));
                }

                blocks.Add(string.Join("\n", body));
                i = j; // Resume after the closing fence (or at end of file if it is missing).
            }

            return blocks;
        }

        /// <summary>Removes up to <paramref name="width"/> leading spaces or tabs.</summary>
        private static string StripIndent(string line, int width)
        {
            int removed = 0;
            while (removed < width && removed < line.Length && (line[removed] == ' ' || line[removed] == '\t'))
            {
                removed++;
            }
            return line.Substring(removed);
        }

        private static readonly HashSet<string> StartKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "CREATE", "ALTER", "COMPRESS", "ENCRYPT", "DECRYPT",
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
            "IF", "WHILE", "FOR", "FOREACH", "BEGIN", "END",
            "RUN", "WAITFOR", "WAIT", "SEND", "EXPORT", "TAG",
            "SET", "PRINT", "DECLARE", "THROW", "DROP",
            "REBUILD", "PUBLISH", "REFRESH", "DISCONNECT",
            "REVOKE", "RESTART", "SHUTDOWN", "SHOW", "BULK", "RETURN",
            "MOVE", "COPY", "EXECUTE", "EXEC", "PARALLEL", "USE", "GRANT", "COMMIT", "ROLLBACK",
            "ASSERT", "BREAK", "CONTINUE", "RAISERROR", "RAISEERROR", "RECEIVE", "KILL"
        };

        private static readonly HashSet<string> ContinuationKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "THEN", "ON", "INTO", "USING", "AND", "OR", "AS", "WITH", "AT", "TO", "IN", "ELSE", "DELAY", "TIME", "UNTIL", "WHEN"
        };

        private static bool IsContinuationToken(Token token)
        {
            if (token.Type == TokenType.COMMA ||
                token.Type == TokenType.EQUALS ||
                token.Type == TokenType.PLUS ||
                token.Type == TokenType.MINUS ||
                token.Type == TokenType.STAR ||
                token.Type == TokenType.SLASH ||
                token.Type == TokenType.DOT ||
                token.Type == TokenType.LPAREN ||
                token.Type == TokenType.LESS_THAN ||
                token.Type == TokenType.GREATER_THAN)
            {
                return true;
            }

            return ContinuationKeywords.Contains(token.Value);
        }

        private static bool ShouldPreventSplit(string statementStart, string nextKeyword)
        {
            if (statementStart.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                nextKeyword.Equals("SET", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (statementStart.Equals("ALTER", StringComparison.OrdinalIgnoreCase) &&
                nextKeyword.Equals("SET", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (statementStart.Equals("INSERT", StringComparison.OrdinalIgnoreCase) &&
                nextKeyword.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (statementStart.Equals("MERGE", StringComparison.OrdinalIgnoreCase) &&
                (nextKeyword.Equals("INSERT", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("DELETE", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            if ((statementStart.Equals("EXECUTE", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("EXEC", StringComparison.OrdinalIgnoreCase)) &&
                nextKeyword.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if ((statementStart.Equals("CREATE", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("EXPORT", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("PUBLISH", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("ALTER", StringComparison.OrdinalIgnoreCase)) &&
                (nextKeyword.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("ENCRYPT", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("DECRYPT", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("COMPRESS", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("REFRESH", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            // `FOR REPORT ...` and `SEND TO ...` are clauses of CREATE SUBSCRIPTION, not
            // statements of their own — an unterminated CREATE keeps owning them.
            if (statementStart.Equals("CREATE", StringComparison.OrdinalIgnoreCase) &&
                (nextKeyword.Equals("SEND", StringComparison.OrdinalIgnoreCase) ||
                 nextKeyword.Equals("FOR", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            if (statementStart.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) &&
                nextKeyword.Equals("TRANSACTION", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if ((statementStart.Equals("IF", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("WHILE", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("FOR", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("FOREACH", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("ELSE", StringComparison.OrdinalIgnoreCase)) &&
                nextKeyword.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if ((statementStart.Equals("SELECT", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("INSERT", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
                 statementStart.Equals("MERGE", StringComparison.OrdinalIgnoreCase)) &&
                nextKeyword.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static List<List<Token>> SplitTokensIntoStatements(List<Token> tokens)
        {
            var results = new List<List<Token>>();
            var current = new List<Token>();
            int parenthesisDepth = 0;

            var real = tokens.Where(t => t.Type != TokenType.EOF).ToList();

            for (int i = 0; i < real.Count; i++)
            {
                var token = real[i];
                var next = i + 1 < real.Count ? real[i + 1] : null;

                // A checkpoint label (`Cleanup:` on its own line) is neither a statement nor a
                // continuation of the one above it. Treat it as its own fragment so the block
                // below it is validated on its own merits.
                bool opensLabel = token.Type == TokenType.IDENTIFIER
                    && next is not null
                    && next.Type == TokenType.COLON
                    && next.Line == token.Line;

                if (token.Type == TokenType.LPAREN)
                {
                    parenthesisDepth++;
                }
                else if (token.Type == TokenType.RPAREN)
                {
                    parenthesisDepth--;
                }

                if (current.Any() && parenthesisDepth == 0)
                {
                    var lastToken = current.Last();
                    var firstToken = current.FirstOrDefault(t => t.Type != TokenType.EOF);
                    if (token.Line > lastToken.Line &&
                        (opensLabel ||
                         (StartKeywords.Contains(token.Value) &&
                          !IsContinuationToken(lastToken) &&
                          (firstToken == null || !ShouldPreventSplit(firstToken.Value, token.Value)))))
                    {
                        results.Add(current);
                        current = new List<Token>();
                    }
                }

                current.Add(token);

                // Close the label fragment immediately after its colon.
                if (token.Type == TokenType.COLON && current.Count == 2 &&
                    current[0].Type == TokenType.IDENTIFIER && current[0].Line == token.Line)
                {
                    results.Add(current);
                    current = new List<Token>();
                    continue;
                }

                if (token.Type == TokenType.SEMICOLON)
                {
                    results.Add(current);
                    current = new List<Token>();
                    parenthesisDepth = 0;
                }
            }

            if (current.Any())
            {
                results.Add(current);
            }

            return results;
        }
    }
}
