using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Data;

namespace ETL_SQL.Tests
{
    public class PivotTests
    {
        private readonly IServiceProvider _serviceProvider;

        public PivotTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task Pivot_BasicSummary()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();
            
            // 1. Setup data: Category, Year, Amount
            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #Sales (Category NVARCHAR(50), Year INT, Amount DECIMAL(18,2));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #Sales (Category, Year, Amount) VALUES ('Electronics', 2021, 100), ('Electronics', 2022, 150), ('Clothing', 2021, 200), ('Clothing', 2022, 250);").Tokenize()).Parse());

            // 2. Run PIVOT
            var sql = @"
                SELECT * FROM #Sales
                PIVOT (
                    SUM(Amount)
                    FOR Year IN (2021, 2022)
                ) AS Pvt;
            ";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            var result = eval.LastResult;

            // Result should have: Category, 2021, 2022
            Assert.NotNull(result);
            Assert.Equal(2, result.Rows.Count);
            Assert.Contains("2021", result.ColumnNames);
            Assert.Contains("2022", result.ColumnNames);

            var electronics = result.Rows.First(r => r["Category"]?.ToString() == "Electronics");
            Assert.NotNull(electronics["2021"]);
            Assert.Equal(100m, Convert.ToDecimal(electronics["2021"]));
            Assert.NotNull(electronics["2022"]);
            Assert.Equal(150m, Convert.ToDecimal(electronics["2022"]));

            var clothing = result.Rows.First(r => r["Category"]?.ToString() == "Clothing");
            Assert.NotNull(clothing["2021"]);
            Assert.Equal(200m, Convert.ToDecimal(clothing["2021"]));
            Assert.NotNull(clothing["2022"]);
            Assert.Equal(250m, Convert.ToDecimal(clothing["2022"]));
        }

        [Fact]
        public async Task Unpivot_BasicRotation()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();
            
            // 1. Setup data: Category, Q1, Q2
            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #Quarterly (Category NVARCHAR(50), Q1 DECIMAL(18,2), Q2 DECIMAL(18,2));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #Quarterly (Category, Q1, Q2) VALUES ('Electronics', 100, 150), ('Clothing', 200, 250);").Tokenize()).Parse());

            // 2. Run UNPIVOT
            var sql = @"
                SELECT * FROM #Quarterly
                UNPIVOT (
                    Amount FOR Quarter IN (Q1, Q2)
                ) AS Unpvt;
            ";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            var result = eval.LastResult;

            // Result should have: Category, Quarter, Amount (4 rows)
            Assert.NotNull(result);
            Assert.Equal(4, result.Rows.Count);
            Assert.Contains("Quarter", result.ColumnNames);
            Assert.Contains("Amount", result.ColumnNames);

            var eQ1 = result.Rows.First(r => r["Category"]?.ToString() == "Electronics" && r["Quarter"]?.ToString() == "Q1");
            Assert.NotNull(eQ1["Amount"]);
            Assert.Equal(100m, Convert.ToDecimal(eQ1["Amount"]));

            var cQ2 = result.Rows.First(r => r["Category"]?.ToString() == "Clothing" && r["Quarter"]?.ToString() == "Q2");
            Assert.NotNull(cQ2["Amount"]);
            Assert.Equal(250m, Convert.ToDecimal(cQ2["Amount"]));
        }

        [Fact]
        public async Task Pivot_WithMultipleGroupingColumns()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();
            
            // Region, Category, Year, Amount
            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #RegionalSales (Region NVARCHAR(50), Category NVARCHAR(50), Year INT, Amount DECIMAL(18,2));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #RegionalSales (Region, Category, Year, Amount) VALUES ('North', 'Electronics', 2021, 10), ('North', 'Electronics', 2021, 20), ('South', 'Electronics', 2021, 30), ('North', 'Clothing', 2021, 40);").Tokenize()).Parse());

            var sql = @"
                SELECT * FROM #RegionalSales
                PIVOT (
                    SUM(Amount)
                    FOR Year IN (2021)
                ) AS Pvt;
            ";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            var result = eval.LastResult;

            // Header: Region, Category, 2021
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count); // (North, Electronics), (South, Electronics), (North, Clothing)
            
            var nElec = result.Rows.First(r => r["Region"]?.ToString() == "North" && r["Category"]?.ToString() == "Electronics");
            Assert.NotNull(nElec["2021"]);
            Assert.Equal(30m, Convert.ToDecimal(nElec["2021"])); // 10 + 20
        }
    }
}
