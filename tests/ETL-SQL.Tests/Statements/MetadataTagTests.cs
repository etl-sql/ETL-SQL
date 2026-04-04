using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests
{
    public class MetadataTagTests
    {
        [Fact]
        public void TestParseSingleTag()
        {
            var script = Parse("SELECT col1 /*@d: desc;*/ FROM src;");
            var select = (SelectStatement)script.Statements[0];
            var col = select.Columns[0];
            
            Assert.Equal("desc", col.Description);
            Assert.True(col.Metadata.ContainsKey("d"));
            Assert.Equal("desc", col.Metadata["d"]);
        }

        [Fact]
        public void TestParseMultipleTags()
        {
            var script = Parse("SELECT col1 /*@d: description; @owner: John; @sensitivity: PII; */ FROM src;");
            var select = (SelectStatement)script.Statements[0];
            var col = select.Columns[0];
            
            Assert.Equal("description", col.Description);
            Assert.Equal("John", col.Metadata["owner"]);
            Assert.Equal("PII", col.Metadata["sensitivity"]);
        }

        [Fact]
        public void TestParseMultipleCommentBlocks()
        {
            var script = Parse("SELECT col1 /*@tag1: val1; */ /* @tag2: val2; */ FROM src;");
            var select = (SelectStatement)script.Statements[0];
            var col = select.Columns[0];
            
            Assert.Equal("val1", col.Metadata["tag1"]);
            Assert.Equal("val2", col.Metadata["tag2"]);
        }

        [Fact]
        public async Task TestLineageMetadataCapture()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            var tracker = services.GetRequiredService<ILineageTracker>();
            
            await ev.Evaluate(Parse("CREATE TABLE #S (A INT);"));
            await ev.Evaluate(Parse("CREATE TABLE #D (B INT);"));
            await ev.Evaluate(Parse("INSERT INTO #D (B) SELECT A /* @owner: Admin; @d: My column; */ FROM #S;"));
            
            var lineage = tracker.GetFullLineage().Where(e => e.TargetColumn == "B").ToList();
            Assert.NotEmpty(lineage);
            var entry = lineage.First();
            
            Assert.Equal("Admin", entry.Metadata["owner"]);
            Assert.Equal("My column", entry.Metadata["d"]);
            Assert.Equal("My column", entry.Description);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
