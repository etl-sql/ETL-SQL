using Xunit;
using System.Text.Json;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Markdown/JSON result export formatting used by the REPL export command.</summary>
    public class ReplExportTests
    {
        private static DataTable SampleTable()
        {
            var dt = new DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = new Row(dt.Schema);
            row["id"] = 1m;
            row["name"] = "a|b";   // pipe must be escaped in markdown
            dt.Rows.Add(row);
            return dt;
        }

        [Fact]
        public void FormatMarkdown_WritesHeaderSeparatorAndEscapesPipes()
        {
            var md = ReplUi.FormatMarkdown(SampleTable());

            Assert.Contains("| id | name |", md);
            Assert.Contains("| --- | --- |", md);
            Assert.Contains(@"a\|b", md);   // pipe escaped so the cell stays in one column
        }

        [Fact]
        public void FormatJson_ProducesArrayOfRowObjects()
        {
            var json = ReplUi.FormatJson(SampleTable());

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(1, doc.RootElement.GetArrayLength());

            var first = doc.RootElement[0];
            Assert.Equal("a|b", first.GetProperty("name").GetString());
            Assert.Equal(1, first.GetProperty("id").GetDecimal());
        }
    }
}
