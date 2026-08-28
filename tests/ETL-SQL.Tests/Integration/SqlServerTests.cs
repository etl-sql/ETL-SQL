using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Testcontainers.MsSql;
using Xunit;

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

        [Fact]
        public async Task VerifiedViewerContext_IsParameterizedAndClearedBeforePoolReuseAcrossTerminalPaths()
        {
            var builder = new SqlConnectionStringBuilder(_fixture.SqlConnectionString)
            {
                ApplicationName = "etlsql-viewer-context-" + Guid.NewGuid().ToString("N"),
                MaxPoolSize = 1
            };
            var connStr = builder.ConnectionString;
            const string hostile = "finance'; EXEC sp_addrolemember 'db_owner','viewer'; --";
            string executingLogin;
            await using (var baseline = new SqlConnection(connStr))
            {
                await baseline.OpenAsync();
                await using var identity = new SqlCommand("SELECT ORIGINAL_LOGIN()", baseline);
                executingLogin = (string)(await identity.ExecuteScalarAsync())!;
            }

            await using (var mismatched = new SqlServerDataSource(SystemExecutionContext.Instance, connStr))
            {
                var denied = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
                    mismatched.BeginVerifiedViewerContextAsync(
                        Context("identity-mismatch", "different-login"), CancellationToken.None));
                Assert.Contains("does not match", denied.Message);
            }
            await AssertContextClearedAsync(connStr);

            await using (var success = new SqlServerDataSource(SystemExecutionContext.Instance, connStr))
            {
                await success.BeginVerifiedViewerContextAsync(
                    Context("success", executingLogin, hostile), CancellationToken.None);
                var batch = await success.ExecuteRawSql(
                    "SELECT CONVERT(nvarchar(2048), SESSION_CONTEXT(N'etlsql.viewer_id')) AS viewer, " +
                    "CONVERT(nvarchar(2048), SESSION_CONTEXT(N'etlsql.claim_department')) AS department, " +
                    "CONVERT(nvarchar(2048), SESSION_CONTEXT(N'etlsql.claim_cost-center')) AS cost_center, " +
                    "ORIGINAL_LOGIN() AS original_login, SUSER_SNAME() AS effective_login",
                    null, CancellationToken.None).FirstAsync();
                var row = Assert.Single(batch.Rows);
                Assert.Equal(hostile, row["viewer"]);
                Assert.Equal(hostile, row["department"]);
                Assert.Equal(hostile, row["cost_center"]);
                Assert.Equal(executingLogin, row["original_login"]);
                Assert.Equal(executingLogin, row["effective_login"]);
                await success.CommitAsync();
            }
            await AssertContextClearedAsync(connStr);

            await AssertFailurePathClearsAsync(connStr, executingLogin, "provider-failure", async source =>
            {
                await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                    await source.ExecuteRawSql(
                        "SELECT * FROM dbo.__etlsql_missing_viewer_context_table",
                        null, CancellationToken.None).FirstAsync());
            });

            await AssertFailurePathClearsAsync(connStr, executingLogin, "cancellation", async source =>
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                var cancelled = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                    await source.ExecuteRawSql("WAITFOR DELAY '00:00:05'; SELECT 1 AS value",
                        null, cancellation.Token).FirstAsync());
                Assert.IsType<SqlException>(cancelled.InnerException);
            });

            await using (var timeout = new SqlServerDataSource(
                SystemExecutionContext.Instance, connStr, options: new Dictionary<string, string>
                {
                    ["TIMEOUT_SECONDS"] = "1"
                }))
            {
                await timeout.BeginVerifiedViewerContextAsync(
                    Context("timeout", executingLogin), CancellationToken.None);
                await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                    await timeout.ExecuteRawSql("WAITFOR DELAY '00:00:05'; SELECT 1 AS value",
                        null, CancellationToken.None).FirstAsync());
            }
            await AssertContextClearedAsync(connStr);

            await using (var broken = new SqlServerDataSource(SystemExecutionContext.Instance, connStr))
            {
                await broken.BeginVerifiedViewerContextAsync(
                    Context("broken", executingLogin), CancellationToken.None);
                var spidBatch = await broken.ExecuteRawSql("SELECT @@SPID AS spid", null, CancellationToken.None).FirstAsync();
                var spid = Convert.ToInt32(Assert.Single(spidBatch.Rows)["spid"]);
                await using var killer = new SqlConnection(_fixture.SqlConnectionString);
                await killer.OpenAsync();
                await using var kill = new SqlCommand($"KILL {spid}", killer);
                await kill.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                    await broken.ExecuteRawSql("SELECT 1 AS value", null, CancellationToken.None).FirstAsync());
            }
            await AssertContextClearedAsync(connStr);

            VerifiedViewerContext Context(string operationId, string executingCredential, string viewer = "viewer") =>
                new("tenant-a", "sqlserver-reports", operationId, viewer, "real-viewer",
                    executingCredential,
                    new Dictionary<string, string> { ["department"] = viewer, ["cost-center"] = viewer },
                    DateTimeOffset.UtcNow);
        }

        private static async Task AssertFailurePathClearsAsync(
            string connectionString,
            string executingLogin,
            string operationId,
            Func<SqlServerDataSource, Task> exercise)
        {
            await using (var source = new SqlServerDataSource(SystemExecutionContext.Instance, connectionString))
            {
                await source.BeginVerifiedViewerContextAsync(
                    new VerifiedViewerContext(
                        "tenant-a", "sqlserver-reports", operationId, "viewer", "real-viewer",
                        executingLogin, new Dictionary<string, string>(), DateTimeOffset.UtcNow),
                    CancellationToken.None);
                await exercise(source);
            }
            await AssertContextClearedAsync(connectionString);
        }

        private static async Task AssertContextClearedAsync(string connectionString)
        {
            await using var reused = new SqlConnection(connectionString);
            await reused.OpenAsync();
            await using var check = new SqlCommand(
                "SELECT SESSION_CONTEXT(N'etlsql.viewer_id'), SESSION_CONTEXT(N'etlsql.claim_department'), " +
                "SESSION_CONTEXT(N'etlsql.claim_cost-center')",
                reused);
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
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
