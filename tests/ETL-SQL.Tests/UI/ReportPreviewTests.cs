using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.TUI.UI;
using Xunit;

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

        [Fact]
        public async Task HeaderControlButtons_SelectAndActivateControls()
        {
            var editor = new ConsoleEditor("test.rptsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.ReportVisible = true;
            var first = Checkbox("FirstFilter", "@first");
            var second = Checkbox("SecondFilter", "@second");
            var page = new PageManifest { Name = "Main" };
            page.SlotMap["A"] = first.Name;
            page.SlotMap["B"] = second.Name;
            var manifest = new ReportManifest();
            manifest.Pages.Add(page);
            manifest.Visuals.Add(first);
            manifest.Visuals.Add(second);
            manifest.Parameters["@first"] = "false";
            manifest.Parameters["@second"] = "false";
            editor._renderer.CurrentReportManifest = manifest;
            var controls = ReportControlInteraction.GetControls(manifest, 0);

            await editor._renderer.HandleReportNavigationTarget(
                ReportPreviewNavigationTarget.NextControl, controls, editor);
            Assert.Equal(1, editor._renderer.ActiveReportControlIndex);

            await editor._renderer.HandleReportNavigationTarget(
                ReportPreviewNavigationTarget.ActivateControl, controls, editor);
            Assert.Equal("true", manifest.Parameters["@second"]);
            Assert.Equal("true", editor._evaluator.GetVariable("@second"));

            await editor._renderer.HandleReportNavigationTarget(
                ReportPreviewNavigationTarget.PreviousControl, controls, editor);
            Assert.Equal(0, editor._renderer.ActiveReportControlIndex);
        }

        [Fact]
        public async Task TabInReportPreview_CyclesControlsWithoutIndentingScript()
        {
            var editor = new ConsoleEditor("test.rptsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.ReportVisible = true;
            editor._buffer.Load(["SELECT 1;"]);
            var originalScript = editor._buffer.GetText();
            var first = Checkbox("FirstFilter", "@first");
            var second = Checkbox("SecondFilter", "@second");
            var page = new PageManifest { Name = "Main" };
            page.SlotMap["A"] = first.Name;
            page.SlotMap["B"] = second.Name;
            editor._renderer.CurrentReportManifest = new ReportManifest
            {
                Pages = { page },
                Visuals = { first, second }
            };

            await editor.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));

            Assert.Equal(1, editor._renderer.ActiveReportControlIndex);
            Assert.Equal(originalScript, editor._buffer.GetText());
        }

        [Fact]
        public void HeaderHitTest_TracksControlAndPageButtons()
        {
            const int width = 80;
            var controlStart = ReportPreviewNavigation.ControlStartX(width, hasMultiplePages: true);
            var pageStart = ReportPreviewNavigation.PageStartX(width);
            var runStart = ReportPreviewNavigation.RunStartX(width, hasControls: true, hasMultiplePages: true);

            Assert.Equal(ReportPreviewNavigationTarget.RunReport,
                ReportPreviewNavigation.HitTest(runStart, width, hasControls: true, hasMultiplePages: true));
            Assert.Equal(ReportPreviewNavigationTarget.PreviousControl,
                ReportPreviewNavigation.HitTest(controlStart, width, hasControls: true, hasMultiplePages: true));
            Assert.Equal(ReportPreviewNavigationTarget.ActivateControl,
                ReportPreviewNavigation.HitTest(controlStart + ReportPreviewNavigation.ButtonWidth, width, true, true));
            Assert.Equal(ReportPreviewNavigationTarget.NextControl,
                ReportPreviewNavigation.HitTest(controlStart + 2 * ReportPreviewNavigation.ButtonWidth, width, true, true));
            Assert.Equal(ReportPreviewNavigationTarget.PreviousPage,
                ReportPreviewNavigation.HitTest(pageStart, width, true, true));
            Assert.Equal(ReportPreviewNavigationTarget.NextPage,
                ReportPreviewNavigation.HitTest(pageStart + ReportPreviewNavigation.ButtonWidth, width, true, true));
        }

        [Fact]
        public async Task EnterOnCheckbox_UpdatesBoundParameterWithoutLeavingPreview()
        {
            var editor = new ConsoleEditor("test.rptsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.ReportVisible = true;
            var picker = new VisualManifest { Name = "IncludeReturns", VisualType = "CHECKBOX", DefaultValue = "false" };
            picker.Actions.Add(new VisualActionManifest
            {
                Type = "SET_PARAMETER",
                Trigger = "ON_CHANGE",
                ParameterName = "@include_returns",
                ValueSource = "CONTROL_VALUE"
            });
            var page = new PageManifest { Name = "Main" };
            page.SlotMap["A"] = picker.Name;
            var manifest = new ReportManifest();
            manifest.Pages.Add(page);
            manifest.Visuals.Add(picker);
            manifest.Parameters["@include_returns"] = "false";
            editor._renderer.CurrentReportManifest = manifest;

            await editor.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            Assert.Equal("true", manifest.Parameters["@include_returns"]);
            Assert.Equal("true", editor._evaluator.GetVariable("@include_returns"));
            Assert.True(editor._renderer.ReportVisible);
        }

        [Fact]
        public void ControlValues_AreTypeCheckedAndMultiselectUsesBrowserCompatibleJson()
        {
            var date = new VisualManifest { VisualType = "DATEPICKER" };
            Assert.False(ReportControlInteraction.TryNormalizeValue(date, "08/20/2026", out _, out var dateError));
            Assert.Contains("YYYY-MM-DD", dateError);

            var slider = new VisualManifest { VisualType = "SLIDER", Min = 10, Max = 20 };
            Assert.False(ReportControlInteraction.TryNormalizeValue(slider, "25", out _, out var rangeError));
            Assert.Contains("below", rangeError);

            var multi = new VisualManifest { VisualType = "MULTISELECT" };
            multi.Columns.Add("Region");
            multi.Rows.Add(["East"]);
            multi.Rows.Add(["West"]);
            Assert.True(ReportControlInteraction.TryNormalizeValue(multi, "East, West", out var normalized, out _));
            Assert.Equal("[\"East\",\"West\"]", normalized);
            Assert.False(ReportControlInteraction.TryNormalizeValue(multi, "North", out _, out var choiceError));
            Assert.Contains("not an available choice", choiceError);
        }

        private static VisualManifest Checkbox(string name, string parameter)
        {
            var checkbox = new VisualManifest { Name = name, VisualType = "CHECKBOX", DefaultValue = "false" };
            checkbox.Actions.Add(new VisualActionManifest
            {
                Type = "SET_PARAMETER",
                Trigger = "ON_CHANGE",
                ParameterName = parameter,
                ValueSource = "CONTROL_VALUE"
            });
            return checkbox;
        }
    }
}
