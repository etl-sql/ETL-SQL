using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
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
        public async Task Unpivot_StreamsResultBatches()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();
            eval.BatchSize = 2;

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #QuarterlyStream (Category NVARCHAR(50), Q1 INT, Q2 INT);").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #QuarterlyStream (Category, Q1, Q2) VALUES ('A', 10, 20), ('B', 30, 40), ('C', 50, 60);").Tokenize()).Parse());

            var sql = @"
                SELECT * FROM #QuarterlyStream
                UNPIVOT (
                    Amount FOR Quarter IN (Q1, Q2)
                ) AS Unpvt;
            ";

            var stmt = new Parser(new Lexer(sql).Tokenize()).Parse().Statements[0];
            var batches = await eval.ExecuteQuery(stmt).ToListAsync();
            var rows = batches.SelectMany(b => b.Rows).ToList();

            Assert.True(batches.Count > 1);
            Assert.All(batches, b => Assert.True(b.Rows.Count <= 2));
            Assert.Equal(6, rows.Count);
            Assert.Contains("Quarter", batches[0].ColumnNames);
            Assert.Contains("Amount", batches[0].ColumnNames);
            Assert.Contains(rows, r => r["Category"]?.ToString() == "C" && r["Quarter"]?.ToString() == "Q2" && Convert.ToInt32(r["Amount"]) == 60);
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

        [Fact]
        public async Task Pivot_LargeInput_UsesSpillBackedAggregation()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();
            eval.JoinSpillThreshold = 2;
            eval.BatchSize = 1;

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #PivotSpill (Category NVARCHAR(20), Year INT, Amount INT);").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #PivotSpill VALUES ('A', 2021, 10), ('A', 2021, 15), ('A', 2022, 20), ('B', 2021, 30), ('B', 2022, 40), ('B', 2022, 5);").Tokenize()).Parse());

            var sql = "SELECT * FROM #PivotSpill PIVOT (SUM(Amount) FOR Year IN (2021, 2022)) AS Pvt;";
            var stmt = new Parser(new Lexer(sql).Tokenize()).Parse().Statements[0];
            var spillBefore = eval.Telemetry.TotalSpilledBytes;
            var batches = await eval.ExecuteQuery(stmt).ToListAsync();
            var rows = batches.SelectMany(b => b.Rows).ToList();

            Assert.True(eval.Telemetry.TotalSpilledBytes > spillBefore);
            Assert.Equal(2, rows.Count);
            Assert.All(batches, b => Assert.True(b.Rows.Count <= 1));
            var categoryA = rows.Single(r => r["Category"]?.ToString() == "A");
            var categoryB = rows.Single(r => r["Category"]?.ToString() == "B");
            Assert.Equal(25m, Convert.ToDecimal(categoryA["2021"]));
            Assert.Equal(20m, Convert.ToDecimal(categoryA["2022"]));
            Assert.Equal(30m, Convert.ToDecimal(categoryB["2021"]));
            Assert.Equal(45m, Convert.ToDecimal(categoryB["2022"]));
        }

        [Fact]
        public async Task Pivot_Chaining()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();

            // Source: Region, Year, SaleType, Amount
            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #Chained (Region NVARCHAR(50), Year INT, SaleType NVARCHAR(10), Amount DECIMAL(18,2));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #Chained VALUES ('North', 2021, 'A', 10), ('North', 2021, 'B', 20), ('North', 2022, 'A', 30), ('North', 2022, 'B', 20);").Tokenize()).Parse());

            // 1. Pivot by SaleType then Pivot by Year
            var sql = @"
                SELECT * 
                FROM #Chained 
                PIVOT (SUM(Amount) FOR SaleType IN ('A', 'B')) AS P1
                PIVOT (SUM(A) FOR Year IN (2021, 2022)) AS P2;
            ";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            var result = eval.LastResult;

            // Header should have: Region, B (from P1), 2021 (from P2), 2022 (from P2)
            Assert.NotNull(result);
            Assert.Contains("Region", result.ColumnNames);
            Assert.Contains("B", result.ColumnNames);
            Assert.Contains("2021", result.ColumnNames);
            Assert.Contains("2022", result.ColumnNames);

            // Now there should be ONE row for North because B is consistently 20
            var north = result.Rows.First(r => r["Region"]?.ToString() == "North");
            Assert.Equal(10m, Convert.ToDecimal(north["2021"])); // A (2021) 
            Assert.Equal(30m, Convert.ToDecimal(north["2022"])); // A (2022)
            Assert.Equal(20m, Convert.ToDecimal(north["B"]));
        }

        [Fact]
        public async Task Pivot_Subquery()
        {
            var eval = _serviceProvider.GetRequiredService<Evaluator>();

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE #SubSrc (ID INT, Val DECIMAL, Category NVARCHAR(10));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO #SubSrc VALUES (1, 10, 'A'), (2, 20, 'B');").Tokenize()).Parse());

            var sql = @"
                SELECT * 
                FROM (SELECT Category, Val FROM #SubSrc) AS src
                PIVOT (MAX(Val) FOR Category IN ('A', 'B')) AS pvt;
            ";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            var result = eval.LastResult;

            Assert.Single(result.Rows);
            Assert.Equal(10m, Convert.ToDecimal(result.Rows[0]["A"]));
            Assert.Equal(20m, Convert.ToDecimal(result.Rows[0]["B"]));
        }
    }
}
