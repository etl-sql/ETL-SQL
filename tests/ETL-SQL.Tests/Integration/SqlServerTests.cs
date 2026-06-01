using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Collection("Database collection")]
    public class SqlServerTests
    {
        private readonly DatabaseFixture _fixture;
        
        public SqlServerTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RunAllTests()
        {
            var connStr = _fixture.SqlConnectionString;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await TestDataTypes(eval, connStr);
            await TestFunctions(eval, connStr);
            await TestMetadata(eval, connStr);
        }

        private async Task TestDataTypes(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing SQL Server Data Types...");
            await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION db AS MSSQL('{connStr}');").Tokenize()).Parse());
            
            string sql = @"
                CREATE TABLE db.TypeTest (
                    ID INT,
                    BigIntCol BIGINT,
                    BitCol BIT,
                    DecimalCol DECIMAL(18,2),
                    VarCharCol VARCHAR(50),
                    NVarCharCol NVARCHAR(50),
                    DateCol DATE,
                    DateTimeCol DATETIME
                );";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            
            string insert = @"
                INSERT INTO db.TypeTest (ID, BigIntCol, BitCol, DecimalCol, VarCharCol, NVarCharCol, DateCol, DateTimeCol) 
                VALUES (1, 9223372036854775807, 1, 123.45, 'Hello', 'World', '2023-01-01', '2023-01-01 12:00:00');";
            await eval.Evaluate(new Parser(new Lexer(insert).Tokenize()).Parse());
            
            await eval.Evaluate(new Parser(new Lexer("SELECT * FROM db.TypeTest;").Tokenize()).Parse());
            var res = eval.LastResult;
            
            Assert.NotNull(res);
            Assert.Single(res.Rows);
            var row = res.Rows[0];
            Assert.Equal(9223372036854775807L, Convert.ToInt64(row["BIGINTCOL"]));
            Assert.True(Convert.ToBoolean(row["BITCOL"]));
            Assert.Equal(123.45m, Convert.ToDecimal(row["DECIMALCOL"]));
        }

        private async Task TestFunctions(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing T-SQL Specific Functions...");
            
            await eval.Evaluate(new Parser(new Lexer("SELECT GETDATE() AS Now;").Tokenize()).Parse());
            Assert.NotNull(eval.LastResult?.Rows[0]["Now"]);
            
            string sql = "SELECT * FROM db.TypeTest WHERE LEN(VarCharCol) = 5;";
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
                Assert.Contains(columns, c => c.Equals("VarCharCol", StringComparison.OrdinalIgnoreCase));
            }

            await TestCatalogProviderColumnComments(connStr);
        }

        private static async Task TestCatalogProviderColumnComments(string connStr)
        {
            var tableName = "CatalogComment_" + Guid.NewGuid().ToString("N");

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            try
            {
                await using (var create = new SqlCommand($@"
CREATE TABLE dbo.{tableName} (
    Id INT NOT NULL PRIMARY KEY,
    Amount DECIMAL(18,2) NULL
);", conn))
                {
                    await create.ExecuteNonQueryAsync();
                }

                await using (var comment = new SqlCommand($@"
EXEC sys.sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Sales amount from SQL Server',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'{tableName}',
    @level2type = N'COLUMN', @level2name = N'Amount';", conn))
                {
                    await comment.ExecuteNonQueryAsync();
                }

                var provider = new SqlServerCatalogProvider(connStr);
                var catalog = await provider.GetColumnMetadataAsync("dbo", tableName);
                var amount = Assert.Single(catalog, c => c.ColumnName.Equals("Amount", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("Sales amount from SQL Server", amount.Description);
            }
            finally
            {
                await using var drop = new SqlCommand($"DROP TABLE IF EXISTS dbo.{tableName};", conn);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
