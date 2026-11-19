using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Docs
{
    /// <summary>
    /// Runs the doc samples a new user is told to paste and run, not just parse them. Parsing alone
    /// let the Quickstart ship reading a column MOCKDB does not have and let both map recipes ship
    /// with visuals that failed to build.
    ///
    /// Covered: Quickstart blocks that use MOCKDB, every report recipe block (inline data) except
    /// ones that need the reader's own file or connection, and the first block of each ETL recipe
    /// whose curated description says "Runs as-is". Each block is linted like the CLI does (lint
    /// errors block a run) and evaluated; report blocks must also build with no failed visual.
    /// </summary>
    public class RunnableDocSampleTests
    {
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private const string RunsAsIsMarker = "Runs as-is";

        public static IEnumerable<object[]> RunnableBlocks()
        {
            var quickstart = Path.Combine(RepoRoot, "docs", "guides", "onboarding", "QUICKSTART.md");
            foreach (var (id, code) in CookbookVerificationTests.ExtractSqlBlocks(File.ReadAllText(quickstart), "QUICKSTART.md"))
            {
                if (code.Contains("MOCKDB()", StringComparison.OrdinalIgnoreCase))
                    yield return new object[] { id, IsReport(code), code };
            }

            foreach (var path in RecipeFiles("report"))
            {
                foreach (var (id, code) in CookbookVerificationTests.ExtractSqlBlocks(File.ReadAllText(path), Path.GetFileName(path)))
                {
                    // Needs a GeoJSON file or a connection the reader supplies.
                    if (code.Contains("MAP_FILE", StringComparison.OrdinalIgnoreCase) ||
                        code.Contains("your_conn", StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return new object[] { id, true, code };
                }
            }

            foreach (var path in RecipeFiles("etl"))
            {
                var markdown = File.ReadAllText(path);
                if (!markdown.Contains(RunsAsIsMarker, StringComparison.Ordinal)) continue;
                var (id, code) = CookbookVerificationTests.ExtractSqlBlocks(markdown, Path.GetFileName(path)).First();
                yield return new object[] { id, false, code };
            }
        }

        [Theory]
        [MemberData(nameof(RunnableBlocks))]
        public async Task DocSample_RunsAsWritten(string id, bool isReport, string code)
        {
            var script = new Parser(new Lexer(code).Tokenize(), code).Parse();
            var parseErrors = script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(parseErrors.Count == 0, $"{id} does not parse: {string.Join("; ", parseErrors.Select(e => e.Message))}");

            var linter = new Linter();
            foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                         .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
            {
                if (Activator.CreateInstance(type) is ILintRule rule) linter.AddRule(rule);
            }
            var lintErrors = (await linter.AnalyzeAsync(script, new DefaultLintContext { DocumentUri = id }))
                .Where(r => r.Severity == LintSeverity.Error).ToList();
            Assert.True(lintErrors.Count == 0, $"{id} fails lint: {string.Join("; ", lintErrors.Select(e => $"line {e.LineNumber}: {e.Message}"))}");

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.RedirectOutput = true;
            evaluator.DisplayExecuteTree = false;
            await evaluator.Evaluate(script); // an execution error throws and fails the case

            if (!isReport) return;
            var manifest = await new ManifestBuilder(evaluator).BuildAsync(id.Split(':')[0] + ".rptsql");
            var failed = manifest.Visuals.Where(v => v.Error != null).Select(v => $"{v.Name}: {v.Error}").ToList();
            Assert.True(manifest.Error == null && failed.Count == 0,
                $"{id} report build failed: {manifest.Error} {string.Join("; ", failed)}");
        }

        private static IEnumerable<string> RecipeFiles(string collection) =>
            Directory.GetFiles(Path.Combine(RepoRoot, "docs", "cookbooks", collection), "*.md")
                .Where(p => !string.Equals(Path.GetFileName(p), "README.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);

        private static bool IsReport(string code) =>
            code.Contains("CREATE VISUAL", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("CREATE PAGE", StringComparison.OrdinalIgnoreCase);
    }
}
