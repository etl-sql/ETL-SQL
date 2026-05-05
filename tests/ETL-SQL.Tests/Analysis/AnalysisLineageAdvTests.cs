using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Analysis
{
    public class LineageAdvancedTests
    {
        [Fact]
        public async Task TestTableLevelMetadata()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            var tracker = services.GetRequiredService<ILineageTracker>();
            
            await ev.Evaluate(Parse("CREATE TABLE #Src (A INT);"));
            var script = "SELECT A FROM #Src AS s /* @owner: chuck; @d: source table; */;";
            await ev.Evaluate(Parse(script));
            
            var tableLineage = tracker.GetFullLineage().Where(e => e.Operation == "TABLE_TAGS").ToList();
            Assert.NotEmpty(tableLineage);
            var entry = tableLineage.First();
            Assert.Equal("#Src", entry.TargetTable);
            Assert.Equal("chuck", entry.Metadata["owner"]);
        }

        [Fact]
        public async Task TestLineageInheritanceAndAmalgamation()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            var tracker = services.GetRequiredService<ILineageTracker>();
            
            await ev.Evaluate(Parse("CREATE TABLE #A (ColA INT);"));
            await ev.Evaluate(Parse("CREATE TABLE #B (ColB INT);"));
            
            // Step 1: Tag columns in #A and #B
            await ev.Evaluate(Parse("SELECT ColA /* @d: Description A; @tag: valA; */ INTO #A_Tagged FROM #A;"));
            await ev.Evaluate(Parse("SELECT ColB /* @d: Description B; @tag: valB; */ INTO #B_Tagged FROM #B;"));
            
            // Step 2: Combine them into #C
            await ev.Evaluate(Parse("SELECT ColA + ColB AS Combined INTO #C FROM #A_Tagged JOIN #B_Tagged ON 1=1;"));
            
            var lineage = tracker.GetColumnLineage("#C", "Combined").ToList();
            Assert.NotEmpty(lineage);
            var entry = lineage.First();
            
            // Last-seen wins for @d (Description B since it's the second in the expression)
            Assert.Equal("Description B", entry.Description);
            
            // Amalgamation
            Assert.Contains("ColA: Description A", entry.DerivedFromDescriptions);
            Assert.Contains("ColB: Description B", entry.DerivedFromDescriptions);

            // Inherited tag
            Assert.Equal("valB", entry.Metadata["tag"]);
        }

        [Fact]
        public async Task TestQueryableLineage()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            
            await ev.Evaluate(Parse("CREATE TABLE #Simple (Id INT);"));
            await ev.Evaluate(Parse("SELECT Id /* @d: SimpleId; */ INTO #Target FROM #Simple;"));
            
            // Query the lineage
            var query = "SELECT Operation, TargetTable, TargetColumn, Description FROM LINEAGE(#Target);";
            await ev.Evaluate(Parse(query));
            
            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0);
            
            var row = result.Rows.First(r => r["TargetColumn"]?.ToString() == "Id");
            Assert.Equal("SELECT INTO", row["Operation"]);
            Assert.Equal("SimpleId", row["Description"]);
        }

        [Fact]
        public async Task TestMarkdownExport()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();
            string path = "test_lineage.md";
            if (File.Exists(path)) File.Delete(path);

            try 
            {
                await ev.Evaluate(Parse("CREATE TABLE #Src (X INT);"));
                // Tag the source column so it can be inherited
                await ev.Evaluate(Parse("SELECT X /* @d: Initial X; */ INTO #Export FROM #Src;"));
                
                await ev.Evaluate(Parse("SELECT X /* @d: To export; @sensitive: true; */ INTO #Exported FROM #Export /* @owner: chuck; */;"));
                
                await ev.Evaluate(Parse($"LINEAGE(#Exported) TO '{path}';"));
                
                Assert.True(File.Exists(path));
                string content = File.ReadAllText(path);
                
                // Structured content checks
                Assert.Contains("# Data Lineage Report", content);
                Assert.Contains("## Visual Graph", content);
                Assert.Contains("```mermaid", content);
                Assert.Contains("graph TD", content);
                
                // Edge verification: #Export (Source) --> #Exported (Target)
                Assert.Contains("-->", content);
                Assert.Contains("#Export", content);
                Assert.Contains("#Exported", content);

                // Metadata verification
                Assert.Contains("| Timestamp | Operation | Sources | Metadata |", content); // Audit log header
                Assert.Contains("owner", content);
                Assert.Contains("chuck", content);
                Assert.Contains("To export", content);
                Assert.Contains("sensitive", content);
                Assert.Contains("true", content);

                // Lineage Chain and Inheritance
                // Note: The handler uses ## Column: X when a column is specified
                // But in this test, we might be seeing it in the metadata or description.
                Assert.Contains("Detailed Audit Log", content);
                Assert.Contains("Derived From", content);
                Assert.Contains("#Export.X", content);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task LineageTags_VirtualTable_ExposesFlatTagRows()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
SELECT
    id   /* @pii: true; @d: Primary key */,
    name /* @sensitive: true */
INTO #tagged
FROM (SELECT 1 AS id, 'Alice' AS name) AS x;

SELECT TagName, TagValue
INTO #result
FROM LINEAGE_TAGS
WHERE TargetTable = '#tagged';

SELECT * FROM #result;
";
            await ev.Evaluate(Parse(script));

            var rows = ev.LastResult?.Rows ?? new List<Row>();
            Assert.Contains(rows, r => r["TagName"]?.ToString() == "pii" && r["TagValue"]?.ToString() == "true");
            Assert.Contains(rows, r => r["TagName"]?.ToString() == "sensitive" && r["TagValue"]?.ToString() == "true");
        }

        [Fact]
        public async Task LineageTags_VirtualTable_HasCorrectScope()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
