using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;

namespace ETL_SQL.Tests.Statements
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
    }
}
