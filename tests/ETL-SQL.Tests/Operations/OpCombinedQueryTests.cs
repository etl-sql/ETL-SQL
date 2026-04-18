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

namespace ETL_SQL.Tests.Operations
{
    public class CombinedQueryTests
    {
        [Fact]
        public async Task TestGroupByWithWindowFunction()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // Setup data
            await ev.Evaluate(Parse("CREATE TABLE #Sales (Category STRING, Amount DECIMAL);"));
            await ev.Evaluate(Parse("INSERT INTO #Sales VALUES ('Electronics', 100), ('Electronics', 200), ('Clothing', 50), ('Clothing', 150), ('Books', 300);"));

            // Query: Total sales by category, ranked by total sales DESC
            var sql = @"
                SELECT 
                    Category, 
                    SUM(Amount) AS TotalSales, 
                    RANK() OVER(ORDER BY SUM(Amount) DESC) AS SalesRank
                FROM #Sales
                GROUP BY Category;
            ";

            var script = Parse(sql);
            var result = await ev.EvaluateSelect((SelectStatement)script.Statements[0]).FirstAsync();

            Assert.Equal(3, result.Rows.Count);

            // Grouping:
            // Books: 300 -> Rank 1
            // Electronics: 300 -> Rank 1 (Tie)
            // Clothing: 200 -> Rank 3

            var books = result.Rows.First(r => r["Category"]?.ToString() == "Books");
            var electronics = result.Rows.First(r => r["Category"]?.ToString() == "Electronics");
            var clothing = result.Rows.First(r => r["Category"]?.ToString() == "Clothing");

            Assert.Equal(300m, Convert.ToDecimal(books["TotalSales"]));
            Assert.Equal(300m, Convert.ToDecimal(electronics["TotalSales"]));
            Assert.Equal(200m, Convert.ToDecimal(clothing["TotalSales"]));

            Assert.Equal(1m, Convert.ToDecimal(books["SalesRank"]));
            Assert.Equal(1m, Convert.ToDecimal(electronics["SalesRank"]));
            Assert.Equal(3m, Convert.ToDecimal(clothing["SalesRank"]));
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
