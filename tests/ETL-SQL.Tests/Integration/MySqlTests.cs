using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;
using Testcontainers.MySql;
using ETL_SQL.Connectors.MySql;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Collection("MySQL collection")]
    public class MySqlTests
    {
        private readonly MySqlFixture _fixture;

        public MySqlTests(MySqlFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RunAllTests()
        {
            var connStr = _fixture.MySqlConnectionString;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await TestDataTypes(eval, connStr);
            await TestFunctions(eval, connStr);
            await TestMetadata(eval, connStr);
            await TestTransactions(eval, connStr);
        }

        private async Task TestDataTypes(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing MySQL Data Types...");
            await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION db AS MYSQL('{connStr}');").Tokenize()).Parse());
            
            string sql = @"
                CREATE TABLE db.typetest (
                    ID INT,
                    BigIntCol BIGINT,
                    BooleanCol BOOLEAN,
                    NumericCol DECIMAL(18,2),
                    TextCol TEXT,
                    VarcharCol VARCHAR(50),
                    DateCol DATE,
                    TimestampCol TIMESTAMP NULL
                );";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            
            string insert = @"
                INSERT INTO db.typetest (ID, BigIntCol, BooleanCol, NumericCol, TextCol, VarcharCol, DateCol, TimestampCol) 
                VALUES (1, 9223372036854775807, true, 123.45, 'Large text block...', 'Hello', '2023-01-01', '2023-01-01 12:00:00');";
            await eval.Evaluate(new Parser(new Lexer(insert).Tokenize()).Parse());
            
            await eval.Evaluate(new Parser(new Lexer("SELECT * FROM db.typetest;").Tokenize()).Parse());
            var res = eval.LastResult;
            
            Assert.NotNull(res);
            Assert.Single(res.Rows);
            var row = res.Rows[0];
            Assert.Equal(9223372036854775807L, Convert.ToInt64(row["BIGINTCOL"]));
            Assert.True(Convert.ToBoolean(row["BOOLEANCOL"]));
            Assert.Equal(123.45m, Convert.ToDecimal(row["NUMERICCOL"]));
        }

        private async Task TestFunctions(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing MySQL Specific Functions...");
            
            await eval.Evaluate(new Parser(new Lexer("SELECT INSTR('Hello World', 'World') AS Pos FROM db.typetest LIMIT 1;").Tokenize()).Parse());
            Assert.Equal(7, Convert.ToInt32(eval.LastResult?.Rows[0]["POS"]));

            await eval.Evaluate(new Parser(new Lexer("SELECT NOW() AS Now;").Tokenize()).Parse());
            Assert.NotNull(eval.LastResult?.Rows[0]["NOW"]);
            
            string sql = "SELECT * FROM db.typetest WHERE LENGTH(VarcharCol) = 5;";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            Assert.Single(eval.LastResult?.Rows);
        }

        private async Task TestMetadata(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing MySQL Metadata Discovery...");
            if (eval.Connections.TryGetValue("db", out var ds) && ds is IDatabaseSource db)
            {
                var tables = (await db.GetTablesAsync()).ToList();
                Assert.Contains(tables, t => t.EndsWith(".typetest", StringComparison.OrdinalIgnoreCase) || t.Equals("typetest", StringComparison.OrdinalIgnoreCase));
                
                var columns = (await db.GetColumnsAsync("typetest")).ToList();
                Assert.Contains(columns, c => c.Equals("varcharcol", StringComparison.OrdinalIgnoreCase));
            }

            await TestCatalogProviderColumnComments(connStr);
        }

        private async Task TestTransactions(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing MySQL Transactions...");
            
            // 1. Rollback test
            string script1 = @"
                BEGIN TRANSACTION;
                INSERT INTO db.typetest (ID, VarcharCol) VALUES (99, 'RollbackMe');
                ROLLBACK TRANSACTION;
            ";
            await eval.Evaluate(new Parser(new Lexer(script1).Tokenize()).Parse());
            
            await eval.Evaluate(new Parser(new Lexer("SELECT * FROM db.typetest WHERE ID = 99;").Tokenize()).Parse());
            Assert.Empty(eval.LastResult?.Rows);

            // 2. Commit test
            string script2 = @"
                BEGIN TRANSACTION;
                INSERT INTO db.typetest (ID, VarcharCol) VALUES (100, 'CommitMe');
                COMMIT TRANSACTION;
            ";
            await eval.Evaluate(new Parser(new Lexer(script2).Tokenize()).Parse());
            
            await eval.Evaluate(new Parser(new Lexer("SELECT * FROM db.typetest WHERE ID = 100;").Tokenize()).Parse());
            Assert.Single(eval.LastResult?.Rows);
        }

        private static async Task TestCatalogProviderColumnComments(string connStr)
        {
            var tableName = "catalog_comment_" + Guid.NewGuid().ToString("N");

            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            var schema = conn.Database;
            try
            {
                await using (var create = new MySqlCommand($@"
CREATE TABLE `{tableName}` (
    `id` INT NOT NULL PRIMARY KEY,
    `amount` DECIMAL(18,2) NULL COMMENT 'Sales amount from MySQL'
);", conn))
                {
                    await create.ExecuteNonQueryAsync();
                }

                var provider = new MySqlCatalogProvider(connStr);
                var catalog = await provider.GetColumnMetadataAsync(schema, tableName);
                var amount = Assert.Single(catalog, c => c.ColumnName.Equals("amount", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("Sales amount from MySQL", amount.Description);
            }
            finally
            {
                await using var drop = new MySqlCommand($"DROP TABLE IF EXISTS `{tableName}`;", conn);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
