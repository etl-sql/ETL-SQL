using Xunit;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Geometry for the top tab bar. Each cell is " label " + "x" + " " (label width + 4),
    /// followed by a one-column separator; the close "x" sits at CloseX. These widths must
    /// match the renderer so the close button and "+" are clickable where drawn.
    /// </summary>
    public class TabBarLayoutTests
    {
        [Fact]
        public void Tabs_WidthsCloseColumnsAndPlus()
        {
            var labels = new List<string> { "a", "bbb" };
            var segs = TabBarLayout.Tabs(labels).ToList();

            // "a": width 5, start 0, close at 0 + 1(len) + 2 = 3
            Assert.Equal(0, segs[0].StartX);
            Assert.Equal(5, segs[0].Width);
            Assert.Equal(3, segs[0].CloseX);

            // separator after tab0 -> "bbb" starts at 0 + 5 + 1 = 6, width 7, close at 6 + 3 + 2 = 11
            Assert.Equal(6, segs[1].StartX);
            Assert.Equal(7, segs[1].Width);
            Assert.Equal(11, segs[1].CloseX);

            // "+" after the trailing separator: 6 + 7 + 1 = 14
            Assert.Equal(14, TabBarLayout.PlusStartX(labels));
        }

        [Fact]
        public void CloseColumn_IsWithinItsTab_NotTheNextOrPlus()
        {
            // Regression: the old hit-test used label+3 (one short), so close/+ drifted right.
            var labels = new List<string> { "test.etlsql", "untitled.etlsql", "untitled.etlsql" };
            var segs = TabBarLayout.Tabs(labels).ToList();
            int plusX = TabBarLayout.PlusStartX(labels);

            foreach (var s in segs)
            {
                Assert.InRange(s.CloseX, s.StartX, s.StartX + s.Width - 1); // close 'x' lives inside its tab
                Assert.True(s.CloseX < plusX);                              // and never on the '+'
            }
        }

        [Fact]
        public void Label_UsesFileNameAndDirtyMarker()
        {
            Assert.Equal("script.etlsql", TabBarLayout.Label("/a/b/script.etlsql", false));
            Assert.Equal("script.etlsql*", TabBarLayout.Label("/a/b/script.etlsql", true));
            Assert.Equal("Untitled.etlsql", TabBarLayout.Label("", false));
        }
    }
}
