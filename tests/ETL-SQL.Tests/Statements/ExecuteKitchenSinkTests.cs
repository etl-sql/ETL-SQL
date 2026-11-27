using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// EXECUTE against a connection. For a table it does not know, the mock source echoes each bound
    /// parameter with the SQL it received, which shows what actually reached the source. The table
    /// and column names avoid the mock's own tables: it matches them by substring.
    /// </summary>
    public class ExecuteKitchenSinkTests
    {
        private static async Task<Evaluator> Run(string script)
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.Connections["ds"] = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            await evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            return evaluator;
        }

        private static (object?[] Values, string Sql) Echo(Evaluator evaluator)
        {
            var rows = evaluator.LastResult!.Rows;
            return (rows.Select(r => r["ParameterValue"]).ToArray(), rows[0]["ProcessedSql"]!.ToString()!);
        }

        [Fact]
        public async Task TestExecuteKitchenSink_PositionalParameters()
        {
            var evaluator = await Run(@"
DECLARE @id INT = 101;
DECLARE @name VARCHAR(50) = 'Alice';

EXECUTE ds INTO #temp WITH(@id, @name)
BEGIN
    SELECT * FROM ds.Probe WHERE pid = ? AND label = ?;
END

SELECT * FROM #temp;");

            var (values, sql) = Echo(evaluator);
            Assert.Equal(new object?[] { 101m, "Alice" }, values.Select(v => v is int or long ? Convert.ToDecimal(v) : v));
            Assert.Contains("pid = @p0 AND label = @p1", sql, StringComparison.Ordinal);
        }

        /// <summary>An indexed placeholder names its WITH argument, whatever order it appears in.</summary>
        [Fact]
        public async Task TestExecuteKitchenSink_IndexedParameters()
        {
            var evaluator = await Run(@"
DECLARE @id INT = 102;
DECLARE @name VARCHAR(50) = 'Bob';

EXEC ds INTO #temp2 WITH(@id, @name)
BEGIN
    SELECT * FROM ds.Probe WHERE pid = ?2 AND label = ?1;
END

SELECT * FROM #temp2;");

            var (values, sql) = Echo(evaluator);
            Assert.Equal(new object?[] { 102m, "Bob" }, values.Select(v => v is int or long ? Convert.ToDecimal(v) : v));
            Assert.Contains("pid = @p1 AND label = @p0", sql, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TestExecuteKitchenSink_DynamicString()
        {
            var evaluator = await Run(@"
DECLARE @query VARCHAR(500) = 'SELECT * FROM Employees';
EXECUTE ds INTO #temp3 (@query);
SELECT * FROM #temp3;");

            // The mock's Employees table has 100 rows; #temp3 holds all of them.
            Assert.Equal(100, evaluator.LastResult!.Rows.Count);
            Assert.Contains("EmpID", evaluator.LastResult.ColumnNames);
        }

        [Theory]
        [InlineData("EXEC")]
        [InlineData("EXECUTE")]
        public async Task TestExecuteKitchenSink_ShorthandSynonyms(string keyword)
        {
            var evaluator = await Run($"DECLARE @q = 'SELECT * FROM Employees'; {keyword} ds INTO #syn (@q); SELECT * FROM #syn;");

            Assert.Equal(100, evaluator.LastResult!.Rows.Count);
        }
    }
}
