using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Testcontainers.Oracle;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using Spectre.Console;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Trait("Connector", "ORACLE")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    [Collection("Database collection")]
    public class OracleTests
    {
        private readonly DatabaseFixture _fixture;
        
        public OracleTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RunAllTests()
        {
            var connStr = _fixture.OracleConnectionString;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await TestDataTypes(eval, connStr);
            await TestFunctions(eval, connStr);
            await TestMetadata(eval, connStr);
        }

        private async Task TestDataTypes(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing Oracle Data Types...");
            await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION db AS ORACLE('{connStr}');").Tokenize()).Parse());
            
            string sql = @"
                CREATE TABLE db.TypeTest (
                    ID NUMBER,
                    FloatCol FLOAT,
                    CharCol CHAR(10),
                    Varchar2Col VARCHAR2(50),
                    DateCol DATE,
                    TimestampCol TIMESTAMP
                );";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO db.TypeTest (ID, FloatCol, CharCol, Varchar2Col, DateCol, TimestampCol) VALUES (1, 123.45, 'A', 'Hello', TO_DATE('2023-01-01', 'YYYY-MM-DD'), TO_TIMESTAMP('2023-01-01 12:00:00', 'YYYY-MM-DD HH24:MI:SS'));").Tokenize()).Parse());
            
            await eval.Evaluate(new Parser(new Lexer("SELECT * FROM db.TypeTest;").Tokenize()).Parse());
            var rows = eval.LastResult?.Rows;
            Assert.NotNull(rows);
            Assert.Single(rows);
            var row = rows[0];
            Assert.Equal(123.45m, Convert.ToDecimal(row["FLOATCOL"]));
            Assert.Equal("Hello", row["VARCHAR2COL"]?.ToString()?.Trim());
        }

        private async Task TestFunctions(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing Oracle Specific Functions...");
            
            await eval.Evaluate(new Parser(new Lexer("SELECT SUBSTR('Hello World', 7, 5) AS Sub FROM db.TypeTest WHERE ROWNUM <= 1;").Tokenize()).Parse());
            Assert.Equal("World", eval.LastResult?.Rows[0]["SUB"]?.ToString());
            
            await eval.Evaluate(new Parser(new Lexer("SELECT SYSDATE AS Now;").Tokenize()).Parse());
            string sql = "SELECT * FROM db.TypeTest WHERE LENGTH(Varchar2Col) = 5;";
            await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            Assert.Single(eval.LastResult?.Rows);
        }

        private async Task TestMetadata(Evaluator eval, string connStr)
        {
            AnsiConsole.MarkupLine("  - Testing Metadata Discovery...");
            if (eval.Connections.TryGetValue("db", out var ds) && ds is IDatabaseSource db)
            {
                var tables = (await db.GetTablesAsync()).ToList();
                Assert.Contains(tables, t => t.EndsWith(".TYPETEST", StringComparison.OrdinalIgnoreCase) || t.Equals("TYPETEST", StringComparison.OrdinalIgnoreCase));
                
                var columns = (await db.GetColumnsAsync("typetest")).ToList();
                Assert.Contains(columns, c => c.Equals("VARCHAR2COL", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public async Task MissingTable_ReadBatches_WrapsAsExecutionException()
        {
            var ds = new OracleDataSource(
                SystemExecutionContext.Instance,
                _fixture.OracleConnectionString,
                $"missing_table_{Guid.NewGuid():N}");

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await foreach (var _ in ds.ReadBatches(batchSize: 10))
                {
                }
            });

            Assert.Contains("Oracle", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task InvalidSql_ExecuteRawSql_WrapsAsExecutionException()
        {
            var ds = new OracleDataSource(
                SystemExecutionContext.Instance,
                _fixture.OracleConnectionString);

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await foreach (var _ in ds.ExecuteRawSql("SELECT * FROM"))
                {
                }
            });

            Assert.Contains("Oracle", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
