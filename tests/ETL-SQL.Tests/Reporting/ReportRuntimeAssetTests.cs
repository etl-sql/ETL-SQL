using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class ReportRuntimeAssetTests
    {
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void SharedRuntime_IncludesInteractionLayoutAndMaximizeHooks()
        {
            var root = FindRepoRoot();
            var js = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.js"));
            var css = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.css"));

            Assert.Contains("function renderLayout", js);
            Assert.Contains("gridTemplateAreas", js);
            Assert.Contains("renderContainer(container", js);
            Assert.Contains("renderButton(container", js);
            Assert.Contains("renderNavBar", js);
            Assert.Contains("applyPageCrossFilter", js);
            Assert.Contains("ON_SELECT", js);
            Assert.Contains("visualCanReflectSelections", js);
            Assert.Contains("toggleVisualMaximize", js);
            Assert.Contains("closeMaximizedVisual", js);
            Assert.Contains("keydown", js);
            Assert.Contains("collapsedRows", js);
            Assert.Contains("collapsedCols", js);
            Assert.Contains("matrix-toggle", js);
            Assert.Contains("case 'MATRIX':      renderMatrix(card, visual)", js);
            Assert.Contains("visual.highlightRows.map(rowKey)", js);
            Assert.Contains("function applyNativeHighlight", js);
            Assert.Contains("const hasCrossHighlights", js);
            Assert.Contains("cross-highlight-selection", js);

            Assert.Contains(".visual-card.visual-maximized", css);
            Assert.Contains("body.visual-maximize-active", css);
            Assert.Contains(".container-scroll", css);
            Assert.Contains(".nav-tab", css);
            Assert.Contains(".nav-btn", css);
            Assert.Contains(".matrix-toggle", css);
            Assert.Contains(".matrix-group-row", css);
            Assert.Contains(".cross-dimmed", css);
            Assert.Contains(".cross-highlight-universe", css);
            Assert.Contains(".cross-highlight-selection", css);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void SharedRuntime_GivesEveryParameterControlAnAccessibleName()
        {
            var root = FindRepoRoot();
            var js = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.js"));

            Assert.Contains("function setParameterAccessibleName", js);
            Assert.Contains("setParameterAccessibleName(select, visual, paramName)", js);
            Assert.Contains("setParameterAccessibleName(textInput, visual, param, 'date')", js);
            Assert.Contains("setParameterAccessibleName(textInput, visual, param, 'relative date')", js);
            Assert.Contains("setParameterAccessibleName(input, visual, param)", js);
            Assert.Contains("setParameterAccessibleName(cb, visual, param, val)", js);
            Assert.Contains("function attachDetailSurface", js);
            Assert.Contains("postParameters({ '@hover_value': value }, true)", js);
            Assert.DoesNotContain("if (tooltip) card.title = tooltip", js);
            Assert.Contains("card.style.borderRadius = borderRadius", js);
            Assert.Contains("card.style.boxShadow", js);
            Assert.Contains("input.setAttribute('aria-label', label.textContent)", js);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void SharedRuntime_UsesCanonicalJsonForMultiSelectParameters()
        {
            var root = FindRepoRoot();
            var js = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.js"));

            Assert.Contains("function parseMultiParameter", js);
            Assert.Contains("JSON.stringify(Array.from(selected))", js);
            Assert.Contains("accept legacy comma-separated values", js);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void SharedRuntime_ConsumesResolvedMicroChartsWithoutBrowserGeometryCompiler()
        {
            var root = FindRepoRoot();
            var js = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.js"));
            var css = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.css"));

            Assert.Contains("function findMicroChart", js);
            Assert.Contains("micro.accessibleLabel", js);
            Assert.Contains("sparkline.accessibleLabel", js);
            Assert.Contains("micro.role === 'html.inline'", js);
            Assert.Contains("data-etl-microchart-id", js);
            Assert.DoesNotContain("function buildSparklineSvg", js);
            Assert.Contains(".card-sparkline svg", css);
            Assert.Contains("td[role=\"img\"] svg", css);
            Assert.Contains(".html-inline-microchart svg", css);
        }

        /// <summary>
        /// The chart dispatch must not call a function the runtime does not define. The
        /// behavioural proof that the degraded state actually renders lives in the browser lane
        /// (<c>DetailSurfaceBehaviourTests</c>); this keeps the dead call from coming back in a
        /// lane that runs everywhere.
        /// </summary>
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void SharedRuntime_HasNoUndefinedChartFallback()
        {
            var root = FindRepoRoot();
            var js = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.js"));
            var css = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "report-runtime.css"));

            Assert.DoesNotContain("renderChart(", js);
            Assert.Contains("function renderMissingChartPayload", js);
            Assert.Contains(": renderMissingChartPayload(card, visual); break;", js);
            Assert.Contains("el.setAttribute('role', 'status')", js);
            Assert.Contains(".missing-chart-payload", css);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void ReportBuilder_ExposesNativeCustomCompositionRecipes()
        {
            var root = FindRepoRoot();
            var js = File.ReadAllText(Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "designer.js"));

            Assert.Contains("Composition recipe", js);
            Assert.Contains("Box plot + mean tick", js);
            Assert.Contains("Candlestick + volume", js);
            Assert.Contains("Q1 = q1 (TYPE = QUANTITATIVE)", js);
            Assert.Contains("OPEN = open (TYPE = QUANTITATIVE, SCALE = price)", js);
            Assert.Contains("Layered map", js);
            Assert.Contains("TYPE = GEOGRAPHIC", js);
            Assert.Contains("ROUTE = route (TYPE = NOMINAL)", js);
        }

        private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFilePath) ?? "" })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "src", "ETL-SQL.ReportRuntime"))
                        && Directory.Exists(Path.Combine(current.FullName, "tests", "ETL-SQL.Tests")))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }
    }
}
