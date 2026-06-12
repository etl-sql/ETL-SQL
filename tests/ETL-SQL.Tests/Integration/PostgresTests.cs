using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Spectre.Console;
using Testcontainers.PostgreSql;
using Xunit;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Collection("Database collection")]
    public class PostgresTests
    {
        private readonly DatabaseFixture _fixture;

        public PostgresTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RunAllTests()
        {
            var connStr = _fixture.PostgresConnectionString;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            await TestDataTypes(eval, connStr);
            await TestFunctions(eval, connStr);
            await TestMetadata(eval, connStr);
        }

        private async Task TestDataTypes(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing Postgres Data Types...");
            await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION db AS POSTGRES('{connStr}');").Tokenize()).Parse());

            string sql = @"
                CREATE TABLE db.typetest (
                    ID INT,
                    BigIntCol BIGINT,
                    BooleanCol BOOLEAN,
                    NumericCol NUMERIC(18,2),
                    TextCol TEXT,
                    VarcharCol VARCHAR(50),
                    DateCol DATE,
                    TimestampCol TIMESTAMP
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
            AnsiConsole.MarkupLine("  - Testing Postgres Specific Functions...");

            await eval.Evaluate(new Parser(new Lexer("SELECT STRPOS('Hello World', 'World') AS Pos FROM db.typetest LIMIT 1;").Tokenize()).Parse());
            Assert.Equal(7, Convert.ToInt32(eval.LastResult?.Rows[0]["POS"]));

            await eval.Evaluate(new Parser(new Lexer("SELECT NOW() AS Now;").Tokenize()).Parse());
            Assert.NotNull(eval.LastResult?.Rows[0]["NOW"]);

            string sql = "SELECT * FROM db.typetest WHERE LENGTH(VarcharCol) = 5;";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            Assert.Single(eval.LastResult?.Rows);
        }

        private async Task TestMetadata(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing Metadata Discovery...");
            if (eval.Connections.TryGetValue("db", out var ds) && ds is IDatabaseSource db)
            {
                var tables = (await db.GetTablesAsync()).ToList();
                Assert.Contains(tables, t => t.EndsWith(".typetest", StringComparison.OrdinalIgnoreCase) || t.Equals("typetest", StringComparison.OrdinalIgnoreCase));

                var columns = (await db.GetColumnsAsync("typetest")).ToList();
                Assert.Contains(columns, c => c.Equals("varcharcol", StringComparison.OrdinalIgnoreCase));
            }

            await TestCatalogProviderColumnComments(connStr);
        }

        private static async Task TestCatalogProviderColumnComments(string connStr)
        {
            var tableName = "catalog_comment_" + Guid.NewGuid().ToString("N");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            try
            {
                await using (var create = new NpgsqlCommand($@"
CREATE TABLE public.{tableName} (
    id INT PRIMARY KEY,
    amount NUMERIC(18,2)
);
COMMENT ON COLUMN public.{tableName}.amount IS 'Sales amount from Postgres';", conn))
                {
                    await create.ExecuteNonQueryAsync();
                }

                var provider = new PostgresCatalogProvider(connStr);
                var catalog = await provider.GetColumnMetadataAsync("public", tableName);
                var amount = Assert.Single(catalog, c => c.ColumnName.Equals("amount", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("Sales amount from Postgres", amount.Description);
            }
            finally
            {
                await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS public.{tableName};", conn);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
