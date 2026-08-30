using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Analysis
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
        public void TestSemicolonInsideQuotedValueIsLiteral()
        {
            var script = Parse("SELECT col1 /* @example: 'IN (''a;b'', ''c'')'; @owner: Bob; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("'IN (''a;b'', ''c'')'", col.Metadata["example"]);
            Assert.Equal("Bob", col.Metadata["owner"]);
        }

        [Fact]
        public void TestCommaBetweenTagsDoesNotSwallowNextTag()
        {
            var script = Parse("SELECT col1 /* @unit: 'USD', @format: 'n2' */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("'USD'", col.Metadata["unit"]);
            Assert.Equal("'n2'", col.Metadata["format"]);
        }

        [Fact]
        public void TestDoubledQuoteInsideQuotedValueIsLiteral()
        {
            var script = Parse("SELECT col1 /* @d: 'it''s here'; @owner: Bob; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("'it''s here'", col.Metadata["d"]);
            Assert.Equal("Bob", col.Metadata["owner"]);
        }

        [Fact]
        public void TestRegexValueKeepsAtSignAndBackslashLiteral()
        {
            var script = Parse(@"SELECT col1 /* @format: '^[^@]+@[^@]+\.com$'; @owner: Bob; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal(@"'^[^@]+@[^@]+\.com$'", col.Metadata["format"]);
            Assert.Equal("Bob", col.Metadata["owner"]);
        }

        [Fact]
        public void TestDoubleQuotedValueWithCommasIsLiteral()
        {
            var script = Parse("SELECT col1 /* @example: \"IN ('NA','EMEA','APAC')\"; @owner: Bob; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("\"IN ('NA','EMEA','APAC')\"", col.Metadata["example"]);
            Assert.Equal("Bob", col.Metadata["owner"]);
        }

        [Fact]
        public void TestApostropheMidValueInUnquotedValueIsLiteral()
        {
            var script = Parse("SELECT col1 /* @d: John's column; @owner: Bob; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("John's column", col.Metadata["d"]);
            Assert.Equal("Bob", col.Metadata["owner"]);
        }

        [Fact]
        public void TestAtSignInUnquotedValueIsLiteral()
        {
            var script = Parse("SELECT col1 /* @owner: john@example.com; @d: contact; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("john@example.com", col.Metadata["owner"]);
            Assert.Equal("contact", col.Metadata["d"]);
        }

        [Fact]
        public void TestBooleanTagSeparatedByComma()
        {
            var script = Parse("SELECT col1 /* @pii, @d: masked; */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("true", col.Metadata["pii"]);
            Assert.Equal("masked", col.Metadata["d"]);
        }

        [Fact]
        public void TestUnterminatedQuoteSwallowsToEndWithoutCrash()
        {
            var script = Parse("SELECT col1 /* @example: 'abc; @owner: Bob */ FROM src;");
            var col = ((SelectStatement)script.Statements[0]).Columns[0];

            Assert.Equal("'abc; @owner: Bob", col.Metadata["example"]);
            Assert.False(col.Metadata.ContainsKey("owner"));
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
