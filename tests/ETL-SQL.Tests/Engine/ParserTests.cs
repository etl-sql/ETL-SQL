using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests
{
    public class ParserTests
    {
        [Fact]
        public void TestParseDeclare()
        {
            var source = "DECLARE @v INT; SET @v = 10;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Equal(2, script.Statements.Count);
            Assert.IsType<DeclareStatement>(script.Statements[0]);
            Assert.IsType<SetVariableStatement>(script.Statements[1]);
            
            var decl = (DeclareStatement)script.Statements[0];
            Assert.Equal("@v", decl.VariableName);
            Assert.Equal("INT", decl.DataType);
        }

        [Fact]
        public void TestParseSelect()
        {
            var source = "SELECT Col1, 1+1 AS Two FROM MyTable WHERE Col1 > 0 ORDER BY Col1 DESC LIMIT 10;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            Assert.IsType<SelectStatement>(script.Statements[0]);
            
            var select = (SelectStatement)script.Statements[0];
            Assert.Equal(2, select.Columns.Count);
            Assert.Equal("MyTable", select.FromTable.TableName);
            Assert.NotNull(select.WhereClause);
            Assert.Single(select.OrderBy);
            Assert.NotNull(select.LimitCount);
        }

        [Fact]
        public void TestParseCreateConnection()
        {
            var source = "CREATE CONNECTION my_conn ON FLATFILE('data.csv') WITH (DELIMITER=PIPE, HEADER=ON);";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            Assert.IsType<CreateConnectionStatement>(script.Statements[0]);
            
            var cc = (CreateConnectionStatement)script.Statements[0];
            Assert.Equal("my_conn", cc.ConnectionName);
            Assert.Equal("FLATFILE", cc.ConnectionType);
            Assert.Equal(2, cc.Options?.Count);
            Assert.Equal("PIPE", cc.Options?["DELIMITER"]);
        }

        [Fact]
        public void TestParseExpressionPrecedence()
        {
            var source = "PRINT 1 + 2 * 3;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.IsType<PrintStatement>(script.Statements[0]);
            var print = (PrintStatement)script.Statements[0];
            Assert.IsType<BinaryExpression>(print.Message);
            
            var bin = (BinaryExpression)print.Message;
            // 1 + (2 * 3) -> Top level should be +
            Assert.Equal(TokenType.PLUS, bin.Operator);
            Assert.IsType<BinaryExpression>(bin.Right);
            var rightBin = (BinaryExpression)bin.Right;
            Assert.Equal(TokenType.STAR, rightBin.Operator);
        }

        [Fact]
        public void TestParseInsert()
        {
            var source = "INSERT INTO Dest SELECT * FROM Src;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            Assert.IsType<InsertStatement>(script.Statements[0]);
            
            var insert = (InsertStatement)script.Statements[0];
            Assert.Equal("Dest", insert.TargetTable.TableName);
            Assert.NotNull(insert.SelectQuery);
        }
    }
}
