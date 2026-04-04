using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using Spectre.Console;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.UI;

namespace ETL_SQL.Tests
{
    public class LexerTests
    {
        [Fact]
        public void TestBasicKeywords()
        {
            var source = "SELECT * FROM MyTable";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            Assert.Equal(4 + 1, tokens.Count);
            Assert.Equal(TokenType.SELECT, tokens[0].Type);
            Assert.Equal(TokenType.STAR, tokens[1].Type);
            Assert.Equal(TokenType.FROM, tokens[2].Type);
            Assert.Equal(TokenType.IDENTIFIER, tokens[3].Type);
            Assert.Equal(TokenType.EOF, tokens[4].Type);
        }

        [Fact]
        public void TestLiterals()
        {
            var source = "123 'hello' 45.67";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            Assert.Equal(3 + 1, tokens.Count);
            Assert.Equal(TokenType.NUMBER, tokens[0].Type);
            Assert.Equal("123", tokens[0].Value.ToString());
            Assert.Equal(TokenType.STRING, tokens[1].Type);
            Assert.Equal("hello", tokens[1].Value.ToString());
            Assert.Equal(TokenType.NUMBER, tokens[2].Type);
            Assert.Equal("45.67", tokens[2].Value.ToString());
        }

        [Fact]
        public void TestOperators()
        {
            var source = "+ - * / = != < > <= >=";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            Assert.Equal(10 + 1, tokens.Count);
            Assert.Equal(TokenType.PLUS, tokens[0].Type);
        }

        [Fact]
        public void TestComments()
        {
            var source = "SELECT -- comment\n* /* multi\nline */ FROM";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            Assert.Equal(3 + 1, tokens.Count);
            Assert.Equal(TokenType.SELECT, tokens[0].Type);
            Assert.Equal(TokenType.STAR, tokens[1].Type);
            Assert.Equal(TokenType.FROM, tokens[2].Type);
        }

        [Fact]
        public void TestVariables()
        {
            var source = "@var1 @my_var";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            Assert.Equal(2 + 1, tokens.Count);
            Assert.Equal(TokenType.VARIABLE, tokens[0].Type);
            Assert.Equal("@var1", tokens[0].Value.ToString());
            Assert.Equal(TokenType.VARIABLE, tokens[1].Type);
            Assert.Equal("@my_var", tokens[1].Value.ToString());
        }

        [Fact]
        public void TestIdentifiers()
        {
            var source = "Table1 [Quoted Identifier] #TempTable";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            Assert.Equal(3 + 1, tokens.Count);
            Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
            Assert.Equal("Table1", tokens[0].Value.ToString());
            Assert.Equal(TokenType.IDENTIFIER, tokens[1].Type);
            Assert.Equal("Quoted Identifier", tokens[1].Value.ToString());
            Assert.Equal(TokenType.IDENTIFIER, tokens[2].Type);
            Assert.Equal("#TempTable", tokens[2].Value.ToString());
        }
    }
}
