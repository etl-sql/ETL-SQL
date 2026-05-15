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

namespace ETL_SQL.Tests.Core
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
            var delimExpr = cc.Options?["DELIMITER"];
            var delimVal = delimExpr is LiteralExpression lit ? lit.Value?.ToString() : (delimExpr as IdentifierExpression)?.Name;
            Assert.Equal("PIPE", delimVal);
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
            Assert.Single(print.Arguments);
            Assert.IsType<BinaryExpression>(print.Arguments[0]);
            
            var bin = (BinaryExpression)print.Arguments[0];
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

        [Fact]
        public void TestParseQualify()
        {
            var source = "SELECT * FROM T QUALIFY ROW_NUMBER() OVER(ORDER BY ID) = 1;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            var select = (SelectStatement)script.Statements[0];
            Assert.NotNull(select.QualifyClause);
            Assert.IsType<BinaryExpression>(select.QualifyClause);
        }

        [Fact]
        public void TestParseFilterInWindow()
        {
            var source = "SELECT SUM(Val) FILTER (WHERE Val > 10) OVER(ORDER BY ID) FROM T;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            var select = (SelectStatement)script.Statements[0];
            var col = select.Columns[0];
            var fce = (FunctionCallExpression)col.Expression;
            Assert.NotNull(fce.Filter);
            Assert.IsType<BinaryExpression>(fce.Filter);
        }

        [Fact]
        public void TestParseShowLineageForms()
        {
            var script = Parse(@"
SHOW LINEAGE;
SHOW LINEAGE FOR REPORT SalesDashboard;
SHOW LINEAGE FOR DATASET &CustomerMart;
SHOW LINEAGE FOR #Target COLUMN Revenue INTO #lineage;
");

            Assert.Equal(4, script.Statements.Count);
            Assert.All(script.Statements, stmt => Assert.IsType<LineageStatement>(stmt));

            var all = (LineageStatement)script.Statements[0];
            Assert.Null(all.TargetTable);

            var report = (LineageStatement)script.Statements[1];
            Assert.Equal("report:SalesDashboard", report.TargetTable?.TableName);

            var dataset = (LineageStatement)script.Statements[2];
            Assert.Equal("dataset:CustomerMart", dataset.TargetTable?.TableName);

            var table = (LineageStatement)script.Statements[3];
            Assert.Equal("#Target", table.TargetTable?.TableName);
            Assert.Equal("Revenue", table.ColumnName);
            Assert.Equal("#lineage", table.IntoTable);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }
    }
}
