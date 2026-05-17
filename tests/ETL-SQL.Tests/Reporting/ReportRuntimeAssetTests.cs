using System;
using System.IO;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class ReportRuntimeAssetTests
    {
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void SharedRuntime_IncludesPhase5InteractionLayoutAndMaximizeHooks()
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
            Assert.Contains("toggleVisualMaximize", js);
            Assert.Contains("closeMaximizedVisual", js);
            Assert.Contains("keydown", js);
            Assert.Contains("collapsedRows", js);
            Assert.Contains("collapsedCols", js);
            Assert.Contains("matrix-toggle", js);

            Assert.Contains(".visual-card.visual-maximized", css);
            Assert.Contains("body.visual-maximize-active", css);
            Assert.Contains(".container-scroll", css);
            Assert.Contains(".nav-tab", css);
            Assert.Contains(".nav-btn", css);
            Assert.Contains(".matrix-toggle", css);
            Assert.Contains(".matrix-group-row", css);
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src", "ETL-SQL.ReportRuntime"))
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
