using System;
using System.Linq;
using ETL_SQL.Core.Parser;

using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    public class ToolParserTests
    {
        private Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new ETL_SQL.Core.Parser.Parser(tokens, sql).Parse();
        }

        [Fact]
        public void ParseCreateTool_ValidSyntax_ReturnsCreateToolStatement()
        {
            var sql = @"
CREATE TOOL myPythonScript AS EXECUTABLE (
    COMMAND = 'python',
    ARGS = 'script.py --date {DATE} --region {REGION}',
    WORKING_DIR = 'scripts/',
    TIMEOUT = 60
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateToolStatement>().First();

            Assert.Equal("myPythonScript", stmt.ToolName);
            Assert.Equal("EXECUTABLE", stmt.ToolType);
            Assert.NotNull(stmt.Options);
            Assert.Equal(4, stmt.Options.Count);
            
            var commandExpr = Assert.IsType<LiteralExpression>(stmt.Options["COMMAND"]);
            Assert.Equal("python", commandExpr.Value);

            var timeoutExpr = Assert.IsType<LiteralExpression>(stmt.Options["TIMEOUT"]);
            Assert.Equal(60L, timeoutExpr.Value);
        }

        [Fact]
        public void ParseExecuteTool_ValidSyntax_ReturnsExecuteToolStatement()
        {
            var sql = @"
EXECUTE TOOL 'myPythonScript'
FROM #sourceData
INTO #targetData
WITH (
    DATE = '2024-01-01',
    REGION = 'US-EAST'
)
EXPECT SCHEMA (
    id VARCHAR(50) NOT NULL,
    score INT
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<ExecuteToolStatement>().First();

            Assert.Equal("myPythonScript", stmt.ToolAlias);
            Assert.NotNull(stmt.SourceTable);
            Assert.Equal("#sourceData", stmt.SourceTable.TableName);

            Assert.NotNull(stmt.TargetTable);
            Assert.Equal("#targetData", stmt.TargetTable.TableName);

            Assert.NotNull(stmt.Parameters);
            Assert.Equal(2, stmt.Parameters.Count);
            
            var dateExpr = Assert.IsType<LiteralExpression>(stmt.Parameters["DATE"]);
            Assert.Equal("2024-01-01", dateExpr.Value);

            Assert.NotNull(stmt.ExpectedSchema);
            Assert.Equal(2, stmt.ExpectedSchema.Count);

            var idSchema = stmt.ExpectedSchema[0];
            Assert.Equal("id", idSchema.ColumnName);
            Assert.Equal("VARCHAR(50)", idSchema.DataType);
            Assert.True(idSchema.NotNull);

            var scoreSchema = stmt.ExpectedSchema[1];
            Assert.Equal("score", scoreSchema.ColumnName);
            Assert.Equal("INT", scoreSchema.DataType);
            Assert.False(scoreSchema.NotNull);
        }

        [Fact]
        public void ToSql_ExecuteTool_SerializesCorrectly()
        {
            var sql = @"EXECUTE TOOL 'myPythonScript' FROM #sourceData INTO #targetData WITH (DATE = '2024-01-01', REGION = 'US-EAST') EXPECT SCHEMA (id VARCHAR(50) NOT NULL, score INT);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<ExecuteToolStatement>().First();
            var serialized = stmt.ToSql();
            
            Assert.Equal("EXECUTE TOOL 'myPythonScript' FROM #sourceData INTO #targetData WITH (DATE = '2024-01-01', REGION = 'US-EAST') EXPECT SCHEMA (id VARCHAR(50) NOT NULL, score INT)", serialized);
        }

        [Fact]
        public void ToSql_CreateTool_SerializesCorrectly()
        {
            var sql = @"CREATE TOOL myPythonScript AS EXECUTABLE (COMMAND = 'python', TIMEOUT = 60);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateToolStatement>().First();
            var serialized = stmt.ToSql();
            
            Assert.Equal("CREATE TOOL myPythonScript AS EXECUTABLE (COMMAND = 'python', TIMEOUT = 60)", serialized);
        }
    }
}
