using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Regressions for the v0.19.0 report-formatting compatibility breaks recorded in
    /// <c>BREAKING_CHANGES.md</c>. Each asserts the new behaviour and states what the old one was, so a
    /// future change that quietly restores the old behaviour fails here.
    /// </summary>
    [Trait("CompatBreak", "0.19")]
    public class ReportFormattingCompatBreakTests
    {
        /// <summary>
        /// Was: any identifier parsed and the handler discarded anything it did not implement, so the
        /// statement ran, reported nothing, and changed nothing.
        /// </summary>
        [Fact]
        public void UnknownSetReportKey_IsRejectedRatherThanSilentlyIgnored()
        {
            const string script = "SET REPORT TIMEZONE = 'UTC';";

            var error = Assert.Throws<SyntaxException>(
                () => new Parser(new Lexer(script).Tokenize(), script).ParseStatement());

            Assert.Contains("not a valid SET REPORT key", error.Message);
        }

        /// <summary>
        /// Was: a temporal literal carrying no offset was parsed against the server's local zone, so the
        /// same report rendered a different instant on a machine in another zone.
        /// </summary>
        [Fact]
        public async Task OffsetlessTemporalLiteral_IsAnchoredToUtcNotTheServersZone()
        {
            var display = await TemporalDisplay(@"
SELECT '2026-03-01' AS ObservedTime, 10.0 AS Amount INTO #series
UNION ALL SELECT '2026-03-02', 20.0;

CREATE VISUAL Chart AS LINE (SOURCE = #series, MAPPINGS (X = ObservedTime, Y = Amount));
");

            Assert.Equal("2026-03-01", display.First());
        }

        /// <summary>
        /// Was: a NULL measure rendered as an empty string in the semantic fallback and every surface
        /// built from it. It now renders the resolved NULL label, which defaults to "-".
        /// </summary>
        [Fact]
        public async Task NullMeasure_RendersTheDefaultNullLabelRatherThanEmptyText()
        {
            var manifest = await Build(@"
SELECT 'A' AS Category, 10.0 AS Amount INTO #bars
UNION ALL SELECT 'B', NULL;

CREATE VISUAL Chart AS BAR (SOURCE = #bars, MAPPINGS (X = Category, Y = Amount));
");

            var plan = manifest.Visuals.Single(visual => visual.Name == "Chart").PlotPlan!;
            Assert.Contains(plan.Fallback.Items, item => item.Value == "-");
        }

        private static async Task<ReportManifest> Build(string script)
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.RedirectOutput = true;
            evaluator.DisplayExecuteTree = false;
            await evaluator.Evaluate(new Parser(new Lexer(script).Tokenize(), script).Parse());
            return await new ManifestBuilder(evaluator).BuildAsync("report.rptsql");
        }

        private static async Task<IEnumerable<string?>> TemporalDisplay(string script)
        {
            var manifest = await Build(script);
            return manifest.Visuals.Single(visual => visual.Name == "Chart").ChartData!.Columns
                .Single(column => column.Name == "ObservedTime").DisplayValues;
        }
    }
}
