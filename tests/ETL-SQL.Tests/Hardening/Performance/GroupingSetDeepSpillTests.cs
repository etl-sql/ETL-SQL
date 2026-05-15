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
    public class GroupingSetDeepSpillTests
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
        public async Task TestCubeDeepSpilling()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            
            // Set threshold very low to force external aggregation
            eval.JoinSpillThreshold = 10;
            eval.ExternalHashPartitions = 4;

            await Execute(eval, "CREATE CONNECTION src ON MOCKDB('local');");

            // CUBE(Region, ProductID) should generate 4 grouping sets: (Region, ProductID), (Region), (ProductID), ()
            string sql = @"
            SELECT 
                Region,
                ProductID,
                COUNT(*) as row_count,
                SUM(Total) as total_amount
            FROM src.Sales
            GROUP BY CUBE(Region, ProductID);";

            await Execute(eval, sql);

            var result = eval.LastResult;
            Assert.NotNull(result);
            
            // 250 input rows expanded into 4 sets each = 1000 intermediate rows spilled.
            // After aggregation, unique groups will vary based on data.
            Assert.True(eval.Telemetry.TotalSpilledBytes > 0);
            Assert.True(eval.Telemetry.AggregateGroupsCount > 0);
            Assert.Equal(4.0, eval.Telemetry.AggregateExpansionRatio); // 4 sets for CUBE(A, B)

            // Verify the presence of sub-totals (where Region is null or ProductID is null)
            var allResults = result.Rows;
            Assert.Contains(allResults, r => r["Region"] == null && r["ProductID"] == null); // Grand total
            Assert.Contains(allResults, r => r["Region"] != null && r["ProductID"] == null); // Region sub-total
            Assert.Contains(allResults, r => r["Region"] == null && r["ProductID"] != null); // ProductID sub-total
            Assert.Contains(allResults, r => r["Region"] != null && r["ProductID"] != null); // Granular data

            // Verify totals
            decimal? grandTotal = (decimal?)allResults.First(r => r["Region"] == null && r["ProductID"] == null)["total_amount"];
            decimal? sumOfSums = allResults.Where(r => r["Region"] != null && r["ProductID"] != null).Sum(r => (decimal?)r["total_amount"]);
            
            Assert.Equal(grandTotal, sumOfSums);
        }

        [Fact]
        public async Task TestRollupExpansionRatio()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            
            eval.JoinSpillThreshold = 1;

            await Execute(eval, "CREATE CONNECTION src ON MOCKDB('local');");

            // ROLLUP(Region, ProductID, SaleID) should generate 4 grouping sets: (R,P,S), (R,P), (R), ()
            string sql = @"
            SELECT Region, ProductID, SaleID, COUNT(*) FROM src.Sales GROUP BY ROLLUP(Region, ProductID, SaleID);";

            await Execute(eval, sql);

            Assert.Equal(4.0, eval.Telemetry.AggregateExpansionRatio);
            Assert.True(eval.Telemetry.TotalSpilledBytes > 0);
        }
    }
}
