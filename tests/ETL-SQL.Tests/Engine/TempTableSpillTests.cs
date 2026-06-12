using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class TempTableSpillTests
    {
        [Fact]
        public async Task TestTempTableSpillAndRead()
        {
            AnsiConsole.MarkupLine("  - Scenario: #temp table spill-to-disk (5000 rows, threshold 1000)...");

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // 1. Setup threshold and large insert
            string script = @"
                SET TEMP_TABLE_SPILL_THRESHOLD = 1000;
                
                CREATE TABLE #large_data (id INT, val NVARCHAR(100));
                
                -- Insert 5000 rows using a loop to ensure multiple batches/spills
                DECLARE @i INT = 1;
                WHILE @i <= 5000
                BEGIN
                    INSERT INTO #large_data (id, val) VALUES (@i, 'Value ' + CAST(@i AS NVARCHAR));
                    SET @i = @i + 1;
                END;
                
                SELECT COUNT(*) as Total, SUM(id) as SumId FROM #large_data;
            ";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            var result = eval.LastResult;
            Assert.NotNull(result);

            int count = Convert.ToInt32(result.Rows[0]["TOTAL"]);
            long sumId = Convert.ToInt64(result.Rows[0]["SUMID"]);

            // 5000 rows expected
            Assert.Equal(5000, count);

            // Sum of 1..5000 = (n * (n+1)) / 2 = (5000 * 5001) / 2 = 12,502,500
            Assert.Equal(12502500, sumId);

            AnsiConsole.MarkupLine($"  [green]Success: Count={count}, Sum={sumId}[/]");
        }

        [Fact]
        public async Task TestTempTableSpillWithJoin()
        {
            AnsiConsole.MarkupLine("  - Scenario: #temp table spill with JOIN...");

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            string script = @"
                SET TEMP_TABLE_SPILL_THRESHOLD = 500;
                
                CREATE TABLE #t1 (id INT, name NVARCHAR(50));
                CREATE TABLE #t2 (id INT, category NVARCHAR(50));
                
                -- Fill #t1 (1000 rows -> spills)
                DECLARE @i INT = 1;
                WHILE @i <= 1000
                BEGIN
                    INSERT INTO #t1 (id, name) VALUES (@i, 'Name' + CAST(@i AS NVARCHAR));
                    SET @i = @i + 1;
                END;
                
                -- Fill #t2 (100 rows -> stays in memory)
                SET @i = 1;
                WHILE @i <= 100
                BEGIN
                    INSERT INTO #t2 (id, category) VALUES (@i * 10, 'Category' + CAST(@i AS NVARCHAR));
                    SET @i = @i + 1;
                END;
                
                -- Join spilled table with memory table
                SELECT t1.id, t1.name, t2.category
                FROM #t1 t1
                JOIN #t2 t2 ON t1.id = t2.id;
            ";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            var result = eval.LastResult;
            Assert.NotNull(result);

            // Should have 100 matches (id 10, 20, ..., 1000)
            Assert.Equal(100, result.Rows.Count);

            AnsiConsole.MarkupLine($"  [green]Success: Join returned {result.Rows.Count} rows[/]");
        }
    }
}
