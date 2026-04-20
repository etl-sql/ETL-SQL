using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Parser.Components;
using ETL_SQL.Core;
using System.Linq;
using System.Collections.Generic;

namespace ETL_SQL.Tests
{
    public class DataLabelsTests
    {
        [Fact]
        public void CreateVisual_ShouldParseDataLabelsWithExtendedOptions()
        {
            string script = @"
            CREATE VISUAL MyChart AS BAR (
              SOURCE = #data,
              OPTIONS (
                DATA_LABELS = ON WITH (
                  POSITION = INSIDE_TOP_RIGHT,
                  FONT_SIZE = 14,
                  COLOR = '#FF0000',
                  FONT_WEIGHT = BOLD,
                  FORMAT = 'N2'
                )
              )
            );";

            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var statements = new List<Statement>();
            while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());

            var cv = (CreateVisualStatement)statements[0];
            
            Assert.Equal("ON", cv.Options.First(o => o.Key == "DATA_LABELS").Value);
            Assert.Equal("INSIDE_TOP_RIGHT", cv.Options.First(o => o.Key == "DATA_LABELS:POSITION").Value);
            Assert.Equal("14", cv.Options.First(o => o.Key == "DATA_LABELS:FONT_SIZE").Value);
            Assert.Equal("#FF0000", cv.Options.First(o => o.Key == "DATA_LABELS:COLOR").Value);
            Assert.Equal("BOLD", cv.Options.First(o => o.Key == "DATA_LABELS:FONT_WEIGHT").Value);
            Assert.Equal("N2", cv.Options.First(o => o.Key == "DATA_LABELS:FORMAT").Value);
        }
    }
}
