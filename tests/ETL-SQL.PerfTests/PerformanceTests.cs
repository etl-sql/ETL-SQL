using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests
{
    [Trait("Category", "Performance")]
    public class PerformanceTests
    {
        // Graduated #temp insert benchmark: measures rows/sec and memory at each 10k checkpoint.
        // Uses direct AST inserts (no SQL parse overhead in the hot loop).
        // Detects O(n²) DataTable scan degradation: last chunk should not be >5x slower than first.
        // Spill-to-disk is live for query engines (sort/join/agg) but not raw #temp accumulation.
        [Fact(Timeout = 120_000)] // 2-minute hard cap — hang == fail
        public async Task TestLargeDatasetScaling()
        {
            const int chunkSize = 10_000;
            const int maxChunks = 10;   // 100k rows; extend when spill-to-disk ships

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.BatchSize = chunkSize;

            await Execute(eval, "CREATE TABLE #scaling (ID INT, Val VARCHAR(20));");

            var process = Process.GetCurrentProcess();
            var chunkTimes = new List<double>();
            int totalRows = 0;

            Console.WriteLine($"{"Rows",10}  {"Rows/sec",10}  {"Chunk ms",10}  {"WorkSet MB",12}");
            Console.WriteLine(new string('-', 50));

            for (int chunk = 0; chunk < maxChunks; chunk++)
            {
                // Build the chunk as an AST InsertStatement — no SQL parsing in the hot loop.
                var rowValues = new List<List<Expression>>(chunkSize);
                for (int r = 0; r < chunkSize; r++)
                {
                    int id = totalRows + r + 1;
                    rowValues.Add(new List<Expression>
                    {
                        new LiteralExpression(id,            TokenType.NUMBER),
                        new LiteralExpression($"V{id}", TokenType.STRING)
                    });
                }

                var insertStmt = new InsertStatement(
                    new TableReference("#scaling"),
                    new List<string> { "ID", "Val" },
                    rowValues);
                var script = new Script();
                script.Statements.Add(insertStmt);

                var sw = Stopwatch.StartNew();
                await eval.Evaluate(script);
                sw.Stop();

                totalRows += chunkSize;
                chunkTimes.Add(sw.Elapsed.TotalMilliseconds);

                process.Refresh();
                long memMB = process.WorkingSet64 / 1024 / 1024;
                double rps = chunkSize / sw.Elapsed.TotalSeconds;

                Console.WriteLine($"{totalRows,10:N0}  {rps,10:N0}  {sw.Elapsed.TotalMilliseconds,10:N0}  {memMB,10} MB");
            }

            // Degradation check: last chunk should be < 5x the first chunk's time.
            // A ratio above that indicates O(n²) table growth — a bug worth fixing.
            double firstMs = chunkTimes[0];
            double lastMs = chunkTimes[^1];
            double ratio = lastMs / firstMs;
            Console.WriteLine($"\nDegradation ratio (last/first chunk): {ratio:F2}x");
            Assert.True(ratio < 5.0,
                $"Insert performance degraded {ratio:F1}x from first to last chunk " +
                $"({firstMs:N0}ms → {lastMs:N0}ms). " +
                "Likely O(n²) growth in DataTable scan or identifier resolution. " +
                "See Docs/Strategy/LargeDatasets.md for the spill-to-disk fix.");

            // Streaming SELECT: measure whether ExecuteQuery streams or materializes.
            GC.Collect();
            process.Refresh();
            long mbBefore = process.WorkingSet64 / 1024 / 1024;

            int selectCount = 0;
            var selectSw = Stopwatch.StartNew();
            await foreach (var batch in eval.ExecuteQuery(Parse("SELECT * FROM #scaling;").Statements[0]))
                selectCount += batch.Rows.Count;
            selectSw.Stop();

            process.Refresh();
            long mbAfter = process.WorkingSet64 / 1024 / 1024;
            double selectRps = selectCount / selectSw.Elapsed.TotalSeconds;
            Console.WriteLine($"\nSELECT * returned {selectCount:N0} rows in {selectSw.ElapsedMilliseconds:N0}ms " +
                              $"({selectRps:N0} rows/sec). Memory delta: +{mbAfter - mbBefore}MB");

            Assert.Equal(totalRows, selectCount);
        }

        // Exercises the disk-spill path on sort, join, and aggregate engines.
        // Forces spill by temporarily lowering thresholds below the 50k test data size.
        [Fact(Timeout = 120_000)]
        public async Task TestSpillEnginesPaths()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.RedirectOutput = true;

            // Lower spill thresholds below 50k so every operation is forced through spill.
            eval.JoinSpillThreshold = 10_000;
            eval.WindowSpillThreshold = 10_000;

            // Build 50k rows directly from C# — no SQL WHILE loop overhead.
            const int rowCount = 50_000;
            var rowValues = new List<List<Expression>>(rowCount);
            var rng = new Random(42);
            for (int i = 1; i <= rowCount; i++)
                rowValues.Add(new List<Expression>
                {
                    new LiteralExpression(i,                 TokenType.NUMBER),
                    new LiteralExpression(rng.Next(1, 1000), TokenType.NUMBER), // Group (1-999)
                    new LiteralExpression(rng.Next(1, 10000), TokenType.NUMBER) // Value
                });

            await Execute(eval, "CREATE TABLE #spill_test (ID INT, Grp INT, Val INT);");

            var insertStmt = new InsertStatement(
                new TableReference("#spill_test"),
                new List<string> { "ID", "Grp", "Val" },
                rowValues);
            var insertScript = new Script();
            insertScript.Statements.Add(insertStmt);
            await eval.Evaluate(insertScript);

            Console.WriteLine($"Inserted {rowCount:N0} rows. Testing spill-to-disk paths...");
            var process = Process.GetCurrentProcess();

            // 1. External sort (ORDER BY on full table → ExternalSortEngine)
            var sortSw = Stopwatch.StartNew();
            await Execute(eval, "SELECT * FROM #spill_test ORDER BY Val;");
            sortSw.Stop();
            Console.WriteLine($"ORDER BY (spill sort):  {sortSw.ElapsedMilliseconds:N0}ms");

            // 2. External aggregate (GROUP BY → ExternalAggregateEngine)
            var aggSw = Stopwatch.StartNew();
            await Execute(eval, "SELECT Grp, SUM(Val) AS Total FROM #spill_test GROUP BY Grp;");
            aggSw.Stop();
            var aggResult = eval.LastResult as DataTable;
            Assert.NotNull(aggResult);
            var aggGroupCount = aggResult!.Rows.Count;
            Console.WriteLine($"GROUP BY (spill agg):   {aggSw.ElapsedMilliseconds:N0}ms → {aggGroupCount} groups");

            // 3. External join (JOIN over threshold → ExternalJoinEngine)
            await Execute(eval, "CREATE TABLE #spill_right (ID INT, Label VARCHAR(10));");
            var joinRows = new List<List<Expression>>(1000);
            for (int i = 1; i <= 1000; i++)
                joinRows.Add(new List<Expression>
                {
                    new LiteralExpression(i, TokenType.NUMBER),
                    new LiteralExpression("L" + i, TokenType.STRING)
                });
            var joinInsert = new InsertStatement(new TableReference("#spill_right"), new List<string> { "ID", "Label" }, joinRows);
            var joinScript = new Script(); joinScript.Statements.Add(joinInsert);
            await eval.Evaluate(joinScript);

            var joinSw = Stopwatch.StartNew();
            await Execute(eval, "SELECT COUNT(*) FROM #spill_test JOIN #spill_right ON #spill_test.Grp = #spill_right.ID;");
            joinSw.Stop();
            process.Refresh();
            Console.WriteLine($"JOIN (spill join):      {joinSw.ElapsedMilliseconds:N0}ms | WorkSet: {process.WorkingSet64 / 1024 / 1024}MB");

            // All three operations must complete without OOM or timeout.
            Assert.True(sortSw.ElapsedMilliseconds < 60_000, $"Spill sort took {sortSw.ElapsedMilliseconds}ms — expected < 60s");
            Assert.True(aggSw.ElapsedMilliseconds < 60_000, $"Spill aggregate took {aggSw.ElapsedMilliseconds}ms — expected < 60s");
            Assert.True(joinSw.ElapsedMilliseconds < 60_000, $"Spill join took {joinSw.ElapsedMilliseconds}ms — expected < 60s");
            Assert.True(aggGroupCount > 0, "GROUP BY returned no groups");
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task Execute(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            await eval.Evaluate(script);
        }

        [Fact]
        public async Task TestLargeJoinPerformance()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.BatchSize = 10000;

            await Execute(eval, "CREATE TABLE #t1 (ID INT, Val VARCHAR(10));");
            await Execute(eval, "CREATE TABLE #t2 (ID INT, Val VARCHAR(10));");

            Console.WriteLine("Generating 10k rows...");
            var rows1 = new List<List<Expression>>();
            for (int i = 1; i <= 10000; i++)
                rows1.Add(new List<Expression> { new LiteralExpression(i, TokenType.NUMBER), new LiteralExpression("A" + i, TokenType.STRING) });

            var insert1 = new InsertStatement(new TableReference("#t1"), new List<string> { "ID", "Val" }, rows1);
            var script1 = new Script(); script1.Statements.Add(insert1);
            await eval.Evaluate(script1);

            var script2 = new Script(); script2.Statements.Add(new InsertStatement(new TableReference("#t2"), new List<string> { "ID", "Val" }, rows1));
            await eval.Evaluate(script2);

            Stopwatch sw = new Stopwatch();

            Console.WriteLine("Running Nested Loop (Equality but no index/hash built yet)...");
            sw.Start();
            await Execute(eval, "SELECT COUNT(*) FROM #t1 JOIN #t2 ON (#t1.ID + 0) = #t2.ID;");
            sw.Stop();
            long nestedTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"Nested Loop Time: {nestedTime} ms");

            sw.Restart();
            await Execute(eval, "SELECT COUNT(*) FROM #t1 JOIN #t2 ON #t1.ID = #t2.ID;");
            sw.Stop();
            long hashTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"Hash Join Time: {hashTime} ms");

            await Execute(eval, "CREATE INDEX idx_t2_id ON #t2 (ID);");
            sw.Restart();
            await Execute(eval, "SELECT COUNT(*) FROM #t1 JOIN #t2 ON #t1.ID = #t2.ID;");
            sw.Stop();
            long indexTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"Index Join Time: {indexTime} ms");

            Assert.True(hashTime < nestedTime, "Hash join should be faster than nested loop");
        }

        [Fact]
        public async Task TestMultiColumnHashJoin()
        {
            AnsiConsole.MarkupLine("\n[cyan]Testing Multi-Column Hash Join Performance...[/]");
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.BatchSize = 1000;

            await Execute(eval, @"
                CREATE TABLE #t1 (A INT, B INT, P VARCHAR(20));
                CREATE TABLE #t2 (A INT, B INT, P VARCHAR(20));
                
                DECLARE @idx INT = 0;
                WHILE @idx < 1000
                BEGIN
                    INSERT INTO #t1 SELECT @idx, @idx % 10, 'P1_' + CAST(@idx AS VARCHAR) FROM DUAL;
                    INSERT INTO #t2 SELECT @idx, @idx % 10, 'P2_' + CAST(@idx AS VARCHAR) FROM DUAL;
                    SET @idx = @idx + 1;
                END;
            ");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            await Execute(eval, "SELECT COUNT(*) FROM #t1 JOIN #t2 ON #t1.A = #t2.A AND #t1.B = #t2.B;");
            sw.Stop();

            AnsiConsole.MarkupLine($"[green]Multi-Column Hash Join took {sw.ElapsedMilliseconds} ms for 1k x 1k records[/]");

            await eval.Evaluate(Parse("EXPLAIN SELECT * FROM #t1 JOIN #t2 ON #t1.A = #t2.A AND #t1.B = #t2.B;"));
            var plan = eval.LastResult as DataTable;
            Assert.NotNull(plan);
            var joinRow = plan!.Rows.FirstOrDefault(r => r["Operation"]?.ToString()?.Contains("Hash Join") == true);
            Assert.NotNull(joinRow);
            Assert.Contains("Hash Keys: A, B", joinRow!["Details"]?.ToString() ?? "");
        }
        [Fact]
        public async Task TestParallelConcurrencyStress()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // Create target table and initialize row
            await Execute(eval, "CREATE TABLE #stress (ID INT, Counter INT);");
            await Execute(eval, "INSERT INTO #stress VALUES (1, 0);");

            // Generate a script with 1000 parallel updates
            var lap = 1000;
            var sql = "PARALLEL (20) BEGIN ";
            for (int i = 0; i < lap; i++)
            {
                sql += "UPDATE #stress SET Counter = Counter + 1 WHERE ID = 1; ";
            }
            sql += "END";

            await Execute(eval, sql);

            // Verify result
            await Execute(eval, "SELECT Counter FROM #stress WHERE ID = 1;");
            var result = eval.LastResult as DataTable;
            Assert.NotNull(result);
            var finalCount = Convert.ToInt32(result!.Rows[0]["Counter"]);

            // This confirms that the engine's in-memory locking now works!
            Assert.Equal(lap, finalCount);
        }
    }
}
