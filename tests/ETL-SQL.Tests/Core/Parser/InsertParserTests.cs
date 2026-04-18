using Xunit;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.Core
{
    public class InsertParserTests
    {
        [Fact]
        public void TestMultiRowInsertWithComments()
        {
            string sql = @"
                INSERT INTO #Target (Col1, Col2)
                VALUES 
                (1, 'A') -- Row 1 comment
                , (2, 'B') /* Block comment */
                , (3, 'C') /* @d: Tag between rows */
                , (4, 'D');";
            
            var script = Parse(sql);
            Assert.Single(script.Statements);
            var insert = script.Statements[0] as InsertStatement;
            Assert.NotNull(insert);
            Assert.Equal(4, insert.Values.Count);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
