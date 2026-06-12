using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening.Performance
{
    [Trait("Category", "Performance")]
    public class WindowDeepSpillTests
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
        public async Task TestDeepSpillingRankingFunctions()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();

            // Set threshold very low to force DEEP-SPILLING (partition-level streaming)
            // Sales table has 250 rows.
            eval.WindowSpillThreshold = 10;
            eval.ExternalHashPartitions = 2;

            await Execute(eval, "CREATE CONNECTION src AS MOCKDB();");

            string sql = @"
            SELECT 
                SaleID, 
                Region,
                ProductID,
                ROW_NUMBER() OVER(PARTITION BY Region ORDER BY ProductID) as row_num,
                RANK() OVER(PARTITION BY Region ORDER BY ProductID) as rnk,
                DENSE_RANK() OVER(PARTITION BY Region ORDER BY ProductID) as drnk
            FROM src.Sales;";

            await Execute(eval, sql);

            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0);
            Assert.True(eval.Telemetry.TotalSpilledBytes > 0);

            // 1. Check for duplicates first
            var duplicateSales = result.Rows.GroupBy(r => r["SaleID"]).Where(g => g.Count() > 1).ToList();
            if (duplicateSales.Any())
            {
                Console.WriteLine($"Found {duplicateSales.Count} duplicate SaleIDs!");
                foreach (var dup in duplicateSales.Take(5))
                    Console.WriteLine($"Duplicate SaleID: {dup.Key}, Count: {dup.Count()}");
            }
            Assert.Empty(duplicateSales);

            // MockDataSeeder generates 250 rows for Sales
            Assert.Equal(250, result.Rows.Count);

            // 2. Verify ranking logic
            var regionGroups = result.Rows.GroupBy(r => r["Region"]?.ToString());
            foreach (var group in regionGroups)
            {
                var sorted = group.OrderBy(r => r["ProductID"]).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var curr = sorted[i];
                    try
                    {
                        Assert.Equal((decimal)(i + 1), curr["row_num"]);

                        if (i > 0)
                        {
                            var prev = sorted[i - 1];
                            if (curr["ProductID"]!.Equals(prev["ProductID"]))
                            {
                                Assert.Equal(prev["rnk"], curr["rnk"]);
                                Assert.Equal(prev["drnk"], curr["drnk"]);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Assertion failed for Region: {group.Key}, Row: {i + 1}");
                        Console.WriteLine($"Current Row ProductID: {curr["ProductID"]}, Rank: {curr["rnk"]}, DenseRank: {curr["drnk"]}, RowNum: {curr["row_num"]}");
                        if (i > 0)
                        {
                            var prev = sorted[i - 1];
                            Console.WriteLine($"Previous Row ProductID: {prev["ProductID"]}, Rank: {prev["rnk"]}, DenseRank: {prev["drnk"]}, RowNum: {prev["row_num"]}");
                        }
                        throw;
                    }
                }
            }
        }
    }
}
