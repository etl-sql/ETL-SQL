using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
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
        public async Task EngTags_ForTable_ReturnsTableTags()
        {
            var eval = NewEval();

            // Record a lineage entry with table-level tags
            var metadata = new Dictionary<string, string> { { "owner", "admin" }, { "sensitivity", "high" } };
            // Simulate a table creation or tag application
            eval.LineageTracker.Record("MyTable", new List<string>(), "CREATE", null, null, metadata);

            var script = TestHelpers.Parse("SELECT tag_name, tag_value FROM eng.tags WHERE target_table = 'MyTable' AND scope = 'table';");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            var resultKeys = eval.LastResult!.Rows.Select(r => r["tag_name"]?.ToString()).ToList();
            var resultVals = eval.LastResult!.Rows.Select(r => r["tag_value"]?.ToString()).ToList();

            Assert.Contains("owner", resultKeys);
            Assert.Contains("admin", resultVals);
        }

        [Fact]
        public async Task EngTags_ForColumn_ReturnsColumnTags()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "d", "User ID" } };
            eval.LineageTracker.Record("MyTable", new List<string>(), "CREATE", "UserId", null, metadata);

            var script = TestHelpers.Parse("SELECT tag_name, tag_value FROM eng.tags WHERE target_table = 'MyTable' AND target_column = 'UserId';");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("d", eval.LastResult.Rows[0]["tag_name"]?.ToString());
            Assert.Equal("User ID", eval.LastResult.Rows[0]["tag_value"]?.ToString());
        }

        [Fact]
        public async Task EngTags_ForTable_ReturnsSpecificValue()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "owner", "admin" }, { "sensitivity", "high" } };
            eval.LineageTracker.Record("TargetTable", new List<string>(), "UPDATE", null, null, metadata);

            var script = TestHelpers.Parse("SELECT tag_name, tag_value FROM eng.tags WHERE target_table = 'TargetTable' AND tag_name = 'owner';");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("owner", eval.LastResult.Rows[0]["tag_name"]?.ToString());
            Assert.Equal("admin", eval.LastResult.Rows[0]["tag_value"]?.ToString());
        }

        [Fact]
        public async Task EngTags_ForColumn_ReturnsSpecificValue()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "d", "Email Address" }, { "pii", "true" } };
            eval.LineageTracker.Record("Users", new List<string>(), "INSERT", "Email", null, metadata);

            var script = TestHelpers.Parse("SELECT tag_name, tag_value FROM eng.tags WHERE target_table = 'Users' AND target_column = 'Email' AND tag_name = 'pii';");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("pii", eval.LastResult.Rows[0]["tag_name"]?.ToString());
            Assert.Equal("true", eval.LastResult.Rows[0]["tag_value"]?.ToString());
        }

        [Fact]
        public async Task EngTags_IntoTempTable_PopulatesDestination()
        {
            var eval = NewEval();

            var metadata = new Dictionary<string, string> { { "author", "ETL_SQL" } };
            eval.LineageTracker.Record("TempSource", new List<string>(), "CREATE", null, null, metadata);

            var script = TestHelpers.Parse(@"
                SELECT tag_name, tag_value INTO #MyTags FROM eng.tags WHERE target_table = 'TempSource';
                SELECT * FROM #MyTags;
            ");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("author", eval.LastResult.Rows[0]["tag_name"]?.ToString());
        }

        // ── Retired SHOW tag forms / -- @tag support ─────────────────────────

        [Theory]
        [InlineData("SHOW TAGS FOR TABLE MyTable;")]
        [InlineData("SHOW TAG VALUE FOR TABLE Users COLUMN Email WITH TAG pii;")]
        [InlineData("SHOW SCRIPT TAGS;")]
        [InlineData("SHOW SCRIPT TAG;")]
        public void RetiredShowTagForms_ReportEngTagsReplacement(string sql)
        {
            var script = TestHelpers.Parse(sql);

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("eng.tags", diagnostic.Message, System.StringComparison.OrdinalIgnoreCase);
            Assert.Empty(script.Statements);
        }

        [Fact]
        public async Task EngTags_ReturnsScriptMetadata()
        {
            var eval = NewEval();
            // Script-header tags (block comment) feed into GlobalMetadata via script.Metadata
            var script = TestHelpers.Parse("/* @owner: DataEngineering; @version: 2.1; */\nSELECT tag_name, tag_value FROM eng.tags WHERE scope = 'script';");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            var tags = eval.LastResult!.Rows.ToDictionary(r => r["tag_name"]!.ToString()!, r => r["tag_value"]!.ToString()!);
            Assert.True(tags.ContainsKey("owner"));
            Assert.Equal("DataEngineering", tags["owner"]);
        }

        [Fact]
        public async Task EngTags_ScriptMetadataIntoTemp_Works()
        {
            var eval = NewEval();
            // Use -- @tag header syntax (new Lexer support) to populate script metadata
            var script = TestHelpers.Parse("-- @pipeline: DailySales\nSELECT tag_name, tag_value INTO #tags FROM eng.tags WHERE scope = 'script';\nSELECT * FROM #tags;");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            Assert.Contains(eval.LastResult!.Rows, r => r["tag_name"]?.ToString() == "pipeline" && r["tag_value"]?.ToString() == "DailySales");
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

        [Fact]
        public async Task CreateTable_SeedsInlineColumnTags()
        {
            var eval = NewEval();
            var script = TestHelpers.Parse(@"
                CREATE TABLE #tagged_table (
                    Id INT NOT NULL PRIMARY KEY /*@d: The unique ID; @owner: sales*/,
                    Name VARCHAR(100)           /*@pii: true*/
                );
                SELECT tag_name, tag_value FROM eng.tags WHERE target_table = '#tagged_table' AND target_column = 'Id';
            ");
            await eval.Evaluate(script);

            Assert.NotNull(eval.LastResult);
            var idTags = eval.LastResult!.Rows.ToDictionary(r => r["tag_name"]!.ToString()!, r => r["tag_value"]!.ToString()!);
            Assert.Contains("d", idTags.Keys);
            Assert.Equal("The unique ID", idTags["d"]);
            Assert.Contains("owner", idTags.Keys);
            Assert.Equal("sales", idTags["owner"]);

            var showNameTags = TestHelpers.Parse("SELECT tag_name, tag_value FROM eng.tags WHERE target_table = '#tagged_table' AND target_column = 'Name';");
            await eval.Evaluate(showNameTags);
            Assert.NotNull(eval.LastResult);
            var nameTags = eval.LastResult!.Rows.ToDictionary(r => r["tag_name"]!.ToString()!, r => r["tag_value"]!.ToString()!);
            Assert.Contains("pii", nameTags.Keys);
            Assert.Equal("true", nameTags["pii"]);
        }
    }
}
