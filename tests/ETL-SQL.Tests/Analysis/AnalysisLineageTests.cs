using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


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
                
                -- Verify eng.lineage output doesn't throw and contains info
                SELECT * FROM eng.lineage;
            ";

            await _evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            // 3. Assert
            var entries = _evaluator.LineageTracker.GetLineage("#Target").ToList();
            // The statement-level INSERT (no target column) carries both branches of the UNION;
            // the per-column entries alongside it each carry only their own source.
            var insertEntry = Assert.Single(entries, e => e.Operation == "INSERT" && e.TargetColumn == null);
            Assert.Contains("#SourceA", insertEntry.SourceTables);
            Assert.Contains("#SourceB", insertEntry.SourceTables);
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
                    SELECT * FROM eng.lineage;
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
                
                SELECT * FROM eng.lineage;
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

        [Fact]
        public async Task TestLineageMultiStepFlowWithTransformation()
        {
            var services1 = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator1 = services1.GetRequiredService<Evaluator>();

            // 1st flow: MOCKDB -> FLATFILE (2-step)
            string script1 = @"
                CREATE CONNECTION src AS MOCKDB(SERVER='src_serv', DATABASE='src_db');
                CREATE CONNECTION dest AS FLATFILE(PATH='C:\tmp\dest.csv');
                INSERT INTO dest.FILE (id, name)
                SELECT id, name FROM src.Users;
            ";
            await evaluator1.Evaluate(new Parser(new Lexer(script1).Tokenize()).Parse());

            var lineage1 = evaluator1.LineageTracker.GetColumnLineage("dest.FILE", "id").ToList();
            Assert.NotEmpty(lineage1);
            var entry1 = lineage1.First();
            Assert.Contains("src.Users", entry1.SourceTables);
            Assert.Contains("id", entry1.SourceColumns);

            var services2 = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator2 = services2.GetRequiredService<Evaluator>();

            // 2nd flow: MOCKDB -> #intermediate -> FLATFILE (3-step)
            string script2 = @"
                CREATE CONNECTION src AS MOCKDB(SERVER='src_serv', DATABASE='src_db');
                CREATE CONNECTION dest AS FLATFILE(PATH='C:\tmp\dest.csv');
                CREATE TABLE #intermediate (id INT, name VARCHAR);
                
                INSERT INTO #intermediate (id, name)
                SELECT CAST(id AS INT) AS id, name FROM src.Users;
                
                INSERT INTO dest.FILE (id, name)
                SELECT id, name FROM #intermediate;
            ";
            await evaluator2.Evaluate(new Parser(new Lexer(script2).Tokenize()).Parse());

            // Get the complete ancestor lineage for the target column
            var ancestors = evaluator2.LineageTracker.GetAncestors("dest.FILE", "id").ToList();

            // Should contain:
            // 1. dest.FILE (target of second insert, from #intermediate)
            // 2. #intermediate (target of first insert, from src.Users, with CAST)
            Assert.True(ancestors.Count >= 2, $"Expected at least 2 ancestor lineage entries, got {ancestors.Count}");

            // The intermediate step should record CAST transformation
            var intermediateEntry = ancestors.FirstOrDefault(e => e.TargetTable.Equals("#intermediate", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(intermediateEntry);
            Assert.Equal(TransformationKind.Cast, intermediateEntry.TransformationKind);
            Assert.Contains("CAST(id, 'INT')", intermediateEntry.TransformationExpression);
        }
    }
}
