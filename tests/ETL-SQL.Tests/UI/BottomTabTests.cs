using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Covers the clickable bottom-pane tab strip: the x→tab hit-test, the layout's
    /// strip/content split, and an end-to-end click that switches the active view while
    /// clicks below the strip still focus the panels.
    /// </summary>
    public class BottomTabTests
    {
        static BottomTabTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        [Theory]
        // Pipeline cell [0,10), sep 10, Results [11,20), sep 20, Performance [21,34)
        [InlineData(0, BottomTab.Pipeline)]
        [InlineData(9, BottomTab.Pipeline)]
        [InlineData(11, BottomTab.Results)]
        [InlineData(19, BottomTab.Results)]
        [InlineData(21, BottomTab.Performance)]
        [InlineData(33, BottomTab.Performance)]
        public void HitTest_MapsColumnToTab(int x, BottomTab expected)
        {
            Assert.Equal(expected, BottomTabStrip.HitTest(x));
        }

        [Theory]
        [InlineData(10)]  // separator after Pipeline
        [InlineData(20)]  // separator after Results
        [InlineData(34)]  // past the last tab
        public void HitTest_ReturnsNullForGaps(int x)
        {
            Assert.Null(BottomTabStrip.HitTest(x));
        }

        [Fact]
        public void Layout_SplitsStripFromContent()
        {
            var l = LayoutCalculator.Compute(80, 24, 1, false, 24, false, false); // lowerY = 10

            Assert.True(l.OnBottomTabStrip(l.LowerY));
            Assert.False(l.InLowerContent(l.LowerY));        // strip row is not content
            Assert.True(l.InLowerContent(l.LowerY + 1));     // first content row
            Assert.Equal(l.LowerY + 1, l.LowerContentTop);
            Assert.Equal(l.LowerAreaHeight - 1, l.LowerContentHeight);
        }

        [Fact]
        public async Task ClickingStrip_SwitchesView_ContentClickFocuses()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.Render(editor, 80, 24); // lowerY = 10

            // Click "Results" tab.
            await editor._renderer.HandleMouseClick(0, 11, 10, false, editor);
            Assert.True(editor._renderer.ResultsVisible);
            Assert.False(editor._renderer.PerformanceVisible);

            // Click "Performance" tab.
            await editor._renderer.HandleMouseClick(0, 21, 10, false, editor);
            Assert.True(editor._renderer.PerformanceVisible);
            Assert.False(editor._renderer.ResultsVisible);

            // Click "Pipeline" tab.
            await editor._renderer.HandleMouseClick(0, 0, 10, false, editor);
            Assert.False(editor._renderer.ResultsVisible);
            Assert.False(editor._renderer.PerformanceVisible);

            // A click on the content row below the strip focuses the panel, not the strip.
            await editor._renderer.HandleMouseClick(0, 1, 11, false, editor);
            Assert.Equal(EditorFocus.ExecutionTree, editor._renderer.Focus);
        }
    }
}
