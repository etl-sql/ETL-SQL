using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Lineage;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Analysis.Statements
{
    /// <summary>
    /// Covers canonical tag metadata mutation and lineage import/delete statements
    /// (OpenLineage import), including the last-writer-wins semantics and the loop/same-line
    /// re-tagging case that motivated LineageTracker.ApplyTags.
    /// </summary>
    public class CreateTagLineageTests
    {
        private static Evaluator NewEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ── Parser ──────────────────────────────────────────────────────────

        [Fact]
        public void ParseCreateTag_RetiredSyntax_ReportsReplacement()
        {
            var script = TestHelpers.Parse("CREATE TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');");

            Assert.Contains(script.Diagnostics, d => d.Message.Contains("CREATE TAG has been retired"));
            Assert.Empty(script.Statements.OfType<CreateTagStatement>());
        }

        [Fact]
        public void ParseInsertTag_TableAndColumn_WithMultipleTags()
        {
            var stmt = TestHelpers.Parse("INSERT TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');")
                .Statements.OfType<CreateTagStatement>().Single();

            Assert.NotNull(stmt.ColumnName);
            Assert.Equal(2, stmt.Tags.Count);
            Assert.True(stmt.Tags.ContainsKey("d"));
            Assert.True(stmt.Tags.ContainsKey("owner"));
            Assert.Equal("INSERT TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');", stmt.ToSql());
        }

        [Fact]
        public void ParseUpdateTag_TableAndColumn_WithMultipleTags()
        {
            var stmt = TestHelpers.Parse("UPDATE TAG FOR TABLE MyTable COLUMN Amount (d = 'Updated', owner = 'Finance');")
                .Statements.OfType<CreateTagStatement>().Single();

            Assert.NotNull(stmt.ColumnName);
            Assert.Equal(2, stmt.Tags.Count);
            Assert.Equal("INSERT TAG FOR TABLE MyTable COLUMN Amount (d = 'Updated', owner = 'Finance');", stmt.ToSql());
        }

        [Fact]
        public void ParseDeleteTag_TableAndColumn_WithMultipleTags()
        {
            var stmt = TestHelpers.Parse("DELETE TAG FOR TABLE MyTable COLUMN Amount (d, owner);")
                .Statements.OfType<DeleteTagStatement>().Single();

            Assert.NotNull(stmt.ColumnName);
            Assert.Equal(["d", "owner"], stmt.TagNames);
            Assert.Equal("DELETE TAG FOR TABLE MyTable COLUMN Amount (d, owner);", stmt.ToSql());
        }

        [Fact]
        public void ParseTag_RetiredSyntax_ReportsReplacement()
        {
            var script = TestHelpers.Parse(@"
                TAG #raw WITH (
                    source_system = 'SourceDB',
                    classification = 'confidential',
                    owner = 'finance_team',
                    load_pattern = 'incremental'
                );");

            Assert.Contains(script.Diagnostics, d => d.Message.Contains("TAG ... WITH has been retired"));
            Assert.Empty(script.Statements.OfType<CreateTagStatement>());
        }

        [Fact]
        public void ParseInsertTag_VariableNames_ParseAsExpressions()
        {
            var stmt = TestHelpers.Parse("INSERT TAG FOR TABLE @r.tbl COLUMN @r.col (d = @r.descr);")
                .Statements.OfType<CreateTagStatement>().Single();

            Assert.IsType<MemberAccessExpression>(stmt.TableName);
            Assert.IsType<MemberAccessExpression>(stmt.ColumnName);
            Assert.Single(stmt.Tags);
        }

        [Fact]
        public void ParseInsertTag_NoTags_ReportsDiagnostic()
        {
            // The parser collects syntax errors as diagnostics rather than throwing.
            var script = TestHelpers.Parse("INSERT TAG FOR TABLE MyTable ();");
            Assert.NotEmpty(script.Diagnostics);
            Assert.Empty(script.Statements.OfType<CreateTagStatement>());
        }

        [Fact]
        public void ParseCreateLineage_RetiredSyntax_ReportsReplacement()
        {
            var script = TestHelpers.Parse("CREATE LINEAGE FOR TABLE #final FROM 'lineage.json';");

            Assert.Contains(script.Diagnostics, d => d.Message.Contains("CREATE LINEAGE has been retired"));
            Assert.Empty(script.Statements.OfType<CreateLineageStatement>());
        }

        [Fact]
        public void ParseInsertLineage_FromStringLiteral()
        {
            var stmt = TestHelpers.Parse("INSERT LINEAGE FOR TABLE #final FROM 'lineage.json';")
                .Statements.OfType<CreateLineageStatement>().Single();

            Assert.NotNull(stmt.Source);
            Assert.NotNull(stmt.TableName);
            Assert.Equal("INSERT LINEAGE FOR TABLE #final FROM 'lineage.json';", stmt.ToSql());
        }

        [Fact]
        public void ParseDeleteLineage_ForTable()
        {
            var stmt = TestHelpers.Parse("DELETE LINEAGE FOR TABLE #final;")
                .Statements.OfType<DeleteLineageStatement>().Single();

            Assert.NotNull(stmt.TableName);
            Assert.Equal("DELETE LINEAGE FOR TABLE #final;", stmt.ToSql());
        }

        // ── TAG metadata: seeding + inheritance ─────────────────────────────

        [Fact]
        public async Task InsertTag_SeedsColumnMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval,
                "INSERT TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');");

            var meta = eval.LineageTracker.GetColumnMetadata("MyTable", "Amount");
            Assert.Equal("Sales amount", meta["d"]);
            Assert.Equal("Finance", meta["owner"]);
        }

        [Fact]
        public async Task UpdateTag_OverwritesExistingMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                INSERT TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');
                UPDATE TAG FOR TABLE MyTable COLUMN Amount (d = 'Net sales amount');");

            var meta = eval.LineageTracker.GetColumnMetadata("MyTable", "Amount");
            Assert.Equal("Net sales amount", meta["d"]);
            Assert.Equal("Finance", meta["owner"]);
        }

        [Fact]
        public async Task DeleteTag_RemovesSelectedMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                INSERT TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');
                DELETE TAG FOR TABLE MyTable COLUMN Amount (owner);");

            var meta = eval.LineageTracker.GetColumnMetadata("MyTable", "Amount");
            Assert.Equal("Sales amount", meta["d"]);
            Assert.False(meta.ContainsKey("owner"));
        }

        [Fact]
        public async Task DeleteTag_InLoop_RemovesRuntimeResolvedMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                CREATE TABLE #meta (tbl VARCHAR(20), col VARCHAR(20));
                INSERT INTO #meta VALUES ('T', 'Amount');
                INSERT TAG FOR TABLE T COLUMN Amount (owner = 'Finance', d = 'Sales amount');
                FOR @r IN (SELECT tbl, col FROM #meta)
                BEGIN
                    DELETE TAG FOR TABLE @r.tbl COLUMN @r.col (owner);
                END");

            var meta = eval.LineageTracker.GetColumnMetadata("T", "Amount");
            Assert.Equal("Sales amount", meta["d"]);
            Assert.False(meta.ContainsKey("owner"));
        }

        [Fact]
        public async Task InsertTag_SeedsTableMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                INSERT TAG FOR TABLE #raw (
                    source_system = 'SourceDB',
                    classification = 'confidential',
                    owner = 'finance_team',
                    load_pattern = 'incremental'
                );");

            var meta = eval.LineageTracker.GetTableMetadata("#raw");
            Assert.Equal("SourceDB", meta["source_system"]);
            Assert.Equal("confidential", meta["classification"]);
            Assert.Equal("finance_team", meta["owner"]);
            Assert.Equal("incremental", meta["load_pattern"]);
        }

        [Fact]
        public async Task InsertTag_BeforeSelectInto_InheritsDescriptionOntoDerivedColumn()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                CREATE TABLE #orders (Amount INT);
                INSERT INTO #orders VALUES (100);
                INSERT TAG FOR TABLE #orders COLUMN Amount (d = 'Sales amount');
                SELECT Amount AS total INTO #summary FROM #orders;");

            var meta = eval.LineageTracker.GetColumnMetadata("#summary", "total");
            Assert.Equal("Sales amount", meta["d"]);
        }

        [Fact]
        public async Task InsertTag_LastWriterWins()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                INSERT TAG FOR TABLE T (owner = 'alice');
                INSERT TAG FOR TABLE T (owner = 'bob');");

            Assert.Equal("bob", eval.LineageTracker.GetTableMetadata("T")["owner"]);
        }

        [Fact]
        public async Task InsertTag_InvalidStandardValue_Throws()
        {
            var eval = NewEval();
            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                TestHelpers.Execute(eval, "INSERT TAG FOR TABLE T (classification = 'secret');"));

            Assert.Contains("@classification", ex.Message);
        }

        [Fact]
        public async Task InsertTag_CustomOrganizationTag_IsAllowed()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, "INSERT TAG FOR TABLE T (org_retention_policy = 'finance-local');");

            Assert.Equal("finance-local", eval.LineageTracker.GetTableMetadata("T")["org_retention_policy"]);
        }

        [Fact]
        public async Task InsertTag_InLoop_SameLine_LastRowWins()
        {
            // Guards the ApplyTags fix: routed through Record, the second iteration's location-keyed
            // dedup would early-return and leave the inheritance dictionary at the first value.
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                CREATE TABLE #meta (tbl VARCHAR(20), val VARCHAR(20));
                INSERT INTO #meta VALUES ('T', 'first');
                INSERT INTO #meta VALUES ('T', 'second');
                FOR @r IN (SELECT tbl, val FROM #meta)
                BEGIN
                    INSERT TAG FOR TABLE @r.tbl (note = @r.val);
                END");

            Assert.Equal("second", eval.LineageTracker.GetTableMetadata("T")["note"]);
        }

        // ── LINEAGE import/delete: OpenLineage import ───────────────────────

        [Fact]
        public void OpenLineageImporter_RoundTripsColumnEdgesAndTags()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#result", new[] { "src.dbo.Orders" }, "SELECT",
                targetColumn: "total",
                sourceColumns: new[] { "amount" },
                metadata: new Dictionary<string, string> { ["pii"] = "false" },
                transformationKind: TransformationKind.Aggregation,
                transformationExpression: "SUM(amount)");

            var json = OpenLineageExporter.BuildRunEvent(tracker, "s1", "job");
            var entries = OpenLineageImporter.Import(json);

            var edge = entries.Single(e => e.TargetTable == "#result" && e.TargetColumn == "total");
            Assert.Contains("src.dbo.Orders", edge.SourceTables);
            Assert.Equal(TransformationKind.Aggregation, edge.TransformationKind);

            // Load into a fresh tracker; the tags facet (table-level) must be restored.
            var imported = new LineageTracker(NullLogger.Instance);
            imported.LoadState(entries);
            Assert.Equal("false", imported.GetTableMetadata("#result")["pii"]);
        }

        [Fact]
        public async Task InsertLineage_FromExportedFile_RestoresLineage()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var producer = NewEval();
                await TestHelpers.Execute(producer, $@"
                    CREATE TABLE #src (id INT, name VARCHAR(50));
                    INSERT INTO #src VALUES (1, 'Alice');
                    CREATE TABLE #dst (id INT, name VARCHAR(50));
                    INSERT INTO #dst SELECT id, name FROM #src;
                    SHOW LINEAGE EXPORT AS OPENLINEAGE TO '{tmp.Replace("\\", "\\\\")}';");

                var consumer = NewEval();
                await TestHelpers.Execute(consumer, $"INSERT LINEAGE FOR TABLE #dst FROM '{tmp.Replace("\\", "\\\\")}';");

                Assert.Contains(consumer.LineageTracker.GetFullLineage(), e => e.TargetTable == "#dst");
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public async Task DeleteLineage_RemovesImportedRowsAndPreservesCapturedRows()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var producer = NewEval();
                await TestHelpers.Execute(producer, $@"
                    CREATE TABLE #src (id INT, name VARCHAR(50));
                    INSERT INTO #src VALUES (1, 'Alice');
                    CREATE TABLE #dst (id INT, name VARCHAR(50));
                    INSERT INTO #dst SELECT id, name FROM #src;
                    INSERT TAG FOR TABLE #dst (owner = 'ImportedCatalog');
                    SHOW LINEAGE EXPORT AS OPENLINEAGE TO '{tmp.Replace("\\", "\\\\")}';");

                var consumer = NewEval();
                await TestHelpers.Execute(consumer, $@"
                    CREATE TABLE #local (id INT);
                    INSERT INTO #local VALUES (1);
                    SELECT id INTO #dst FROM #local;
                    INSERT LINEAGE FOR TABLE #dst FROM '{tmp.Replace("\\", "\\\\")}';
                    DELETE LINEAGE FOR TABLE #dst;");

                var targetEntries = consumer.LineageTracker.GetFullLineage()
                    .Where(e => e.TargetTable.Equals("#dst", System.StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Assert.DoesNotContain(targetEntries, e => e.Operation.Equals("IMPORTED", System.StringComparison.OrdinalIgnoreCase));
                Assert.Contains(targetEntries, e => !e.Operation.Equals("IMPORTED", System.StringComparison.OrdinalIgnoreCase));
                Assert.False(consumer.LineageTracker.GetTableMetadata("#dst").ContainsKey("owner"));
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public async Task InsertLineage_MissingFile_ThrowsExecutionException()
        {
            var eval = NewEval();
            await Assert.ThrowsAnyAsync<System.Exception>(() =>
                TestHelpers.Execute(eval, "INSERT LINEAGE FOR TABLE #x FROM 'does-not-exist-12345.json';"));
        }
    }
}
