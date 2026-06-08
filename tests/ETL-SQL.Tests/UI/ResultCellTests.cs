using Xunit;
using System.Linq;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Result-cell navigation helpers: row filtering, scroll-follow, and copy commands.</summary>
    public class ResultCellTests
    {
        private static DataTable Table()
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "id", "name" });
            foreach (var (id, name) in new[] { (1m, "alpha"), (2m, "Beta"), (3m, "gamma") })
            {
                var row = new Row(dt.Schema);
                row["id"] = id; row["name"] = name;
                dt.Rows.Add(row);
            }
            return dt;
        }

        [Fact]
        public void FilterRows_Null_ReturnsAll()
        {
            Assert.Equal(3, ResultsPanel.FilterRows(Table(), null).Count);
        }

        [Fact]
        public void FilterRows_IsCaseInsensitiveSubstring()
        {
            // "bet" matches "Beta" only.
            var rows = ResultsPanel.FilterRows(Table(), "bet");
            Assert.Single(rows);
            Assert.Equal("Beta", rows[0]["name"]);
        }

        [Fact]
        public void FilterRows_NoMatch_ReturnsEmpty()
        {
            Assert.Empty(ResultsPanel.FilterRows(Table(), "zzz"));
        }

        [Theory]
        [InlineData(0, 0, 5, 10, 0)]   // active already visible
        [InlineData(0, 7, 5, 10, 3)]   // active below window -> scroll to show it at the bottom
        [InlineData(5, 2, 5, 10, 2)]   // active above window -> scroll up to it
        [InlineData(0, 0, 5, 0, 0)]    // no rows
        [InlineData(100, 3, 5, 10, 3)] // out-of-range scroll is clamped first
        public void FollowScroll_KeepsActiveVisible(int scroll, int active, int visible, int total, int expected)
        {
            Assert.Equal(expected, ResultsPanel.FollowScroll(scroll, active, visible, total));
        }

        [Fact]
        public void Palette_IncludesRowAndSetCopy()
        {
            Assert.Contains(CommandPalette.Commands, c => c.Title == "Copy result row (TSV)");
            Assert.Contains(CommandPalette.Commands, c => c.Title == "Copy result set (TSV)");
        }
    }
}
