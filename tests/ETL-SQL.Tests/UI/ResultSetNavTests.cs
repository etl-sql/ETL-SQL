using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Covers the clickable result-set navigator (◀ i/N ▶) and the F7 compare-mode click/
    /// scroll fix. Compare interactions were gated behind ResultsVisible, which F7 sets
    /// false, so clicking/scrolling a pane previously did nothing.
    /// </summary>
    public class ResultSetNavTests
    {
        static ResultSetNavTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        private static ConsoleEditor EditorWithResultSets(int n)
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            for (int i = 0; i < n; i++)
            {
                var t = new DataTable();
                t.SetColumns(new[] { "Col" });
                editor._evaluator.LastResultSets.Add(t);
            }
            return editor;
        }

        // width 80, count 3 -> label " 1/3 " (len 5), nav width 11, start 69:
        //   prev [69,72)  label [72,77)  next [77,80)
        [Theory]
        [InlineData(69, -1)]
        [InlineData(71, -1)]
        [InlineData(77, +1)]
        [InlineData(79, +1)]
        [InlineData(74, 0)]   // label area
        [InlineData(50, 0)]   // far left (tabs)
        public void HitTest_MapsArrowZones(int x, int expected)
        {
            Assert.Equal(expected, ResultSetNav.HitTest(x, 80, index: 0, count: 3));
        }

        [Fact]
        public void HitTest_SingleSet_NoArrows()
        {
            Assert.Equal(0, ResultSetNav.HitTest(79, 80, index: 0, count: 1));
        }

        [Fact]
        public void CycleResultSet_ClampsAndResetsScrollFilter()
        {
            var editor = EditorWithResultSets(3);
            editor._renderer.ResultScrollRow = 5;
            editor._renderer.FilterText = "x";

            editor._renderer.CycleResultSet(+1, 3);
            Assert.Equal(1, editor._renderer.ActiveResultSetIndex);
            Assert.Equal(0, editor._renderer.ResultScrollRow);
            Assert.Equal("", editor._renderer.FilterText);

            editor._renderer.CycleResultSet(+1, 3);
            editor._renderer.CycleResultSet(+1, 3); // clamp at last
            Assert.Equal(2, editor._renderer.ActiveResultSetIndex);

            editor._renderer.CycleResultSet(-1, 3);
            Assert.Equal(1, editor._renderer.ActiveResultSetIndex);
        }

        [Fact]
        public async Task ClickingArrows_SwitchesActiveResultSet()
        {
            var editor = EditorWithResultSets(3);
            editor._renderer.ResultsVisible = true;
            editor._renderer.Render(editor, 80, 24); // lowerY = 10

            await editor._renderer.HandleMouseClick(0, 78, 10, false, editor); // ▶ next
            Assert.Equal(1, editor._renderer.ActiveResultSetIndex);

            await editor._renderer.HandleMouseClick(0, 78, 10, false, editor); // ▶ next
            Assert.Equal(2, editor._renderer.ActiveResultSetIndex);

            await editor._renderer.HandleMouseClick(0, 70, 10, false, editor); // ◀ prev
            Assert.Equal(1, editor._renderer.ActiveResultSetIndex);
        }

        [Fact]
        public async Task CompareMode_ClickSelectsPane_EvenWithResultsHidden()
        {
            var editor = EditorWithResultSets(2);
            editor._renderer.CompareMode = true;
            editor._renderer.ResultsVisible = false;        // exactly what F7 leaves
            editor._renderer.IsBottomMaximized = true;      // F7 maximizes the pane
            editor._renderer.CompareScrollRows = new List<int> { 0, 0 };
            editor._renderer.CompareFilters = new List<string> { "", "" };
            editor._renderer.Render(editor, 80, 24);        // lowerY=7, content [8,22), paneHeight 7

            await editor._renderer.HandleMouseClick(0, 5, 16, false, editor); // pane 1
            Assert.Equal(1, editor._renderer.CompareFocusIndex);

            await editor._renderer.HandleMouseClick(0, 5, 9, false, editor);  // pane 0
            Assert.Equal(0, editor._renderer.CompareFocusIndex);
        }

        [Fact]
        public void CompareMode_WheelScrollsPaneUnderCursor()
        {
            var editor = EditorWithResultSets(2);
            editor._renderer.CompareMode = true;
            editor._renderer.ResultsVisible = false;
            editor._renderer.IsBottomMaximized = true;
            editor._renderer.CompareScrollRows = new List<int> { 0, 0 };
            editor._renderer.CompareFilters = new List<string> { "", "" };
            editor._renderer.Render(editor, 80, 24);

            editor._renderer.ScrollRegion(5, 16, 3);  // wheel down over pane 1
            Assert.Equal(3, editor._renderer.CompareScrollRows[1]);
            Assert.Equal(0, editor._renderer.CompareScrollRows[0]);
        }
    }
}
