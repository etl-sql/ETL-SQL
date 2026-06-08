using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using ETL_SQL.TUI.UI;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Pure geometry tests for the shared <see cref="LayoutCalculator"/> / <see cref="EditorLayout"/>,
    /// plus an end-to-end check that a sidebar click lands on the row that was drawn. The
    /// sidebar is a bordered panel with a header, so its first item is one row below the
    /// band top — the bug this fixes was the mouse using the band top directly.
    /// </summary>
    public class LayoutTests
    {
        static LayoutTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        [Theory]
        [InlineData(40, 10)]  // the supported minimum
        [InlineData(20, 6)]   // below minimum (the renderer shows a "too small" prompt, but the
        [InlineData(1, 1)]    // geometry must still never go negative / out of bounds)
        public void Compute_TinyTerminal_ProducesNoNegativeDimensions(int w, int h)
        {
            foreach (var (sidebar, maximized, compare) in new[] { (false, false, false), (true, false, false), (false, true, false), (false, false, true) })
            {
                var l = LayoutCalculator.Compute(w, h, bufferLineCount: 1, sidebarVisible: sidebar, sidebarWidth: 24, isBottomMaximized: maximized, compareMode: compare);
                Assert.True(l.EditorAreaHeight >= 0, $"EditorAreaHeight negative at {w}x{h}");
                Assert.True(l.LowerAreaHeight >= 0, $"LowerAreaHeight negative at {w}x{h}");
                Assert.True(l.EditorAreaTop >= 0);
                Assert.True(l.GutterWidth >= 0);
                Assert.True(l.SidebarMaxVisibleItems >= 0);
            }
        }

        [Fact]
        public void Compute_BasicBandsAndGutter()
        {
            var l = LayoutCalculator.Compute(80, 24, bufferLineCount: 3,
                sidebarVisible: false, sidebarWidth: 24, isBottomMaximized: false, compareMode: false);

            Assert.Equal(2, l.EditorAreaTop);
            Assert.Equal(2, l.StatusHeight);
            Assert.Equal(12, l.LowerAreaHeight);   // 14 > (20-8) -> max(5, 12)
            Assert.Equal(8, l.EditorAreaHeight);   // 24 - 12 - 2 - 2
            Assert.Equal(10, l.LowerY);
            Assert.Equal(3, l.GutterWidth);        // "3".Length + 2
            Assert.Equal(0, l.SidebarW);           // hidden
        }

        [Fact]
        public void Compute_Maximized_GrowsLowerPane()
        {
            var l = LayoutCalculator.Compute(80, 24, 1, false, 24, isBottomMaximized: true, compareMode: false);
            Assert.Equal(15, l.LowerAreaHeight);   // max(5, available(20) - 5)
            Assert.Equal(5, l.EditorAreaHeight);   // 24 - 15 - 2 - 2
        }

        [Fact]
        public void Sidebar_ContentStartsOneBelowBandTop()
        {
            var l = LayoutCalculator.Compute(80, 24, 1, sidebarVisible: true, sidebarWidth: 24, false, false);

            Assert.Equal(24, l.SidebarW);
            Assert.Equal(l.EditorAreaTop + 1, l.SidebarContentTop);   // header/border offset
            Assert.Equal(l.EditorAreaHeight - 2, l.SidebarMaxVisibleItems);
        }

        [Fact]
        public void SidebarItemIndexAt_AccountsForHeaderAndScroll()
        {
            var l = LayoutCalculator.Compute(80, 24, 1, true, 24, false, false); // contentTop=3, max=6

            Assert.Equal(-1, l.SidebarItemIndexAt(l.EditorAreaTop, 0));          // border/header row
            Assert.Equal(0, l.SidebarItemIndexAt(l.SidebarContentTop, 0));       // first item
            Assert.Equal(5, l.SidebarItemIndexAt(l.SidebarContentTop + 5, 0));   // last visible
            Assert.Equal(-1, l.SidebarItemIndexAt(l.SidebarContentTop + 6, 0));  // past visible window
            Assert.Equal(10, l.SidebarItemIndexAt(l.SidebarContentTop, 10));     // scrolled
        }

        [Fact]
        public void Bands_ClassifyRows()
        {
            var l = LayoutCalculator.Compute(80, 24, 1, true, 24, false, false); // band [2,10), lower [10,22)

            Assert.False(l.InEditorBand(1));
            Assert.True(l.InEditorBand(2));
            Assert.True(l.InEditorBand(9));
            Assert.False(l.InEditorBand(10));

            Assert.True(l.InLowerPane(10));
            Assert.True(l.InLowerPane(21));
            Assert.False(l.InLowerPane(22));

            Assert.True(l.InSidebar(0, 3));
            Assert.False(l.InSidebar(24, 3));   // x must be < width
            Assert.False(l.InSidebar(0, 1));    // above band
        }

        [Fact]
        public void EditorPosition_MapsClickToLineAndColumn()
        {
            var l = LayoutCalculator.Compute(80, 24, 3, sidebarVisible: false, sidebarWidth: 24, false, false);
            Assert.Equal(1, l.EditorLineAt(3, scrollLine: 0));        // y=3 -> line 1
            Assert.Equal(3, l.EditorColumnAt(6, scrollCol: 0));      // x=6 - gutter(3)
        }

        [Fact]
        public async Task SidebarClick_SelectsTheRowThatWasDrawn()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F9, false, false, false)); // open + focus sidebar
            editor._renderer.Render(editor, 80, 24); // establishes last width/height for hit-testing

            var items = editor._renderer._sidebarPanel.GetFlatVisibleItems();
            Assert.True(items.Count >= 2, "expected the expanded root plus at least one child");

            // First item is drawn at SidebarContentTop (= editorAreaTop + 1 = 3); row 4 is the second item.
            await editor._renderer.HandleMouseClick(0, 2, 4, false, editor);
            Assert.Equal(1, editor._renderer.SidebarSelectedIndex);
        }
    }
}
