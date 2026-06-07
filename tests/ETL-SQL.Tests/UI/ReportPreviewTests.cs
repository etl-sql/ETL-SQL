using Xunit;
using System.Collections.Generic;
using ETL_SQL.TUI.UI;
using ETL_SQL.Core;
using ETL_SQL.Reporting;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Report preview page navigation (Left/Right keys and the clickable arrows).</summary>
    public class ReportPreviewTests
    {
        static ReportPreviewTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        private static EditorRenderer RendererWithPages(int pages)
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            var manifest = new ReportManifest();
            for (int i = 0; i < pages; i++) manifest.Pages.Add(new PageManifest { Name = $"Page{i}" });
            editor._renderer.CurrentReportManifest = manifest;
            return editor._renderer;
        }

        [Fact]
        public void NextPage_AdvancesAndClamps()
        {
            var r = RendererWithPages(3);
            r.ActiveReportPageIndex = 0;

            r.ReportNextPage();
            Assert.Equal(1, r.ActiveReportPageIndex);
            r.ReportNextPage();
            r.ReportNextPage(); // clamp at last
            Assert.Equal(2, r.ActiveReportPageIndex);
        }

        [Fact]
        public void PrevPage_RetreatsAndClamps()
        {
            var r = RendererWithPages(3);
            r.ActiveReportPageIndex = 2;

            r.ReportPrevPage();
            Assert.Equal(1, r.ActiveReportPageIndex);
            r.ReportPrevPage();
            r.ReportPrevPage(); // clamp at first
            Assert.Equal(0, r.ActiveReportPageIndex);
        }

        [Fact]
        public void PageChange_ResetsScroll()
        {
            var r = RendererWithPages(3);
            r.ActiveReportPageIndex = 0;
            r.ReportScrollRow = 25;
            r.ReportNextPage();
            Assert.Equal(0, r.ReportScrollRow);
        }

        [Fact]
        public void NoManifest_IsSafe()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.ReportNextPage(); // no manifest -> no throw, no change
            Assert.Equal(0, editor._renderer.ActiveReportPageIndex);
        }
    }
}
