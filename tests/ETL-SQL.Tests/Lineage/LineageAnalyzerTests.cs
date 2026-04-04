using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Lineage
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
            var tracker = new LineageTracker();
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
        public void Analyze_JoinWithAliases_ResolvesSourceTables()
        {
            // Arrange
            var tracker = new LineageTracker();
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
            var tracker = new LineageTracker();
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
            var tracker = new LineageTracker();
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
            var tracker = new LineageTracker();
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
            var tracker = new LineageTracker();
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
    }
}
