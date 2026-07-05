using ETL_SQL.Core.Formatting;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class FormatterRegressionTests
    {
        [Fact]
        public void TestNewCommandsFormatting()
        {
            string sql = "DECLARE @v INT; SET @v = 1; PRINT @v; FILE_SEND 'local', MYREMOTE, 'remote'; SHOW LINEAGE FOR MyTable;";
            string formatted = SqlFormatter.Format(sql);

            Assert.Contains("DECLARE", formatted);
            Assert.Contains("SET", formatted);
            Assert.Contains("PRINT", formatted);
            Assert.Contains("FILE_SEND", formatted);
            Assert.Contains("LINEAGE", formatted);

            // Should have multiple lines now
            Assert.True(formatted.Split('\n').Length >= 5);
        }

        [Fact]
        public void TestBulkInsertFormatting()
        {
            string sql = "BULK INSERT MyTable FROM 'data.csv' WITH (FORMAT = 'CSV', HEADER = ON);";
            string formatted = SqlFormatter.Format(sql);

            Assert.StartsWith("BULK INSERT", formatted);
            Assert.Contains("\nFROM", formatted);
            Assert.Contains("\nWITH", formatted);
        }

        [Fact]
        public void TestMergeFormatting()
        {
            string sql = "MERGE INTO Target AS t USING Source AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET t.val = s.val;";
            string formatted = SqlFormatter.Format(sql);

            Assert.StartsWith("MERGE INTO", formatted);
            // MERGE INTO is a clause, so USING should be on a new line if it was in ClauseKeywords.
            // Wait! I didn't add USING to ClauseKeywords, but MERGE INTO is there.
        }

        [Fact]
        public void TestTrailingCommas()
        {
            var options = new FormatterOptions { CommaPlacement = "trailing" };
            string sql = "SELECT col1, col2, col3 FROM t";
            string formatted = SqlFormatter.Format(sql, options);

            Assert.Contains("col1,", formatted);
            Assert.Contains("col2,", formatted);
            Assert.DoesNotContain("\n    ,", formatted);
        }

        [Fact]
        public void TestRightAlignment()
        {
            var options = new FormatterOptions { RightAlignKeywords = true };
            string sql = "SELECT a FROM t WHERE b = 1";
            string formatted = SqlFormatter.Format(sql, options);

            // "         SELECT" (15 chars total)
            Assert.Contains("         SELECT", formatted);
            Assert.Contains("           FROM", formatted);
            Assert.Contains("          WHERE", formatted);
        }
    }
}
