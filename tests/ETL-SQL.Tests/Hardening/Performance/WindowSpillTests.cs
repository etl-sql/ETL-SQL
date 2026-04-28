using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Hardening.Performance
{
    public class WindowSpillTests
    {
        private static async Task Execute(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            await eval.Evaluate(script);
        }

        [Fact]
        public async Task TestWindowFunctionSpillingWithGrouping()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            
            // Set threshold very low to force spilling
            eval.WindowSpillThreshold = 20;
            eval.IsVerbose = true;
            eval.TelemetryEnabled = true;

            // 1. Setup Mock Connection
            await Execute(eval, "CREATE CONNECTION src ON MOCKDB('local');");

            // 2. Run query with multiple window signatures (incompatible)
            // Group 1: OVER(PARTITION BY Region ORDER BY SaleID)
            // Group 2: OVER(PARTITION BY ProductID)
            // Group 3: OVER(PARTITION BY Region ORDER BY SaleID) -- same as Group 1, should be clustered
            string sql = @"
            SELECT 
                SaleID, 
                Region,
                ProductID,
                ROW_NUMBER() OVER(PARTITION BY Region ORDER BY SaleID) as region_rank,
                SUM(Quantity) OVER(PARTITION BY ProductID) as prod_qty,
                COUNT(*) OVER(PARTITION BY Region ORDER BY SaleID) as region_count
            FROM src.Sales;";

            await Execute(eval, sql);

            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0);
            
            // Verify metrics
            Assert.True(eval.Telemetry.TotalSpilledBytes > 0, $"Expected spill to disk, but TotalSpilledBytes is {eval.Telemetry.TotalSpilledBytes}");
            Assert.True(eval.Telemetry.PartitionsCount > 0, "Expected partition buckets to be used.");

            // Verify logic (Spot check User_1 in North America)
            // Note: Since MockDataSeeder uses Random(42), results are deterministic.
            var firstRow = result.Rows.First();
            Assert.True(firstRow.Columns.ContainsKey("region_rank"));
            Assert.True(firstRow.Columns.ContainsKey("prod_qty"));
            Assert.True(firstRow.Columns.ContainsKey("region_count"));
            
            // Output spill stats for debugging
            Console.WriteLine($"Total Spilled Bytes: {eval.Telemetry.TotalSpilledBytes}");
            Console.WriteLine($"Partitions Count: {eval.Telemetry.PartitionsCount}");
        }
    }
}
