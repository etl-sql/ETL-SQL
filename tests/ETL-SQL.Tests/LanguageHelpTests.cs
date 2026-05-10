using ETL_SQL.Analysis.Documentation;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common;
using Xunit;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ETL_SQL.Tests
{
    public class LanguageHelpTests
    {
        [Fact]
        public void Verify_CoreKeywords_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            
            // Testing a subset of core keywords to verify the resource loading works
            string[] coreKeywords = { "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "IF", "WHILE", "FOR", "FOREACH" };
            
            foreach (var check in verifier.VerifyRequiredTopics(coreKeywords))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void Verify_MajorConnectors_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            string[] connectors = { "MSSQL", "POSTGRES", "FLATFILE", "API" };
            
            foreach (var check in verifier.VerifyRequiredSubTopics("CONNECTION", connectors))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void Verify_SystemVariables_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            string[] sysVars = { "@@ROWCOUNT", "@@ERROR", "@@VERSION" };
            
            foreach (var check in verifier.VerifyRequiredSubTopics("VARIABLES", sysVars))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void Verify_ReportComponents_HaveHelpDocumentation()
        {
            var registry = new LanguageHelpRegistry();
            var verifier = new HelpDocumentationVerifier(registry);
            string[] components = { "DATASET", "PAGE", "STYLE", "VISUAL" };
            
            foreach (var check in verifier.VerifyRequiredSubTopics("REPORT", components))
            {
                Assert.True(check.Found, check.Message);
            }
        }

        [Fact]
        public void ReportHelpSqlExamples_ParseSuccessfully()
        {
            var failures = new List<string>();

            foreach (var example in GetParseableReportHelpExamples())
            {
                try
                {
                    new Parser(new Lexer(example.Script).Tokenize(), example.Script).Parse();
                }
                catch (Exception ex)
                {
                    failures.Add($"{example.DisplayName}: {ex.Message}");
                }
            }

            Assert.Empty(failures);
        }

        [Fact]
        public async Task ReportHelpSqlExamples_LintWithoutThrowing()
        {
            var linter = new Linter();
            linter.AddRule(new ReportKeywordLintRule());
            linter.AddRule(new VisualSourceRequiredRule());
            linter.AddRule(new VisualMappingCompletenessRule());
            linter.AddRule(new PageVisualReferencedRule());

            foreach (var example in GetParseableReportHelpExamples())
            {
                var script = new Parser(new Lexer(example.Script).Tokenize(), example.Script).Parse();
                await linter.AnalyzeAsync(script, new DefaultLintContext());
            }
        }

        private static IEnumerable<(string DisplayName, string Script)> GetParseableReportHelpExamples()
        {
            var reportHelpDir = Path.Combine(FindRepoRoot(), "src", "ETL-SQL.Core", "Resources", "Help", "Report");
            foreach (var file in Directory.GetFiles(reportHelpDir, "*.md").OrderBy(path => path))
            {
                var markdown = File.ReadAllText(file);
                var blockIndex = 0;

                foreach (Match match in Regex.Matches(markdown, @"```sql\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    blockIndex++;
                    var block = match.Groups[1].Value.Trim();
                    if (TryBuildParseableScript(block, out var script))
                    {
                        yield return ($"{Path.GetFileName(file)} SQL block {blockIndex}", script);
                    }
                }
            }
        }

        private static bool TryBuildParseableScript(string block, out string script)
        {
            script = block;
            if (string.IsNullOrWhiteSpace(block) || block.Contains('<') || block.Contains("..."))
            {
                return false;
            }

            var firstLine = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.TrimStart();
            if (firstLine == null)
            {
                return false;
            }

            if (firstLine.StartsWith("OPTIONS", StringComparison.OrdinalIgnoreCase)
                || firstLine.StartsWith("ACTIONS", StringComparison.OrdinalIgnoreCase))
            {
                script = $"""
                    CREATE VISUAL HelpSmoke AS BAR (
                      SOURCE = #help_data,
                      MAPPINGS (X = label, Y = value),
                      {block}
                    );
                    """;
                return true;
            }

            if (firstLine.StartsWith("ON_CLICK", StringComparison.OrdinalIgnoreCase)
                || firstLine.StartsWith("ON_CHANGE", StringComparison.OrdinalIgnoreCase))
            {
                script = $"""
                    CREATE VISUAL HelpSmoke AS SLICER (
                      SOURCE = #help_data,
                      MAPPINGS (VALUE = value),
                      ACTIONS ({block})
                    );
                    """;
                return true;
            }

            return firstLine.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                || firstLine.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)
                || firstLine.StartsWith("SET", StringComparison.OrdinalIgnoreCase)
                || firstLine.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src", "ETL-SQL.Core"))
                    && Directory.Exists(Path.Combine(current.FullName, "tests", "ETL-SQL.Tests")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }
    }
}
