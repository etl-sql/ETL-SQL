using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class CreateTableTagTests
    {
        [Fact]
        public void TestCreateTableWithTags()
        {
            string sql = @"
                CREATE TABLE #test (
                    id INT /* @PK: true */,
                    name VARCHAR(100) /* @PII: Yes */
                );";

            var script = Parse(sql);
            Assert.Single(script.Statements);
            var stmt = script.Statements[0] as CreateTableStatement;
            Assert.NotNull(stmt);
            Assert.Equal(2, stmt.Columns.Count);

            var idCol = stmt.Columns[0];
            Assert.Equal("id", idCol.ColumnName);
            Assert.True(idCol.Metadata.ContainsKey("PK"));
            Assert.Equal("true", idCol.Metadata["PK"]);

            var nameCol = stmt.Columns[1];
            Assert.Equal("name", nameCol.ColumnName);
            Assert.True(nameCol.Metadata.ContainsKey("PII"));
            Assert.Equal("Yes", nameCol.Metadata["PII"]);
        }

        [Fact]
        public void TestCreateTableWithTrailingTags()
        {
            string sql = @"
                CREATE TABLE #test (
                    id INT PRIMARY KEY /* @PK: true */ /* @Internal: yes */,
                    status VARCHAR(20) NOT NULL /* @State: Active */
                );";

            var script = Parse(sql);
            Assert.Single(script.Statements);
            var stmt = script.Statements[0] as CreateTableStatement;
            Assert.NotNull(stmt);

            var idCol = stmt.Columns[0];
            Assert.True(idCol.IsPrimaryKey);
            Assert.True(idCol.Metadata.ContainsKey("PK"));
            Assert.Equal("true", idCol.Metadata["PK"]);
            Assert.True(idCol.Metadata.ContainsKey("Internal"));
            Assert.Equal("yes", idCol.Metadata["Internal"]);

            var statusCol = stmt.Columns[1];
            Assert.False(statusCol.IsNullable);
            Assert.True(statusCol.Metadata.ContainsKey("State"));
            Assert.Equal("Active", statusCol.Metadata["State"]);
        }

        [Fact]
        public void TestCreateTableToSqlSerializesTags()
        {
            string sql = @"CREATE TABLE #test (id INT /* @PK: true */);";
            var script = Parse(sql);
            var stmt = script.Statements[0] as CreateTableStatement;
            Assert.NotNull(stmt);

            string generatedSql = stmt.ToSql();
            Assert.Contains("/* @PK: true */", generatedSql);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
