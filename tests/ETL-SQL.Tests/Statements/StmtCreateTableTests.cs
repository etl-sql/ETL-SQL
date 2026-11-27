using System;
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
    public class CreateTableDefaultTests
    {
        private readonly IServiceProvider _serviceProvider;

        public CreateTableDefaultTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task CreateTable_WithDefault_Literal()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Defaults (
                    Id INT,
                    Name VARCHAR DEFAULT 'Unknown',
                    Score INT DEFAULT 100
                );
                INSERT INTO #Defaults (Id) VALUES (1);
                INSERT INTO #Defaults (Id, Name) VALUES (2, 'Actual');
                SELECT * FROM #Defaults ORDER BY Id;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;

            Assert.Equal(2, result.Rows.Count);

            // Row 1: Defaults applied
            Assert.Equal(1, Convert.ToInt32(result.Rows[0]["Id"]));
            Assert.Equal("Unknown", result.Rows[0]["Name"]);
            Assert.Equal(100, Convert.ToInt32(result.Rows[0]["Score"]));

            // Row 2: One default overridden, one applied
            Assert.Equal(2, Convert.ToInt32(result.Rows[1]["Id"]));
            Assert.Equal("Actual", result.Rows[1]["Name"]);
            Assert.Equal(100, Convert.ToInt32(result.Rows[1]["Score"]));
        }

        [Fact]
        public async Task CreateTable_WithDefault_Expression()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #DateDefaults (
                    Id INT,
                    CreatedDate DATETIME DEFAULT GETDATE(),
                    Calculated INT DEFAULT 5 + 5
                );
                INSERT INTO #DateDefaults (Id) VALUES (1);
                SELECT * FROM #DateDefaults;
            ";

            var before = DateTime.Now;
            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var after = DateTime.Now;
            var row = Assert.Single(evaluator.LastResult.Rows);

            var created = Assert.IsType<DateTime>(row["CreatedDate"]);
            Assert.InRange(created, before.AddSeconds(-1), after.AddSeconds(1));
            Assert.Equal(10m, Convert.ToDecimal(row["Calculated"]));
            Assert.Equal(1m, Convert.ToDecimal(row["Id"]));
        }

        [Fact]
        public async Task CreateTable_WithDefault_OmittedInInsert()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Partial (
                    A INT DEFAULT 1,
                    B INT DEFAULT 2,
                    C INT DEFAULT 3
                );
                INSERT INTO #Partial (B) VALUES (20);
                SELECT * FROM #Partial;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;

            Assert.Single(result.Rows);
            Assert.Equal(1, Convert.ToInt32(result.Rows[0]["A"]));
            Assert.Equal(20, Convert.ToInt32(result.Rows[0]["B"]));
            Assert.Equal(3, Convert.ToInt32(result.Rows[0]["C"]));
        }
    }
}
