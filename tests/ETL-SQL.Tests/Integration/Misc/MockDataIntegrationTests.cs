using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Integration.Misc
{
    public class MockDataIntegrationTests
    {
        private Evaluator CreateEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        [Fact]
        public async Task TestGenerateIntoTempTable()
        {
            var evaluator = CreateEvaluator();
            var sql = @"
GENERATE 5 ROWS INTO #temp AS (
    id = 'SEQUENCE(1, 1)',
    val = 'RANDOM_INT(10, 20)'
);
SELECT * FROM #temp;
";
            await evaluator.Evaluate(Parse(sql));
            
            // Verify rows processed (5 from generate)
            Assert.True(evaluator.Telemetry.RowsProcessed >= 5);
            
            var results = await evaluator.ExecuteQuery(((Script)Parse("SELECT * FROM #temp;")).Statements[0]).ToListAsync();
            var table = results.FirstOrDefault();
            
            Assert.NotNull(table);
            Assert.Equal(5, table.Rows.Count);
            Assert.Equal(2, table.ColumnNames.Count);
            Assert.Contains("id", table.ColumnNames);
            Assert.Contains("val", table.ColumnNames);
            
            // Verify sequence
            Assert.Equal(1, Convert.ToInt32(table.Rows[0]["id"]));
            Assert.Equal(5, Convert.ToInt32(table.Rows[4]["id"]));
        }

        [Fact]
        public async Task TestGenerateIntoVariableTable()
        {
            var evaluator = CreateEvaluator();
            var sql = @"
DECLARE @v TABLE;
GENERATE 3 ROWS INTO @v AS (
    msg = 'RANDOM(Hello, World)'
);
SELECT * FROM @v;
";
            await evaluator.Evaluate(Parse(sql));
            
            var results = await evaluator.ExecuteQuery(((Script)Parse("SELECT * FROM @v;")).Statements[0]).ToListAsync();
            var table = results.FirstOrDefault();
            
            Assert.NotNull(table);
            Assert.Equal(3, table.Rows.Count);
            Assert.Contains("msg", table.ColumnNames);
            
            foreach (var row in table.Rows)
            {
                var msg = row["msg"].ToString();
                Assert.True(msg == "Hello" || msg == "World");
            }
        }

        [Fact]
        public async Task TestGenerateWithSeed()
        {
            var evaluator = CreateEvaluator();
            var sql = @"
GENERATE 10 ROWS INTO #a WITH (SEED = 123) AS ( val = 'RANDOM_INT(1, 1000)' );
GENERATE 10 ROWS INTO #b WITH (SEED = 123) AS ( val = 'RANDOM_INT(1, 1000)' );
";
            await evaluator.Evaluate(Parse(sql));
            
            var resA = (await evaluator.ExecuteQuery(((Script)Parse("SELECT * FROM #a;")).Statements[0]).ToListAsync()).First();
            var resB = (await evaluator.ExecuteQuery(((Script)Parse("SELECT * FROM #b;")).Statements[0]).ToListAsync()).First();
            
            Assert.Equal(10, resA.Rows.Count);
            Assert.Equal(10, resB.Rows.Count);

            for (int i = 0; i < 10; i++)
            {
                var valA = Convert.ToInt32(resA.Rows[i]["val"]);
                var valB = Convert.ToInt32(resB.Rows[i]["val"]);
                if (valA != valB) Console.WriteLine($"Mismatch at row {i}: A={valA}, B={valB}");
                Assert.Equal(valA, valB);
            }
        }

        [Fact]
        public async Task TestAllRandomFunctions()
        {
            var evaluator = CreateEvaluator();
            var sql = @"
GENERATE 1 ROWS INTO #test AS (
    seq = 'SEQUENCE(100, -1)',
    rnd = 'RANDOM(Choice)',
    ri  = 'RANDOM_INT(5, 5)',
    rd  = 'RANDOM_DECIMAL(3.14, 3.14)',
    dt  = 'SEQUENCE(2026-04-21, 1, DAY)'
);
";
            await evaluator.Evaluate(Parse(sql));
            var row = (await evaluator.ExecuteQuery(((Script)Parse("SELECT * FROM #test;")).Statements[0]).ToListAsync()).First().Rows[0];
            
            Assert.Equal(100, Convert.ToInt32(row["seq"]));
            Assert.Equal("Choice", row["rnd"].ToString());
            Assert.Equal(5, Convert.ToInt32(row["ri"]));
            Assert.Equal(3.14m, Convert.ToDecimal(row["rd"]));
            Assert.Equal(new DateTime(2026, 4, 21), Convert.ToDateTime(row["dt"]));
        }

        [Fact]
        public async Task TestVariableTableTypeUsage()
        {
            var evaluator = CreateEvaluator();
            // Test DECLARE @v TABLE followed by a manual INSERT then SELECT
            var sql = @"
DECLARE @v TABLE;
INSERT INTO @v (ID, Name) VALUES (1, 'Test');
SELECT * FROM @v;
";
            await evaluator.Evaluate(Parse(sql));
            
            var results = await evaluator.ExecuteQuery(((Script)Parse("SELECT * FROM @v;")).Statements[0]).ToListAsync();
            var table = results.FirstOrDefault();
            
            Assert.NotNull(table);
            Assert.Single(table.Rows);
            Assert.Contains("Name", table.ColumnNames);
            Assert.Equal("Test", table.Rows[0]["Name"]?.ToString());
        }
    }
}
