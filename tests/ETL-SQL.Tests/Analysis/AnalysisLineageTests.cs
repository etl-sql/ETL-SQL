using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;


namespace ETL_SQL.Tests.Analysis
{
    public class LineageTests
    {
        private readonly Evaluator _evaluator;
        private readonly ServiceProvider _serviceProvider;

        public LineageTests()
        {
            _serviceProvider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();
        }

        [Fact]
        public async Task TestLineageTracking()
        {
            // 1. Setup Data
            await _evaluator.Evaluate(new Parser(new Lexer("CREATE TABLE #SourceA (ID INT, Name VARCHAR(50));").Tokenize()).Parse());
            await _evaluator.Evaluate(new Parser(new Lexer("INSERT INTO #SourceA VALUES (1, 'Alice');").Tokenize()).Parse());
            
            await _evaluator.Evaluate(new Parser(new Lexer("CREATE TABLE #SourceB (ID INT, Name VARCHAR(50));").Tokenize()).Parse());
            await _evaluator.Evaluate(new Parser(new Lexer("INSERT INTO #SourceB VALUES (2, 'Bob');").Tokenize()).Parse());

            // 2. Perform Movement (INSERT INTO ... SELECT)
            string script = @"
                CREATE TABLE #Target (ID INT, Name VARCHAR(50));
                
                INSERT INTO #Target
                SELECT S1.ID, S1.Name FROM #SourceA S1
                UNION ALL
                SELECT S2.ID, S2.Name FROM #SourceB S2;
                
                -- Verify LINEAGE output doesn't throw and contains info
                LINEAGE #Target;
            ";

            await _evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            // 3. Assert
            var entries = _evaluator.LineageTracker.GetLineage("#Target").ToList();
            Assert.Single(entries);
            Assert.Equal("INSERT", entries[0].Operation);
            Assert.Contains("#SourceA", entries[0].SourceTables);
            Assert.Contains("#SourceB", entries[0].SourceTables);
        }

        [Fact]
        public async Task TestBulkInsertLineage()
        {
            string csvPath = Path.GetTempFileName() + ".csv";
            File.WriteAllLines(csvPath, new[] { "ID,Name", "1,Alice" });

            try
            {
                string script = $@"
                    CREATE TABLE #BulkTarget (ID INT, Name VARCHAR(50));
                    BULK INSERT #BulkTarget FROM '{csvPath.Replace("\\", "/")}'
                    WITH (FIELDTERMINATOR = ',', FIRSTROW = 2);
                    LINEAGE #BulkTarget;
                ";

                await _evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

                var entries = _evaluator.LineageTracker.GetLineage("#BulkTarget").ToList();
                Assert.Single(entries);
                Assert.Equal("BULK INSERT", entries[0].Operation);
                Assert.Contains(csvPath.Replace("\\", "/"), entries[0].SourceTables);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task TestColumnLevelLineage()
        {
            await _evaluator.Evaluate(new Parser(new Lexer("CREATE TABLE #Source (ID INT /*@d: The unique identifier; */, Name VARCHAR(50));").Tokenize()).Parse());
            await _evaluator.Evaluate(new Parser(new Lexer("INSERT INTO #Source VALUES (1, 'Alice');").Tokenize()).Parse());

            string script = @"
                CREATE TABLE #Target (TargetID INT, TargetName VARCHAR(50));
                
                INSERT INTO #Target (TargetID, TargetName)
                SELECT ID /*@d: Mapping ID to TargetID; */, Name FROM #Source;
                
                LINEAGE #Target;
            ";

            await _evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            var entries = _evaluator.LineageTracker.GetLineage("#Target").ToList();
            
            // We record for each column in InsertStatementHandler when SELECT is used
            Assert.InRange(entries.Count, 2, 10); 
            
            var idEntry = entries.FirstOrDefault(e => e.TargetColumn == "TargetID");
            Assert.NotNull(idEntry);
            Assert.Contains("ID", idEntry.SourceColumns);
            Assert.Contains("Mapping ID to TargetID", idEntry.Description);
            
            var nameEntry = entries.FirstOrDefault(e => e.TargetColumn == "TargetName");
            Assert.NotNull(nameEntry);
            Assert.Contains("Name", nameEntry.SourceColumns);
        }

        [Fact]
        public async Task TestVisualLineageGraph()
        {
            await _evaluator.Evaluate(new Parser(new Lexer("CREATE TABLE #Source1 (ID INT, Name VARCHAR(50));").Tokenize()).Parse());
            await _evaluator.Evaluate(new Parser(new Lexer("CREATE TABLE #Source2 (ID INT, Price DECIMAL);").Tokenize()).Parse());
            
            string script = @"
                CREATE TABLE #Intermediate (ID INT, Info VARCHAR(50));
                INSERT INTO #Intermediate (ID, Info)
                SELECT ID, Name FROM #Source1;

                CREATE TABLE #Final (ID INT, DetailedInfo VARCHAR(100));
                INSERT INTO #Final (ID, DetailedInfo)
                SELECT I.ID, I.Info + ' - ' + CAST(S2.Price AS VARCHAR)
                FROM #Intermediate I
                JOIN #Source2 S2 ON I.ID = S2.ID;
            ";

            await _evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            var renderer = new LineageGraphRenderer();
            string graph = renderer.Render(_evaluator.LineageTracker, "#Final");

            // Verify structure
            string errorMsg = $"Graph was:\n{graph}\n\nAll Entries:\n" + string.Join("\n", _evaluator.LineageTracker.GetFullLineage().Select(e => e.ToString()));
            
            Assert.True(graph.Contains("[Table: #Final]"), errorMsg);
            Assert.True(graph.Contains("DetailedInfo"), errorMsg);
            Assert.True(graph.Contains("#Intermediate.Info"), errorMsg);
            Assert.True(graph.Contains("#Source1.Name"), errorMsg);
            Assert.True(graph.Contains("#Source2.Price"), errorMsg);
            
            // Output for manual inspection
            Console.WriteLine(graph);
        }
    }
}
