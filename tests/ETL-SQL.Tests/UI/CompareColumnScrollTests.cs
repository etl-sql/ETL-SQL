using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Horizontal-scroll offset clamping for compare-mode result panes.</summary>
    public class CompareColumnScrollTests
    {
        [Theory]
        [InlineData(0, 0)]    // no columns
        [InlineData(1, 0)]    // single column — can't scroll
        [InlineData(5, 4)]    // last column reachable
        [InlineData(25, 24)]
        public void MaxColumnOffset_LeavesOneColumnVisible(int columnCount, int expected)
        {
            Assert.Equal(expected, ResultsPanel.MaxColumnOffset(columnCount));
        }

        [Fact]
        public void ClampColumnOffset_RejectsNegative()
        {
            Assert.Equal(0, ResultsPanel.ClampColumnOffset(-3, 12));
        }

        [Fact]
        public void ClampColumnOffset_CapsAtLastColumn()
        {
            // 12 columns → max offset 11; an over-scroll is pinned there (one column still visible).
            Assert.Equal(11, ResultsPanel.ClampColumnOffset(50, 12));
        }

        [Fact]
        public void ClampColumnOffset_PassesThroughInRange()
        {
            Assert.Equal(7, ResultsPanel.ClampColumnOffset(7, 20));
        }
    }
}
