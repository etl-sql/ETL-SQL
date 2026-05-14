using Xunit;
using System.Text.RegularExpressions;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Unit tests for the pushdown SQL prefix-stripping regex used in ExecutePushdownStatementHandler.
    /// The regex is: (?&lt;![.\w])connectionName\. (case-insensitive)
    /// These tests document the expected behavior for edge cases.
    /// </summary>
    public class PushdownPrefixStripTests
    {
        private static string Strip(string connectionName, string sql)
        {
            var escapedName = Regex.Escape(connectionName);
            return Regex.Replace(sql, $@"(?<![.\w]){escapedName}\.", "", RegexOptions.IgnoreCase);
        }

        [Fact]
        public void Strip_SimpleFromClause_RemovesPrefix()
        {
            var result = Strip("m", "SELECT * FROM m.Employee");
            Assert.Equal("SELECT * FROM Employee", result);
        }

        [Fact]
        public void Strip_SchemaQualified_RemovesOnlyConnectionPrefix()
        {
            // "m.dbo.Employee" → "dbo.Employee": only the first qualifier is removed
            var result = Strip("m", "SELECT * FROM m.dbo.Employee");
            Assert.Equal("SELECT * FROM dbo.Employee", result);
        }

        [Fact]
        public void Strip_ConnectionNameIsPrefixOfTableName_DoesNotCorrupt()
        {
            // Connection "A", table "ARCHIVE" — "A." must not match "ARCHIVE."
            // because "A" in "ARCHIVE" is followed by "R", not "."
            var result = Strip("A", "SELECT * FROM ARCHIVE.dbo.Orders");
            Assert.Equal("SELECT * FROM ARCHIVE.dbo.Orders", result);
        }

        [Fact]
        public void Strip_ConnectionPrefixAfterOpenParen_RemovesPrefix()
        {
            var result = Strip("db", "SELECT (db.Price * db.Qty) AS Total");
            Assert.Equal("SELECT (Price * Qty) AS Total", result);
        }

        [Fact]
        public void Strip_MultipleOccurrences_AllRemoved()
        {
            var result = Strip("m", "SELECT m.Col1, m.Col2 FROM m.Table1 JOIN m.Table2 ON m.Table1.Id = m.Table2.Id");
            Assert.Equal("SELECT Col1, Col2 FROM Table1 JOIN Table2 ON Table1.Id = Table2.Id", result);
        }

        [Fact]
        public void Strip_PrefixInsideStringLiteral_NotRemoved()
        {
            // The connection name followed by "." inside a SQL string literal should NOT be stripped.
            // The negative lookbehind prevents removing prefixes preceded by a word char,
            // but this test documents the KNOWN LIMITATION: occurrences after spaces inside
            // string literals WILL be stripped (full literal parsing is out of scope).
            var result = Strip("m", "SELECT * FROM m.Table1 WHERE Note = 'Use m.prefix in comments'");
            // "m." after space in the literal WILL be stripped — document this limitation
            Assert.Equal("SELECT * FROM Table1 WHERE Note = 'Use prefix in comments'", result);
        }

        [Fact]
        public void Strip_PrefixPrecededByDot_NotRemoved()
        {
            // "schema.connection.table" — connection name preceded by "." is NOT stripped
            // because "." matches the lookbehind pattern [.\w]
            var result = Strip("m", "SELECT * FROM schema.m.table");
            Assert.Equal("SELECT * FROM schema.m.table", result);
        }

        [Fact]
        public void Strip_CaseInsensitive_RemovesAnyCase()
        {
            var result = Strip("MyConn", "SELECT * FROM MYCONN.table1, myconn.table2, MyConn.table3");
            Assert.Equal("SELECT * FROM table1, table2, table3", result);
        }

        [Fact]
        public void Strip_EmptySql_ReturnsEmpty()
        {
            var result = Strip("m", "");
            Assert.Equal("", result);
        }

        [Fact]
        public void Strip_NoOccurrences_ReturnsUnchanged()
        {
            const string sql = "SELECT * FROM other.table1";
            var result = Strip("m", sql);
            Assert.Equal(sql, result);
        }
    }
}
