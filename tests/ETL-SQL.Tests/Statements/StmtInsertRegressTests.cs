using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class InsertValueRegressionTests
    {
        [Fact]
        public async Task TestInsertMultipleValuesRegression()
        {
            // Use the centralized ServiceProvider from Program (initialized in TestSetup)
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            var script = @"
                CREATE TABLE #Orders (
                    OrderId INT PRIMARY KEY,
                    UserId INT,
                    OrderDate DATETIME
                );
                INSERT INTO #Orders 
                (OrderId, UserId, OrderDate) 
                VALUES 
                (1, 101, '2024-01-01')
                , 
                (2, 102, 
                '2024-01-02'), 
                (3, 103, '2024-01-03')
                ;
                
                SELECT COUNT(*) AS RowCount FROM #Orders;
            ";

            await evaluator.Evaluate(TestHelpers.Parse(script));
            var table = evaluator.LastResult;

            Assert.NotNull(table);
            Assert.Single(table.Rows);

            // Explicitly cast to decimal then int as our engine often parses numbers as decimal
            var rowCount = Convert.ToInt32(table.Rows[0]["RowCount"]);
            Assert.Equal(3, rowCount);
        }

        [Fact]
        public async Task TestMultipleValuesKeywordsRegression()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            var script = @"
                CREATE TABLE #T (ID INT);
                INSERT INTO #T 
                VALUES (1), (2)
                VALUES (3), (4);
                SELECT COUNT(*) AS RowCount FROM #T;
            ";

            await evaluator.Evaluate(TestHelpers.Parse(script));
            var table = evaluator.LastResult;

            Assert.Equal(4, Convert.ToInt32(table.Rows[0]["RowCount"]));
        }
        [Fact]
        public async Task TestMultiLineMultiValueInsert_Complex()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            var script = @"
                CREATE TABLE #ComplexOrders (
                    Id INT, 
                    Code VARCHAR(10), 
                    Val DECIMAL(10,2)
                );

                -- Test multiple VALUES segments, multi-line row entries, 
                -- and interleaved metadata tags (robustness check)
                INSERT INTO #ComplexOrders (Id, Code, Val) 
                VALUES 
                    (1, 'A', 10.5) /* @row: 1; */, 
                    (
                        2, 
                        'B', 
                        20.0
                    )
                VALUES 
                    (3, 'C', 30.75),
                    (4, 'D', 40.0) /* @batch: 2; */;

                SELECT SUM(Val) AS TotalVal FROM #ComplexOrders;
            ";

            await evaluator.Evaluate(TestHelpers.Parse(script));
            var table = evaluator.LastResult;

            Assert.Equal(101.25m, Convert.ToDecimal(table.Rows[0]["TotalVal"]));
        }
    }
}
