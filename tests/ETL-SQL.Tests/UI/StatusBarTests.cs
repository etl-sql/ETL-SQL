using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// The clickable help-bar shortcuts: x→button hit-testing (aligned with the rendered
    /// label positions) and an end-to-end click that runs the shortcut's action.
    /// </summary>
    public class StatusBarTests
    {
        static StatusBarTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        [Fact]
        public void Segments_StartAfterLeadSpace_WithTwoColumnGaps()
        {
            var segs = StatusBar.Segments().ToList();

            // "F1:Help" starts at column 1 (after the single lead space), width 7.
            Assert.Equal("F1:Help", segs[0].Button.Label);
            Assert.Equal(1, segs[0].StartX);
            Assert.Equal(7, segs[0].Width);

            // Next button starts after the label + a two-column gap: 1 + 7 + 2 = 10.
            Assert.Equal(10, segs[1].StartX);
        }

        [Fact]
        public void HitTest_FindsButtonsAndGaps()
        {
            Assert.Equal("F1:Help", StatusBar.HitTest(1)?.Label);
            Assert.Equal("F1:Help", StatusBar.HitTest(7)?.Label);
            Assert.Null(StatusBar.HitTest(8));   // gap between buttons
            Assert.Equal("F5:Run", StatusBar.HitTest(10)?.Label);
            Assert.Null(StatusBar.HitTest(0));   // lead space
        }

        [Fact]
        public void HitTest_AlignsWithRenderedPlainText()
        {
            string plain = StatusBar.PlainText();
            foreach (var seg in StatusBar.Segments())
            {
                string drawn = plain.Substring(seg.StartX, seg.Width);
                Assert.Equal(seg.Button.Label, drawn);
            }
        }

        [Fact]
        public async Task ClickingExplorerButton_TogglesSidebar()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.Render(editor, 80, 24); // helpRow = 22

            var seg = StatusBar.Segments().First(s => s.Button.Label == "F9:Explorer");

            Assert.False(editor._renderer.SidebarVisible);
            await editor._renderer.HandleMouseClick(0, seg.StartX, 22, false, editor);
            Assert.True(editor._renderer.SidebarVisible);

            await editor._renderer.HandleMouseClick(0, seg.StartX, 22, false, editor);
            Assert.False(editor._renderer.SidebarVisible);
        }

        [Fact]
        public async Task ClickingHelpBarGap_DoesNothing()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.Render(editor, 80, 24);

            await editor._renderer.HandleMouseClick(0, 78, 22, false, editor); // padding area
            Assert.False(editor._renderer.SidebarVisible);
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);
        }
    }
}
