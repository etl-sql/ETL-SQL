using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Analysis.Statements
{
    public class TaggingStatementTests
    {
        private static Evaluator NewEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task ShowTags_ForTable_ReturnsTableTags()
        {
            var eval = NewEval();

            // Record a lineage entry with table-level tags
            var metadata = new Dictionary<string, string> { { "owner", "admin" }, { "sensitivity", "high" } };
            // Simulate a table creation or tag application
            eval.LineageTracker.Record("MyTable", new List<string>(), "CREATE", null, null, metadata);

            var script = TestHelpers.Parse("SHOW TAGS FOR TABLE MyTable;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            var resultKeys = eval.LastResult!.Rows.Select(r => r["TagName"]?.ToString()).ToList();
            var resultVals = eval.LastResult!.Rows.Select(r => r["TagValue"]?.ToString()).ToList();

            Assert.Contains("owner", resultKeys);
            Assert.Contains("admin", resultVals);
        }

        [Fact]
        public async Task ShowTags_ForColumn_ReturnsColumnTags()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "d", "User ID" } };
            eval.LineageTracker.Record("MyTable", new List<string>(), "CREATE", "UserId", null, metadata);

            var script = TestHelpers.Parse("SHOW TAGS FOR TABLE MyTable COLUMN UserId;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("d", eval.LastResult.Rows[0]["TagName"]?.ToString());
            Assert.Equal("User ID", eval.LastResult.Rows[0]["TagValue"]?.ToString());
        }

        [Fact]
        public async Task ShowTagValue_ForTable_ReturnsSpecificValue()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "owner", "admin" }, { "sensitivity", "high" } };
            eval.LineageTracker.Record("TargetTable", new List<string>(), "UPDATE", null, null, metadata);

            var script = TestHelpers.Parse("SHOW TAG VALUE FOR TABLE TargetTable WITH TAG owner;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("owner", eval.LastResult.Rows[0]["TagName"]?.ToString());
            Assert.Equal("admin", eval.LastResult.Rows[0]["TagValue"]?.ToString());
        }

        [Fact]
        public async Task ShowTagValue_ForColumn_ReturnsSpecificValue()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "d", "Email Address" }, { "pii", "true" } };
            eval.LineageTracker.Record("Users", new List<string>(), "INSERT", "Email", null, metadata);

            var script = TestHelpers.Parse("SHOW TAG VALUE FOR TABLE Users COLUMN Email WITH TAG pii;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("pii", eval.LastResult.Rows[0]["TagName"]?.ToString());
            Assert.Equal("true", eval.LastResult.Rows[0]["TagValue"]?.ToString());
        }

        [Fact]
        public async Task ShowTags_IntoTempTable_PopulatesDestination()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "author", "ETL_SQL" } };
            eval.LineageTracker.Record("TempSource", new List<string>(), "CREATE", null, null, metadata);

            var script = TestHelpers.Parse(@"
                SHOW TAGS FOR TABLE TempSource INTO #MyTags;
                SELECT * FROM #MyTags;
            ");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("author", eval.LastResult.Rows[0]["TagName"]?.ToString());
        }

        // ── SHOW SCRIPT TAGS / -- @tag support ───────────────────────────────

        [Fact]
        public void ParseShowScriptTags_AlternativeSyntax_ParsesCorrectly()
        {
            // Both "SHOW SCRIPT TAGS" and "SHOW SCRIPT TAG" should parse
            var stmtPlural = TestHelpers.Parse("SHOW SCRIPT TAGS;").Statements.OfType<ShowScriptTagsStatement>().FirstOrDefault();
            var stmtSingular = TestHelpers.Parse("SHOW SCRIPT TAG;").Statements.OfType<ShowScriptTagsStatement>().FirstOrDefault();
            Assert.NotNull(stmtPlural);
            Assert.NotNull(stmtSingular);
        }

        [Fact]
        public async Task ShowScriptTags_AlternativeSyntax_ReturnsScriptMetadata()
        {
            var eval = NewEval();
            // Script-header tags (block comment) feed into GlobalMetadata via script.Metadata
            var script = TestHelpers.Parse("/* @owner: DataEngineering; @version: 2.1; */\nSHOW SCRIPT TAGS;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            var tags = eval.LastResult!.Rows.ToDictionary(r => r["TagName"]!.ToString()!, r => r["TagValue"]!.ToString()!);
            Assert.True(tags.ContainsKey("owner"));
            Assert.Equal("DataEngineering", tags["owner"]);
        }

        [Fact]
        public async Task ShowScriptTags_IntoTemp_AlternativeSyntax_Works()
        {
            var eval = NewEval();
            // Use -- @tag header syntax (new Lexer support) to populate script metadata
            var script = TestHelpers.Parse("-- @pipeline: DailySales\nSHOW SCRIPT TAGS INTO #tags;\nSELECT * FROM #tags;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Contains(eval.LastResult!.Rows, r => r["TagName"]?.ToString() == "pipeline" && r["TagValue"]?.ToString() == "DailySales");
        }

        [Fact]
        public void Lexer_LineCommentTag_EmitsColumnTagToken()
        {
            // "-- @pii: true" should produce a COLUMN_TAG token, not be discarded
            var tokens = new Lexer("-- @pii: true").Tokenize();
            Assert.Contains(tokens, t => t.Type == TokenType.COLUMN_TAG);
            var tag = tokens.First(t => t.Type == TokenType.COLUMN_TAG);
            Assert.StartsWith("@pii", tag.Value);
        }

        [Fact]
        public void Lexer_RegularLineComment_IsDiscarded()
        {
            // "-- regular comment" (no @) should NOT produce any token
            var tokens = new Lexer("-- just a comment\nSELECT 1;").Tokenize();
            Assert.DoesNotContain(tokens, t => t.Type == TokenType.COLUMN_TAG);
        }

        [Fact]
        public void Parser_LineCommentHeaderTag_CapturedInScriptMetadata()
        {
            // Script-header "-- @tag: value" should populate script.Metadata
            var source = "-- @owner: TeamA\n-- @version: 3.0\nSELECT 1;";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();
            Assert.True(script.Metadata.ContainsKey("owner"));
            Assert.Equal("TeamA", script.Metadata["owner"]);
            Assert.Equal("3.0", script.Metadata["version"]);
        }
    }
}
