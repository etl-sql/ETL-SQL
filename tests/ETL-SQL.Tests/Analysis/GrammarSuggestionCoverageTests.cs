using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Parser;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Coverage for the states/transitions the editor relies on for suggestions — the bridge from
    /// "the grammar exists" to "the editor uses it reliably". Complements the fuzzer's generator-side
    /// coverage by walking a corpus of complete authoring statements through the same walker the
    /// suggestion service uses, and asserting every registered start node is reachable and a healthy
    /// fraction of labeled transitions are exercised.
    /// </summary>
    public class GrammarSuggestionCoverageTests
    {
        private readonly ITestOutputHelper _output;

        public GrammarSuggestionCoverageTests(ITestOutputHelper output) => _output = output;

        private static List<Token> Tokenize(string sql) => new Lexer(sql).Tokenize();

        // Representative complete statements spanning the main authoring workflows. Kept in one place
        // so both coverage assertions below walk the same corpus.
        private static readonly string[] Corpus =
        {
            "SELECT * FROM src.Users;",
            "SELECT UserID, UserName FROM src.Users WHERE UserID > 5 GROUP BY UserID ORDER BY UserID DESC;",
            "SELECT u.UserID FROM src.Users u JOIN src.Orders o ON u.UserID = o.UserID WHERE o.Total > 10;",
            "SELECT UserID, ROW_NUMBER() OVER (PARTITION BY Region ORDER BY Total DESC) FROM src.Sales QUALIFY ROW_NUMBER() OVER (ORDER BY Total) = 1;",
            "WITH t AS (SELECT * FROM src.Users) SELECT * FROM t;",
            "INSERT INTO src.Users (UserID, UserName) VALUES (1, 'Alice');",
            "UPDATE src.Users SET UserName = 'Bob' WHERE UserID = 1;",
            "DELETE FROM src.Users WHERE UserID = 1;",
            "MERGE INTO src.Users USING src.Staging ON src.Users.UserID = src.Staging.UserID WHEN MATCHED THEN UPDATE SET UserName = 'x';",
            "CREATE CONNECTION src AS MSSQL (PASSWORD = 'abc');",
            "CREATE CONNECTION f AS FLATFILE (PATH = 'C:\\data.csv', COMPRESS = ON);",
            "COMPRESS FILE 'C:\\raw.csv' TO 'C:\\raw.zip' WITH (OVERWRITE = ON);",
            "ENCRYPT FILE 'C:\\raw.csv' TO 'C:\\raw.pgp' PASSWORD 'Secret123';",
            "IF @x > 1 BEGIN SELECT 1; END;",
            "WHILE @x < 10 BEGIN SET @x = @x + 1; END;",
            "FOREACH @id IN src.Users BEGIN PRINT @id; END;",
            "DECLARE @x INT;",
            "SET @x = 5;",
            "PRINT 'hello';",
            "SHOW CONNECTIONS;",
        };

        [Fact]
        public void EveryRegisteredStartNode_IsReachableFromRoot()
        {
            var tree = DefaultGrammar.Build();

            var unreachable = new List<string>();
            foreach (var keyword in tree.StartKeywords)
            {
                var walker = new TokenWalker(tree);
                // Consuming the start keyword must move the walker off Root into that start node.
                bool consumed = walker.Consume(new Token(TokenType.IDENTIFIER, keyword, 1, 1, 1, keyword.Length + 1));
                var start = tree.GetStartNode(keyword);
                if (!consumed || start == null || !walker.ActiveStates.Contains(start))
                {
                    unreachable.Add(keyword);
                }
            }

            Assert.True(unreachable.Count == 0,
                $"These registered start nodes are not reachable from Root by their own keyword: {string.Join(", ", unreachable)}");
        }

        [Fact]
        public void SuggestionCorpus_ExercisesMostLabeledTransitions()
        {
            var tree = DefaultGrammar.Build();

            var allStates = tree.GetAllStates();
            var labeledTransitions = allStates
                .SelectMany(s => s.Transitions)
                .Where(t => t.Label != null)
                .ToHashSet();

            var visitedStates = new HashSet<StateNode>();
            var visitedTransitions = new HashSet<StateTransition>();

            // The curated corpus plus the documented SQL examples — the walker resets to Root at each
            // semicolon, so whole multi-statement snippets can be fed as-is.
            var corpus = Corpus.Concat(LoadDocumentationSnippets());

            foreach (var sql in corpus)
            {
                var walker = new TokenWalker(tree);
                foreach (var token in Tokenize(sql))
                {
                    if (token.Type == TokenType.EOF) break;

                    // Record every transition that could fire for this token from the current active
                    // set (a transition the editor would have offered/taken here).
                    foreach (var state in walker.ActiveStates)
                    {
                        foreach (var transition in state.Transitions)
                        {
                            if (transition.Label != null && transition.Matches(token, walker))
                            {
                                visitedTransitions.Add(transition);
                            }
                        }
                    }

                    walker.Consume(token);
                    visitedStates.UnionWith(walker.ActiveStates);
                }
            }

            double statePct = 100.0 * visitedStates.Count / allStates.Count;
            double transitionPct = 100.0 * visitedTransitions.Count / labeledTransitions.Count;
            _output.WriteLine($"Suggestion corpus coverage: states {visitedStates.Count}/{allStates.Count} ({statePct:F1}%), " +
                              $"labeled transitions {visitedTransitions.Count}/{labeledTransitions.Count} ({transitionPct:F1}%)");

            // Guardrail floors (well below current ~50%/~40%): a regression that makes a large region
            // of the grammar unreachable from real statements/docs drops these below the floor. Raise
            // the floors as the corpus grows.
            Assert.True(statePct >= 40.0, $"Suggestion state coverage dropped to {statePct:F1}% (floor 40%).");
            Assert.True(transitionPct >= 30.0, $"Suggestion transition coverage dropped to {transitionPct:F1}% (floor 30%).");
        }

        private static IEnumerable<string> LoadDocumentationSnippets()
        {
            var repoRoot = FindRepoRoot();
            var files = new List<string>();

            var docsDir = Path.Combine(repoRoot, "Docs");
            if (Directory.Exists(docsDir))
            {
                files.AddRange(Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories));
            }
            var helpDir = Path.Combine(repoRoot, "src", "ETL-SQL.Core", "Resources", "Help");
            if (Directory.Exists(helpDir))
            {
                files.AddRange(Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories));
            }

            var sqlBlock = new Regex(@"```sql\r?\n(.*?)\r?\n```", RegexOptions.Singleline);
            foreach (var file in files)
            {
                foreach (Match match in sqlBlock.Matches(File.ReadAllText(file)))
                {
                    var block = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(block))
                    {
                        yield return block;
                    }
                }
            }
        }

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
    }
}
