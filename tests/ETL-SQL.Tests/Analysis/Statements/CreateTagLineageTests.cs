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
    /// Covers CREATE TAG (explicit tag seeding + inheritance) and CREATE LINEAGE ... FROM
    /// (OpenLineage import), including the last-writer-wins semantics and the loop/same-line
    /// re-tagging case that motivated LineageTracker.ApplyTags.
    /// </summary>
    public class CreateTagLineageTests
    {
        private static Evaluator NewEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ── Parser ──────────────────────────────────────────────────────────

        [Fact]
        public void ParseCreateTag_TableAndColumn_WithMultipleTags()
        {
            var stmt = TestHelpers.Parse("CREATE TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');")
                .Statements.OfType<CreateTagStatement>().Single();

            Assert.NotNull(stmt.ColumnName);
            Assert.Equal(2, stmt.Tags.Count);
            Assert.True(stmt.Tags.ContainsKey("d"));
            Assert.True(stmt.Tags.ContainsKey("owner"));
        }

        [Fact]
        public void ParseTag_LegacyTableSyntax_WithMultipleTags()
        {
            var stmt = TestHelpers.Parse(@"
                TAG #raw WITH (
                    source_system = 'SourceDB',
                    classification = 'confidential',
                    owner = 'finance_team',
                    load_pattern = 'incremental'
                );")
                .Statements.OfType<CreateTagStatement>().Single();

            Assert.Null(stmt.ColumnName);
            Assert.Equal(4, stmt.Tags.Count);
            Assert.True(stmt.Tags.ContainsKey("source_system"));
            Assert.True(stmt.Tags.ContainsKey("classification"));
            Assert.True(stmt.Tags.ContainsKey("owner"));
            Assert.True(stmt.Tags.ContainsKey("load_pattern"));
        }

        [Fact]
        public void ParseCreateTag_VariableNames_ParseAsExpressions()
        {
            var stmt = TestHelpers.Parse("CREATE TAG FOR TABLE @r.tbl COLUMN @r.col (d = @r.descr);")
                .Statements.OfType<CreateTagStatement>().Single();

            Assert.IsType<MemberAccessExpression>(stmt.TableName);
            Assert.IsType<MemberAccessExpression>(stmt.ColumnName);
            Assert.Single(stmt.Tags);
        }

        [Fact]
        public void ParseCreateTag_NoTags_ReportsDiagnostic()
        {
            // The parser collects syntax errors as diagnostics rather than throwing.
            var script = TestHelpers.Parse("CREATE TAG FOR TABLE MyTable ();");
            Assert.NotEmpty(script.Diagnostics);
            Assert.Empty(script.Statements.OfType<CreateTagStatement>());
        }

        [Fact]
        public void ParseCreateLineage_FromStringLiteral()
        {
            var stmt = TestHelpers.Parse("CREATE LINEAGE FOR TABLE #final FROM 'lineage.json';")
                .Statements.OfType<CreateLineageStatement>().Single();

            Assert.NotNull(stmt.Source);
            Assert.NotNull(stmt.TableName);
        }

        // ── CREATE TAG: seeding + inheritance ───────────────────────────────

        [Fact]
        public async Task CreateTag_SeedsColumnMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval,
                "CREATE TAG FOR TABLE MyTable COLUMN Amount (d = 'Sales amount', owner = 'Finance');");

            var meta = eval.LineageTracker.GetColumnMetadata("MyTable", "Amount");
            Assert.Equal("Sales amount", meta["d"]);
            Assert.Equal("Finance", meta["owner"]);
        }

        [Fact]
        public async Task Tag_SeedsTableMetadata()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                TAG #raw WITH (
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
        public async Task CreateTag_BeforeSelectInto_InheritsDescriptionOntoDerivedColumn()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                CREATE TABLE #orders (Amount INT);
                INSERT INTO #orders VALUES (100);
                CREATE TAG FOR TABLE #orders COLUMN Amount (d = 'Sales amount');
                SELECT Amount AS total INTO #summary FROM #orders;");

            var meta = eval.LineageTracker.GetColumnMetadata("#summary", "total");
            Assert.Equal("Sales amount", meta["d"]);
        }

        [Fact]
        public async Task CreateTag_LastWriterWins()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, @"
                CREATE TAG FOR TABLE T (owner = 'alice');
                CREATE TAG FOR TABLE T (owner = 'bob');");

            Assert.Equal("bob", eval.LineageTracker.GetTableMetadata("T")["owner"]);
        }

        [Fact]
        public async Task CreateTag_InvalidStandardValue_Throws()
        {
            var eval = NewEval();
            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                TestHelpers.Execute(eval, "CREATE TAG FOR TABLE T (classification = 'secret');"));

            Assert.Contains("@classification", ex.Message);
        }

        [Fact]
        public async Task CreateTag_CustomOrganizationTag_IsAllowed()
        {
            var eval = NewEval();
            await TestHelpers.Execute(eval, "CREATE TAG FOR TABLE T (org_retention_policy = 'finance-local');");

            Assert.Equal("finance-local", eval.LineageTracker.GetTableMetadata("T")["org_retention_policy"]);
        }

        [Fact]
        public async Task CreateTag_InLoop_SameLine_LastRowWins()
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
                    CREATE TAG FOR TABLE @r.tbl (note = @r.val);
                END");

            Assert.Equal("second", eval.LineageTracker.GetTableMetadata("T")["note"]);
        }

        // ── CREATE LINEAGE: OpenLineage import ──────────────────────────────

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
        public async Task CreateLineage_FromExportedFile_RestoresLineage()
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
                await TestHelpers.Execute(consumer, $"CREATE LINEAGE FOR TABLE #dst FROM '{tmp.Replace("\\", "\\\\")}';");

                Assert.Contains(consumer.LineageTracker.GetFullLineage(), e => e.TargetTable == "#dst");
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        [Fact]
        public async Task CreateLineage_MissingFile_ThrowsExecutionException()
        {
            var eval = NewEval();
            await Assert.ThrowsAnyAsync<System.Exception>(() =>
                TestHelpers.Execute(eval, "CREATE LINEAGE FOR TABLE #x FROM 'does-not-exist-12345.json';"));
        }
    }
}
