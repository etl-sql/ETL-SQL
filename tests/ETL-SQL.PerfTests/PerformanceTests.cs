using Xunit;
using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;
using ETL_SQL.Common;

namespace ETL_SQL.Tests
{
    public class PerformanceTests
    {
        [Fact]
        public async Task TestLargeDatasetMemory()
        {
            AnsiConsole.MarkupLine("\n[cyan]Testing Large Dataset Memory Usage (1M rows)...[/]");
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.BatchSize = 10000;
            
            await Execute(eval, @"
                CREATE TABLE #big (ID INT, Val VARCHAR(20));
                DECLARE @i INT = 1;
                WHILE @i <= 1000000
                BEGIN
                    INSERT INTO #big 
                    SELECT @i, 'Value ' + CAST(@i AS VARCHAR) FROM DUAL
                    UNION ALL SELECT @i+1, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+2, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+3, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+4, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+5, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+6, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+7, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+8, 'Value ' FROM DUAL
                    UNION ALL SELECT @i+9, 'Value ' FROM DUAL;
                    
                    SET @i = @i + 10;
                END
            ");

            var process = Process.GetCurrentProcess();
            process.Refresh();
            long memoryBefore = process.WorkingSet64;

            AnsiConsole.MarkupLine($"[grey]Memory before streaming select: {memoryBefore / 1024 / 1024} MB[/]");

            int count = 0;
            var batches = await eval.ExecuteQuery(Parse("SELECT * FROM #big;").Statements[0]).ToListAsync();
            foreach (var batch in batches)
            {
                count += batch.Rows.Count;
                if (count % 100000 == 0)
                {
                    process.Refresh();
                    AnsiConsole.Markup($"\r[grey]Processed {count:N0} rows... Memory: {process.WorkingSet64 / 1024 / 1024} MB[/]");
                }
            }
            AnsiConsole.WriteLine();

            process.Refresh();
            long memoryAfter = process.WorkingSet64;
            AnsiConsole.MarkupLine($"[green]Finished streaming 1M rows. Final Memory: {memoryAfter / 1024 / 1024} MB[/]");
            
            Assert.Equal(1000000, count);
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
            for(int i=1; i<=10000; i++) 
                rows1.Add(new List<Expression> { new LiteralExpression(i, TokenType.NUMBER), new LiteralExpression("A"+i, TokenType.STRING) });
            
            var insert1 = new InsertStatement(new TableReference("#t1"), new List<string>{"ID", "Val"}, rows1);
            var script1 = new Script(); script1.Statements.Add(insert1);
            await eval.Evaluate(script1);

            var script2 = new Script(); script2.Statements.Add(new InsertStatement(new TableReference("#t2"), new List<string>{"ID", "Val"}, rows1));
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
