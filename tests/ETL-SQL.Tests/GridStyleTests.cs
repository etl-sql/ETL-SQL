using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Parser.Components;
using ETL_SQL.Core;
using System.Linq;
using System.Collections.Generic;

namespace ETL_SQL.Tests
{
    public class GridStyleTests
    {
        [Fact]
        public void CreateVisual_ShouldParseGridList()
        {
            string script = @"
            CREATE VISUAL SalesTable AS TABLE (
              SOURCE = #sales,
              OPTIONS (
                GRID = (HEADER, FOOTER, LEFT),
                SHOW_NO_DATA_PLACEHOLDER = ON
              )
            );";

            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var statements = new List<Statement>();
            while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());

            var cv = (CreateVisualStatement)statements[0];
            var gridOpt = cv.Options.First(o => o.Key == "GRID");
            
            Assert.Equal("HEADER,FOOTER,LEFT", gridOpt.Value);
        }

        [Fact]
        public void CreateVisual_ShouldParseGridSingleValue()
        {
            string script = @"
            CREATE VISUAL AllTable AS TABLE (
              SOURCE = #sales,
              OPTIONS (
                GRID = ALL
              )
            );";

            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var statements = new List<Statement>();
            while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());

            var cv = (CreateVisualStatement)statements[0];
            var gridOpt = cv.Options.First(o => o.Key == "GRID");
            
            Assert.Equal("ALL", gridOpt.Value);
        }
        
        [Fact]
        public void CreateVisual_ShouldParseGridNone()
        {
            string script = @"
            CREATE VISUAL NoneTable AS TABLE (
              SOURCE = #sales,
              OPTIONS (
                GRID = NONE
              )
            );";

            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var statements = new List<Statement>();
            while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());

            var cv = (CreateVisualStatement)statements[0];
            var gridOpt = cv.Options.First(o => o.Key == "GRID");
            
            Assert.Equal("NONE", gridOpt.Value);
        }
    }
}
