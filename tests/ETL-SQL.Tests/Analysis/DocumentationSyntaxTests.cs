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

            // Gather all markdown files in Docs/ and Resources/Help/
            var docsDir = Path.Combine(repoRoot, "Docs");
            if (Directory.Exists(docsDir))
            {
                var files = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                    .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("Strategy"));
                mdFiles.AddRange(files);
            }

            var helpDir = Path.Combine(repoRoot, "src", "ETL-SQL.Core", "Resources", "Help");
            if (Directory.Exists(helpDir))
            {
                mdFiles.AddRange(Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories));
            }

            Assert.NotEmpty(mdFiles);

            int checkedSnippets = 0;
            int skippedSnippets = 0;

            var sqlBlockRegex = new Regex(@"```sql\r?\n(.*?)\r?\n```", RegexOptions.Singleline);

            foreach (var file in mdFiles)
            {
                var content = File.ReadAllText(file);
                var matches = sqlBlockRegex.Matches(content);

                foreach (Match match in matches)
                {
                    var sqlBlock = match.Groups[1].Value.Trim();

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

                            if (!hasPlaceholders && !isEmbeddedPortalCommand)
                            {
                                if (firstToken.Value.Equals("CREATE", StringComparison.OrdinalIgnoreCase) ||
                                    firstToken.Value.Equals("ALTER", StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldValidate = tokens.Any(t => t.Value.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase));
                                }
                                else
                                {
                                    shouldValidate = true;
                                }
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

            // Verify we actually checked some snippets
            Assert.True(checkedSnippets >= 100,
                $"Expected broad documentation grammar coverage, but checked only {checkedSnippets} snippets ({skippedSnippets} skipped).");
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

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.EOF)
                {
                    continue;
                }

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
                        StartKeywords.Contains(token.Value) &&
                        !IsContinuationToken(lastToken) &&
                        (firstToken == null || !ShouldPreventSplit(firstToken.Value, token.Value)))
                    {
                        results.Add(current);
                        current = new List<Token>();
                    }
                }

                current.Add(token);

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
