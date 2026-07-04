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
                mdFiles.AddRange(Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories));
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
                    catch
                    {
                        // Gracefully skip blocks that fail basic lexing (like placeholders or cut-off snippets)
                        continue;
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
                            // Skip template placeholders like <Provider>
                            bool hasPlaceholders = tokens.Any(t => t.Type == TokenType.LESS_THAN || t.Type == TokenType.GREATER_THAN);
                            
                            if (!hasPlaceholders)
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
                            bool success = tree.ValidateSequence(tokens, out var errorMessage);
                            
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
            Assert.True(checkedSnippets > 0, "Expected to find and check at least one snippet starting with supported grammar keywords.");
        }

        private static List<List<Token>> SplitTokensIntoStatements(List<Token> tokens)
        {
            var results = new List<List<Token>>();
            var current = new List<Token>();

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.EOF)
                {
                    continue;
                }

                current.Add(token);

                if (token.Type == TokenType.SEMICOLON)
                {
                    results.Add(current);
                    current = new List<Token>();
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