SELECT id /* @owner: Finance */
INTO #t
FROM (SELECT 1 AS id) AS x;

SELECT Scope
INTO #result
FROM LINEAGE_TAGS
WHERE TargetTable = '#t' AND TagName = 'owner';

SELECT * FROM #result;
";
            await ev.Evaluate(Parse(script));

            var rows = ev.LastResult?.Rows ?? new List<Row>();
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Equal("column", r["Scope"]?.ToString()));
        }

        [Fact]
        public async Task HasTag_ReturnsTrueWhenTagExists()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
SELECT email /* @pii: true */
INTO #users
FROM (SELECT 'a@b.com' AS email) AS x;

SELECT HAS_TAG('#users', 'email', 'pii') AS has_pii;
";
            await ev.Evaluate(Parse(script));

            var rows = ev.LastResult?.Rows ?? new List<Row>();
            Assert.Single(rows);
            Assert.Equal(1m, rows[0]["has_pii"]);
        }

        [Fact]
        public async Task HasTag_WithExpectedValue_ReturnsTrueOnMatch()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
SELECT email /* @pii: true */
INTO #users
FROM (SELECT 'a@b.com' AS email) AS x;

SELECT
    HAS_TAG('#users', 'email', 'pii', 'true') AS match,
    HAS_TAG('#users', 'email', 'pii', 'false') AS no_match;
";
            await ev.Evaluate(Parse(script));

            var rows = ev.LastResult?.Rows ?? new List<Row>();
            Assert.Single(rows);
            Assert.Equal(1m, rows[0]["match"]);
            Assert.Equal(0m, rows[0]["no_match"]);
        }

        [Fact]
        public async Task HasTag_ReturnsFalseWhenTagMissing()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
SELECT name
INTO #t
FROM (SELECT 'Alice' AS name) AS x;

SELECT HAS_TAG('#t', 'name', 'pii') AS result;
";
            await ev.Evaluate(Parse(script));

            var rows = ev.LastResult?.Rows ?? new List<Row>();
            Assert.Single(rows);
            Assert.Equal(0m, rows[0]["result"]);
        }

        [Fact]
        public async Task ForeachLoop_RecordsLoopVariableLineage()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var tracker = services.GetRequiredService<ILineageTracker>();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
CREATE TABLE #src (id INT, val VARCHAR);
INSERT INTO #src VALUES (1, 'a'), (2, 'b');

DECLARE @row OBJECT;
FOREACH @row IN (SELECT id, val FROM #src)
BEGIN
    PRINT @row.val;
END;
";
            await ev.Evaluate(Parse(script));

            var loopEntries = tracker.GetFullLineage()
                .Where(e => e.Operation == "FOREACH_LOOP")
                .ToList();

            Assert.NotEmpty(loopEntries);
            var entry = loopEntries.First();
            Assert.Equal("@row", entry.TargetColumn);
            Assert.Contains("#src", entry.SourceTables, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ForLoop_RecordsCounterVariableLineage()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var tracker = services.GetRequiredService<ILineageTracker>();
            var ev = services.GetRequiredService<Evaluator>();

            var script = @"
DECLARE @i INT = 0;
FOR @i = 1 TO 3
BEGIN
    SET @i = @i;
END;
";
            await ev.Evaluate(Parse(script));

            var loopEntries = tracker.GetFullLineage()
                .Where(e => e.Operation == "FOR_LOOP")
                .ToList();

            Assert.NotEmpty(loopEntries);
            Assert.Equal("@i", loopEntries.First().TargetColumn);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
