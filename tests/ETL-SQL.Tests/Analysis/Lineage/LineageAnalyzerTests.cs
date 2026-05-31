using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Analysis.Lineage
{
    public class LineageAnalyzerTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        [Fact]
        public void Analyze_SimpleSelectInto_RecordsLineage()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            var script = Parse("SELECT col1 /* @d: Source column; */ INTO #Target FROM SourceTable /* @owner: TeamA; */;");

            // Act
            analyzer.Analyze(script);

            // Assert
            var entries = tracker.GetFullLineage().ToList();
            
            // Should have 2 entries: 1 for Table Tags, 1 for SELECT
            Assert.Contains(entries, e => e.Operation == "TABLE_TAGS" && e.TargetTable == "SourceTable" && e.Metadata.ContainsKey("owner"));
            Assert.Contains(entries, e => e.Operation == "SELECT" && e.TargetTable == "#Target" && e.TargetColumn == "col1");
            
            var colEntry = entries.First(e => e.TargetColumn == "col1");
            Assert.Equal("SourceTable", colEntry.SourceTables.First());
            Assert.Equal("Source column", colEntry.Metadata["d"]);
        }

        [Fact]
        public void Analyze_CreateDataset_KeysColumnLineageToDatasetName()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            var script = Parse(
                "CREATE DATASET &sales_snap AS (SELECT SUM(Amount) AS total /* @d: Sales amounts; @pii: true; */ FROM Sales);");

            // Act
            analyzer.Analyze(script);

            // Assert — the dataset's column lineage is keyed to the dataset target,
            // not the ambiguous "RESULTSET", so it persists and resolves by name.
            var entries = tracker.GetFullLineage().ToList();
            Assert.DoesNotContain(entries, e => e.TargetTable == "RESULTSET");

            var total = Assert.Single(entries, e => e.TargetColumn == "total");
            Assert.Equal("dataset:&sales_snap", total.TargetTable);
            Assert.Contains("Sales", total.SourceTables);
            Assert.Contains("Amount", total.SourceColumns);
            Assert.Equal(TransformationKind.Aggregation, total.TransformationKind);
            Assert.Equal("SUM(Amount)", total.TransformationExpression);
            Assert.Equal("Sales amounts", total.Metadata["d"]);
            Assert.Equal("true", total.Metadata["pii"]);
        }

        [Fact]
        public void Analyze_InheritsDbColumnDescriptionOntoDerivedColumn()
        {
            // Arrange — simulate the DB catalog import recording a source column's
            // native comment as the lineage description ("d") + a pii tag.
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("edw.Sales", System.Array.Empty<string>(), "DB_CATALOG",
                targetColumn: "Amount",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["d"]   = "Sales amounts",
                    ["pii"] = "true",
                });

            // Act — derive total = SUM(Amount) from that source, using the same tracker.
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("CREATE DATASET &snap AS (SELECT SUM(Amount) AS total FROM edw.Sales);"));

            // Assert — the DB comment + pii flowed onto the derived column.
            var total = Assert.Single(tracker.GetFullLineage(), e => e.TargetColumn == "total");
            Assert.Contains("Sales amounts", total.DerivedFromDescriptions ?? total.Metadata.GetValueOrDefault("d") ?? "");
            Assert.Equal("true", total.Metadata.GetValueOrDefault("pii"));
        }

        [Fact]
        public void Analyze_JoinWithAliases_ResolvesSourceTables()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            var sql = @"
                SELECT 
                    a.ID /* @d: UID; */, 
                    b.Name /* @d: Name; */
                INTO #Combined
                FROM TableA AS a
                INNER JOIN TableB AS b ON a.ID = b.A_ID;";
            var script = Parse(sql);

            // Act
            analyzer.Analyze(script);

            // Assert
            var entries = tracker.GetColumnLineage("#Combined", "ID").ToList();
            Assert.Single(entries);
            Assert.Equal("TableA", entries[0].SourceTables.First());
            Assert.Equal("UID", entries[0].Metadata["d"]);

            var nameEntries = tracker.GetColumnLineage("#Combined", "Name").ToList();
            Assert.Single(nameEntries);
            Assert.Equal("TableB", nameEntries[0].SourceTables.First());
        }

        [Fact]
        public void Analyze_ExpressionLineage_InheritsMetadata()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            // Pre-seed tracker with source metadata
            tracker.Record("TableA", Enumerable.Empty<string>(), "SEED", "Col1", metadata: new Dictionary<string, string> { ["d"] = "Description1", ["sensitive"] = "true" });
            
            var analyzer = new LineageAnalyzer(tracker);
            var script = Parse("SELECT Col1 + 100 AS Computed INTO #Target FROM TableA;");

            // Act
            analyzer.Analyze(script);

            // Assert
            var entry = tracker.GetColumnLineage("#Target", "Computed").First();
            Assert.Equal("Description1", entry.Metadata["d"]);
            Assert.Equal("true", entry.Metadata["sensitive"]);
            Assert.Contains("Col1: Description1", entry.DerivedFromDescriptions);
        }

        [Fact]
        public void Analyze_MultipleStatements_AccumulatesLineage()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            var sql = @"
                SELECT id, name INTO #Temp1 FROM SourceTable;
                SELECT name AS fullName INTO #Final FROM #Temp1;
            ";
            var script = Parse(sql);

            // Verify parse success
            Assert.Empty(script.Diagnostics.Select(d => d.Message));
            Assert.Equal(2, script.Statements.Count);

            // Act
            analyzer.Analyze(script);

            // Assert
            var allEntries = tracker.GetFullLineage().ToList();
            var tempEntries = tracker.GetLineage("#Temp1").ToList();
            
            // Helpful failure message if empty
            if (!tempEntries.Any())
            {
                var targets = allEntries.Select(e => e.TargetTable).Distinct();
                throw new Exception($"#Temp1 lineage was empty. Found targets: {string.Join(", ", targets)}. Source: {sql}");
            }

            Assert.NotEmpty(tempEntries);

            var finalEntries = tracker.GetLineage("#Final").ToList();
            Assert.Contains(finalEntries, e => e.TargetColumn == "fullName" && e.SourceTables.Contains("#Temp1", StringComparer.OrdinalIgnoreCase));
        }
        [Fact]
        public void Analyze_UpdateStatement_RecordsColumnLineage()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("SourceTbl", Enumerable.Empty<string>(), "SEED", "Price", metadata: new Dictionary<string, string> { ["d"] = "Unit Price" });
            
            var analyzer = new LineageAnalyzer(tracker);
            var sql = "UPDATE #Target SET Price = s.Price * 1.05 FROM SourceTbl AS s;";
            var script = Parse(sql);

            // Act
            analyzer.Analyze(script);

            // Assert
            var updateEntries = tracker.GetColumnLineage("#Target", "Price").ToList();
            Assert.NotEmpty(updateEntries);
            var priceUpdate = updateEntries.First(e => e.Operation == "UPDATE COLUMN");
            Assert.Contains("SourceTbl", priceUpdate.SourceTables);
            Assert.Equal("Unit Price", priceUpdate.Metadata["d"]);
            Assert.Contains("Price: Unit Price", priceUpdate.DerivedFromDescriptions);
        }

        [Fact]
        public void Analyze_MergeStatement_RecordsColumnLineage()
        {
            // Arrange
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("S", Enumerable.Empty<string>(), "SEED", "X", metadata: new Dictionary<string, string> { ["d"] = "Val X" });
            
            var analyzer = new LineageAnalyzer(tracker);
            var sql = @"
                MERGE INTO #T AS T
                USING #S AS S ON T.ID = S.ID
                WHEN MATCHED THEN UPDATE SET X = S.X
                WHEN NOT MATCHED THEN INSERT (X) VALUES (S.X);
            ";
            var script = Parse(sql);

            // Act
            analyzer.Analyze(script);

            // Assert
            var entries = tracker.GetFullLineage().ToList();
            
            var updateEntry = entries.First(e => e.Operation == "MERGE UPDATE" && e.TargetColumn == "X");
            Assert.Equal("Val X", updateEntry.Metadata["d"]);

            var insertEntry = entries.First(e => e.Operation == "MERGE INSERT" && e.TargetColumn == "X");
            Assert.Equal("Val X", insertEntry.Metadata["d"]);
        }

        [Fact]
        public void ClassifyExpression_PassThrough_ForSimpleColumnRef()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT col1 INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "col1").First();
            Assert.Equal(TransformationKind.PassThrough, entry.TransformationKind);
            Assert.Null(entry.TransformationExpression);
        }

        [Fact]
        public void ClassifyExpression_Aggregation_ForSumCount()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT SUM(amount) AS total, COUNT(*) AS cnt INTO #T FROM Src GROUP BY id;"));

            var totalEntry = tracker.GetColumnLineage("#T", "total").First();
            Assert.Equal(TransformationKind.Aggregation, totalEntry.TransformationKind);
            Assert.Contains("SUM", totalEntry.FunctionsApplied!);

            var cntEntry = tracker.GetColumnLineage("#T", "cnt").First();
            Assert.Equal(TransformationKind.Aggregation, cntEntry.TransformationKind);
            Assert.Contains("COUNT", cntEntry.FunctionsApplied!);
        }

        [Fact]
        public void ClassifyExpression_CaseExpression_ForCaseWhen()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT CASE WHEN amount > 0 THEN 'Y' ELSE 'N' END AS flag INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "flag").First();
            Assert.Equal(TransformationKind.CaseExpression, entry.TransformationKind);
            Assert.NotNull(entry.TransformationExpression);
        }

        [Fact]
        public void ClassifyExpression_Arithmetic_ForBinaryOp()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT price * qty AS line_total INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "line_total").First();
            Assert.Equal(TransformationKind.Arithmetic, entry.TransformationKind);
            Assert.NotNull(entry.TransformationExpression);
        }

        [Fact]
        public void ClassifyExpression_Literal_ForConstant()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT 42 AS answer INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "answer").First();
            Assert.Equal(TransformationKind.Literal, entry.TransformationKind);
        }

        [Fact]
        public void ClassifyExpression_FunctionCall_ForScalarFunction()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT UPPER(name) AS uname INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "uname").First();
            Assert.Equal(TransformationKind.FunctionCall, entry.TransformationKind);
            Assert.Contains("UPPER", entry.FunctionsApplied!);
        }

        [Fact]
        public void ClassifyExpression_Cast_ForCastExpression()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT CAST(amount AS DECIMAL) AS amt INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "amt").First();
            Assert.Equal(TransformationKind.Cast, entry.TransformationKind);
            Assert.Contains("CAST", entry.FunctionsApplied!);
        }

        [Fact]
        public void ClassifyExpression_WindowFunction_ForRowNumber()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT ROW_NUMBER() OVER (PARTITION BY dept ORDER BY salary DESC) AS rn INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "rn").First();
            Assert.Equal(TransformationKind.WindowFunction, entry.TransformationKind);
            Assert.Contains("ROW_NUMBER", entry.FunctionsApplied!);
        }

        [Fact]
        public void ClassifyExpression_Conditional_ForCoalesce()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT COALESCE(preferred_name, first_name) AS display_name INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "display_name").First();
            Assert.Equal(TransformationKind.Conditional, entry.TransformationKind);
            Assert.Contains("COALESCE", entry.FunctionsApplied!);
        }

        [Fact]
        public void PiiInheritance_TrueWins_FromSourceColumn()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("Src", Enumerable.Empty<string>(), "SEED", "email", metadata: new Dictionary<string, string> { ["pii"] = "true" });

            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("SELECT UPPER(email) AS email_upper INTO #T FROM Src;"));

            var entry = tracker.GetColumnLineage("#T", "email_upper").First();
            Assert.Equal("true", entry.Metadata["pii"]);
        }

        // ── Phase 4 — Report Lineage ─────────────────────────────────────────

        [Fact]
        public void Analyze_CreateDataset_RecordsDatasetLineage()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("CREATE DATASET &sales AS (SELECT amount FROM orders);"));

            var entries = tracker.GetFullLineage().ToList();
            var datasetEntry = entries.FirstOrDefault(e => e.Operation == "CREATE DATASET");

            Assert.NotNull(datasetEntry);
            Assert.Equal("dataset:&sales", datasetEntry!.TargetTable);
            Assert.Contains("orders", datasetEntry.SourceTables, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Analyze_CreateVisual_WithTempTableSource_RecordsVisualLineage()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("CREATE VISUAL SalesChart AS BAR (SOURCE = #sales, MAPPINGS (x = month, y = amount));"));

            var entries = tracker.GetFullLineage().ToList();
            var visualEntry = entries.FirstOrDefault(e => e.Operation == "CREATE VISUAL");

            Assert.NotNull(visualEntry);
            Assert.Equal("report:SalesChart", visualEntry!.TargetTable);
            Assert.Contains("#sales", visualEntry.SourceTables, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Analyze_CreateVisual_WithInlineSelect_RecordsVisualAndQueryLineage()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            analyzer.Analyze(Parse("CREATE VISUAL RegionChart AS PIE (SOURCE = (SELECT region, revenue FROM #orders), MAPPINGS (label = region, value = revenue));"));

            var entries = tracker.GetFullLineage().ToList();
            var visualEntry = entries.FirstOrDefault(e => e.Operation == "CREATE VISUAL");

            Assert.NotNull(visualEntry);
            Assert.Equal("report:RegionChart", visualEntry!.TargetTable);
            Assert.Contains("#orders", visualEntry.SourceTables, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Analyze_EndToEndReportChain_VisibleInFullLineage()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var analyzer = new LineageAnalyzer(tracker);
            var sql = @"
SELECT amount INTO #orders FROM CRM.dbo.Orders;
CREATE DATASET &daily_sales AS (SELECT amount FROM #orders);
CREATE VISUAL SalesChart AS BAR (SOURCE = &daily_sales, MAPPINGS (x = day, y = amount));
";
            analyzer.Analyze(Parse(sql));

            var all = tracker.GetFullLineage().ToList();

            // Source table flows to #orders
            Assert.Contains(all, e => e.TargetTable == "#orders" && e.SourceTables.Contains("CRM.dbo.Orders", StringComparer.OrdinalIgnoreCase));
            // Dataset links #orders as source
            Assert.Contains(all, e => e.Operation == "CREATE DATASET" && e.TargetTable == "dataset:&daily_sales" && e.SourceTables.Contains("#orders", StringComparer.OrdinalIgnoreCase));
            // Visual links dataset as source
            Assert.Contains(all, e => e.Operation == "CREATE VISUAL" && e.TargetTable == "report:SalesChart" && e.SourceTables.Contains("&daily_sales", StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void Render_ReportNode_UsesVisualLabel()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("report:SalesChart", new[] { "#sales" }, "CREATE VISUAL");

            var renderer = new LineageGraphRenderer();
            var output = renderer.Render(tracker);

            Assert.Contains("[Visual: SalesChart]", output);
            Assert.DoesNotContain("[Table: report:SalesChart]", output);
        }

        [Fact]
        public void Render_DatasetNode_UsesDatasetLabel()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("dataset:&sales", new[] { "#orders" }, "CREATE DATASET");

            var renderer = new LineageGraphRenderer();
            var output = renderer.Render(tracker);

            Assert.Contains("[Dataset: &sales]", output);
            Assert.DoesNotContain("[Table: dataset:&sales]", output);
        }

        [Fact]
        public void RenderMermaid_ReportNode_UsesRoundedShape()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("report:SalesChart", new[] { "#orders" }, "CREATE VISUAL");

            var renderer = new LineageGraphRenderer();
            var mermaid = renderer.RenderMermaid(tracker);

            // report: node should use rounded parentheses, not square brackets
            Assert.Contains("(\"report:SalesChart\")", mermaid);
            Assert.DoesNotContain("[\"report:SalesChart\"]", mermaid);
        }

        [Fact]
        public void RenderMermaid_DatasetNode_UsesCylinderShape()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("dataset:&sales", new[] { "#orders" }, "CREATE DATASET");

            var renderer = new LineageGraphRenderer();
            var mermaid = renderer.RenderMermaid(tracker);

            // dataset: node should use cylinder shape
            Assert.Contains("[(\"dataset:&sales\")]", mermaid);
            Assert.DoesNotContain("[\"dataset:&sales\"]", mermaid);
        }
    }
}
