using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class ReportSyntaxReadinessTests
    {
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void PublicReportDocsHelpAndSamples_DoNotUseReplacedSyntax()
        {
            var root = FindRepoRoot();
            var files = EnumerateReleaseFacingFiles(root).ToList();
            var failures = new List<string>();

            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                CheckDoesNotContain(file, text, "AS LAYOUT", failures);
                CheckDoesNotContain(file, text, "CREATE LAYOUT", failures);
                CheckDoesNotContain(file, text, "ON_CHANGE SET_PARAMETER", failures);
                CheckDoesNotContain(file, text, "SET_PARAMETER @", failures);
                CheckDoesNotContain(file, text, "Buttons cannot currently be placed", failures);
                CheckDoesNotContain(file, text, "grid:1x1", failures);
                CheckDoesNotContain(file, text, "grid:NxN", failures);
            }

            Assert.Empty(failures);
        }

        private static IEnumerable<string> EnumerateReleaseFacingFiles(string root)
        {
            yield return Path.Combine(root, "AGENTS.md");
            yield return Path.Combine(root, "Docs", "Report_SQL_Guide.md");
            yield return Path.Combine(root, "Docs", "Reference", "Grammar.md");
            yield return Path.Combine(root, "Docs", "Sample_Guide.md");
            yield return Path.Combine(root, "Docs", "Strategy", "ReportPortal_Strategy.md");

            foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "ETL-SQL.Core", "Resources", "Help", "Report"), "*.md"))
                yield return file;

            foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "ETL-SQL.Core", "Resources", "Help", "Visuals"), "*.md"))
                yield return file;

            yield return Path.Combine(root, "samples", "golden_workflow", "golden_workflow.rptsql");
            yield return Path.Combine(root, "samples", "08_Reporting", "kitchen_sink.rptsql");
            yield return Path.Combine(root, "samples", "10_Kitchen_Sinks", "report_kitchen_sink.rptsql");
        }

        private static void CheckDoesNotContain(string file, string text, string pattern, List<string> failures)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{Path.GetFileName(file)} contains replaced syntax marker '{pattern}'.");
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(current.FullName, "samples")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }
    }
}
