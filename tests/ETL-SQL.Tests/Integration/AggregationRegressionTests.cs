using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Integration
{
    public class AggregationRegressionTests
    {
        [Fact]
        public async Task TestExternalAggregation_WithDateLookingNumbers_ShouldNotFail()
        {
            // This test verifies that numeric strings that look like dates (e.g. "2024")
            // are not incorrectly converted to DateTime objects when the engine
            // spills to disk for aggregation.
            
            string csvPath = Path.Combine(AppContext.BaseDirectory, "agg_repro.csv");
            
            // Generate CSV data where 'units' looks like a year
            // CSV columns are string-typed by default in FLATFILE without schema
            await File.WriteAllTextAsync(csvPath, "category,units,revenue\nA,2024,100.50\nB,2025,200.75\nA,2024,150.25");

            try
            {
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                
                // We use a simple GROUP BY which triggers streamAggregate -> ExternalAggregateEngine
                string script = $@"
                    CREATE CONNECTION c AS FLATFILE('{csvPath.Replace("\\", "/")}', HEADER = ON);
                    
                    SELECT category, SUM(units) as total_units, SUM(revenue) as total_revenue
                    INTO #summary
                    FROM c.FILE
                    GROUP BY category;
                    
                    SELECT * FROM #summary ORDER BY category;
                ";

                // This should not throw "Invalid cast from 'DateTime' to 'Decimal'"
                await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
                
                var results = eval.LastResult?.Rows;
                Assert.NotNull(results);
                Assert.Equal(2, results.Count);
                
                // Category A: 2024 + 2024 = 4048
                Assert.Equal(4048m, Convert.ToDecimal(results[0]["total_units"]));
                Assert.Equal(250.75m, Convert.ToDecimal(results[0]["total_revenue"]));
                
                // Category B: 2025
                Assert.Equal(2025m, Convert.ToDecimal(results[1]["total_units"]));
                Assert.Equal(200.75m, Convert.ToDecimal(results[1]["total_revenue"]));
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }
    }
}
