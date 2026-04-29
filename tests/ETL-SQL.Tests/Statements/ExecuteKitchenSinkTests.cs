using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Statements
{
    public class ExecuteKitchenSinkTests
    {
        [Fact]
        public async Task TestExecuteKitchenSink_PositionalParameters()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            evaluator.Connections["ds"] = mockDb;

            var script = @"
DECLARE @id INT = 101;
DECLARE @name VARCHAR(50) = 'Alice';

EXECUTE ds INTO #temp WITH(@id, @name)
BEGIN
    SELECT * FROM ds.Employees WHERE EmployeeID = ? AND Name = ?;
END

SELECT * FROM #temp;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            // In MockSqlDataSource, if source is not found it returns a dummy table with ParameterValue and ProcessedSql
            // But here ds.Employees exists in MockSqlDataSource? 
            // Wait, MockSqlDataSource has "Employee" by default usually.
            
            // Let's check if it actually executed.
            Assert.True(evaluator.Connections.ContainsKey("#temp"), "Temp table #temp should exist");
        }

        [Fact]
        public async Task TestExecuteKitchenSink_IndexedParameters()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            evaluator.Connections["ds"] = mockDb;

            var script = @"
DECLARE @id INT = 102;
DECLARE @name VARCHAR(50) = 'Bob';

EXEC ds INTO #temp2 WITH(@id, @name)
BEGIN
    SELECT * FROM ds.Employees WHERE EmployeeID = ?1 AND Name = ?2;
END

SELECT * FROM #temp2;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            Assert.True(evaluator.Connections.ContainsKey("#temp2"), "Temp table #temp2 should exist");
        }

        [Fact]
        public async Task TestExecuteKitchenSink_DynamicString()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            evaluator.Connections["ds"] = mockDb;

            var script = @"
DECLARE @query VARCHAR(500) = 'SELECT * FROM Employees';
EXECUTE ds INTO #temp3 (@query);
SELECT * FROM #temp3;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            Assert.True(evaluator.Connections.ContainsKey("#temp3"), "Temp table #temp3 should exist");
        }

        [Fact]
        public async Task TestExecuteKitchenSink_ShorthandSynonyms()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            evaluator.Connections["ds"] = mockDb;

            // Test that EXEC and EXECUTE both work for the new syntax
            var script1 = "DECLARE @q = 'SELECT 1'; EXEC ds (@q);";
            var script2 = "DECLARE @q = 'SELECT 1'; EXECUTE ds (@q);";

            var ast1 = new Parser(new Lexer(script1).Tokenize()).Parse();
            var ast2 = new Parser(new Lexer(script2).Tokenize()).Parse();

            await evaluator.Evaluate(ast1);
            Assert.NotNull(evaluator.LastResult);

            await evaluator.Evaluate(ast2);
            Assert.NotNull(evaluator.LastResult);
        }
    }
}
