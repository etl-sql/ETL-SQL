using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class ExpressionParserTests
    {
        private static Expression Parse(string input)
        {
            var lexer = new Lexer(input);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.ParseExpression();
        }

        [Fact]
        public void ParseArithmetic()
        {
            var expr = Parse("1 + 2 * 3 - 4 / 2");
            Assert.NotNull(expr);
            Assert.IsType<BinaryExpression>(expr);
        }

        [Theory]
        [InlineData("SELECT 'Dept ' || id AS cat FROM t;")]
        [InlineData("SELECT ('Dept ' || id) AS cat FROM t;")]
        public void ParseConcatenation_AllowsSelectAlias(string source)
        {
            var script = new Parser(new Lexer(source).Tokenize(), source).Parse();

            Assert.Empty(script.Diagnostics);
            var select = Assert.IsType<SelectStatement>(Assert.Single(script.Statements));
            var column = Assert.Single(select.Columns);
            Assert.Equal("cat", column.Alias);
            var concat = Assert.IsType<BinaryExpression>(column.Expression);
            Assert.Equal(TokenType.CONCAT, concat.Operator);
            Assert.Equal("Dept ", Assert.IsType<LiteralExpression>(concat.Left).Value);
            Assert.Equal("id", Assert.IsType<IdentifierExpression>(concat.Right).Name);
            Assert.Equal("('Dept ' || id)", concat.ToSql());
        }

        [Fact]
        public void ParseConcatenation_BindsBelowArithmeticAndAboveComparison()
        {
            var expr = Parse("'Dept ' || id + 1 = 'Dept 2'");

            var comparison = Assert.IsType<BinaryExpression>(expr);
            Assert.Equal(TokenType.EQUALS, comparison.Operator);
            var concat = Assert.IsType<BinaryExpression>(comparison.Left);
            Assert.Equal(TokenType.CONCAT, concat.Operator);
            Assert.Equal(TokenType.PLUS, Assert.IsType<BinaryExpression>(concat.Right).Operator);
        }

        [Fact]
        public void ParseComparison()
        {
            var expr = Parse("@val > 10 AND @val <= 20");
            Assert.NotNull(expr);
            Assert.IsType<BinaryExpression>(expr);
        }

        [Fact]
        public void ParseFunctionCall()
        {
            var expr = Parse("SUBSTRING('Hello World', 1, 5)");
            Assert.IsAssignableFrom<FunctionCallExpression>(expr);
            var func = (FunctionCallExpression)expr!;
            Assert.Equal("SUBSTRING", func.FunctionName);
            Assert.Equal(3, func.Arguments.Count);
        }

        [Fact]
        public void ParseCaseExpression()
        {
            var expr = Parse("CASE WHEN @x = 1 THEN 'One' WHEN @x = 2 THEN 'Two' ELSE 'Other' END");
            Assert.IsType<CaseExpression>(expr);
            var caseExpr = (CaseExpression)expr!;
            Assert.Equal(2, caseExpr.WhenClauses.Count);
            Assert.NotNull(caseExpr.ElseResult);
        }

        [Fact]
        public void ParseInExpression()
        {
            var expr = Parse("@x IN (1, 2, 3)");
            Assert.IsType<InExpression>(expr);
            var inExpr = (InExpression)expr!;
            Assert.False(inExpr.IsNot);
            Assert.IsType<ListExpression>(inExpr.Right);
        }

        [Fact]
        public void ParseNotInExpression()
        {
            var expr = Parse("@x NOT IN (1, 2, 3)");
            Assert.IsType<InExpression>(expr);
            var inExpr = (InExpression)expr!;
            Assert.True(inExpr.IsNot);
        }

        [Fact]
        public void ParseLikeExpression()
        {
            var expr = Parse("@name LIKE 'A%'");
            Assert.IsType<LikeExpression>(expr);
            var likeExpr = (LikeExpression)expr!;
            Assert.False(likeExpr.IsNot);
        }

        [Fact]
        public void ParseNotLikeExpression()
        {
            var expr = Parse("@name NOT LIKE 'A%'");
            Assert.IsType<LikeExpression>(expr);
            var likeExpr = (LikeExpression)expr!;
            Assert.True(likeExpr.IsNot);
        }

        [Fact]
        public void ParseExistsExpression()
        {
            var expr = Parse("EXISTS (SELECT * FROM Users WHERE id = 1)");
            Assert.IsType<ExistsExpression>(expr);
            var exists = (ExistsExpression)expr!;
            Assert.False(exists.IsNot);
        }

        [Fact]
        public void ParseNotExistsExpression()
        {
            var expr = Parse("NOT EXISTS (SELECT * FROM Users WHERE id = 1)");
            Assert.IsType<ExistsExpression>(expr);
            var exists = (ExistsExpression)expr!;
            Assert.True(exists.IsNot);
        }

        [Fact]
        public void ParseCastExpression()
        {
            var expr = Parse("CAST(@x AS INT)");
            Assert.IsType<FunctionCallExpression>(expr);
            var cast = (FunctionCallExpression)expr!;
            Assert.Equal("CAST", cast.FunctionName);
        }

        [Fact]
        public void ParseListExpression()
        {
            var expr = Parse("[1, 2, 3]");
            Assert.IsType<ListExpression>(expr);
            var list = (ListExpression)expr!;
            Assert.Equal(3, list.Items.Count);
        }

        [Fact]
        public void DeeplyNestedExpression_FailsWithSyntaxException()
        {
            var expr = new string('(', 120) + "1" + new string(')', 120);

            var ex = Assert.Throws<ETL_SQL.Core.Common.Exceptions.SyntaxException>(() => Parse(expr));
            Assert.Contains("maximum supported depth", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
