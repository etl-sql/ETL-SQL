using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Integration
{
    /// <summary>
    /// Closes every container the engine's <see cref="IDockerManager"/> started, once, after the
    /// whole class has run. The engine deliberately persists containers across runs (its
    /// DisposeAsync is a no-op), so without this teardown the named DB containers and their
    /// multi-GB anonymous data volumes are only reaped by Ryuk at process exit — and leak entirely
    /// if the run is killed. CloseContainers operates on the manager's static registry, so a fresh
    /// manager instance still sees and removes them all.
    /// </summary>
    public sealed class DockerCleanupFixture : IAsyncLifetime
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var manager = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<IDockerManager>();
            await manager.CloseContainers(null);
        }
    }

    [Trait("Category", "Integration")]
    public class DockerTests : IClassFixture<DockerCleanupFixture>
    {
        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            return new Parser(lexer.Tokenize()).Parse();
        }

        [Fact]
        public async Task TestMssqlDockerConnection()
        {
            var sql = @"
                USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
                DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
                CREATE CONNECTION ds AS MSSQL(@conn);
                EXECUTE ds BEGIN IF OBJECT_ID('DockerTest', 'U') IS NOT NULL DROP TABLE DockerTest; END;
                CREATE TABLE ds.DockerTest (Val INT);
                INSERT INTO ds.DockerTest (Val) VALUES (1);
                SELECT Val FROM ds.DockerTest;
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                var script = Parse(sql);
                await evaluator.Evaluate(script);

                var res = evaluator.LastResult;
                Assert.NotNull(res);
                Assert.Single(res.Rows);
                Assert.Equal(1, Convert.ToInt32(res.Rows[0]["VAL"]));
            }
            finally
            {
                await evaluator.DisposeAsync();
            }
        }

        [Fact]
        public async Task TestPostgresDockerConnection()
        {
            var sql = @"
                USE DOCKER('postgres:15-alpine');
                DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
                CREATE CONNECTION ds AS POSTGRES(@conn);
                CREATE TABLE ds.DockerTest (Val INT);
                INSERT INTO ds.DockerTest (Val) VALUES (1);
                SELECT Val FROM ds.DockerTest;
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                var script = Parse(sql);
                await evaluator.Evaluate(script);

                var res = evaluator.LastResult;
                Assert.NotNull(res);
                Assert.Single(res.Rows);
                Assert.Equal(1, Convert.ToInt32(res.Rows[0]["VAL"]));
            }
            finally
            {
                await evaluator.DisposeAsync();
            }
        }

        [Fact]
        public async Task TestOracleDockerConnection()
        {
            var sqlPrefix = @"
                USE DOCKER('gvenzl/oracle-free:latest');
                DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                // Initialize the container and variable once
                await evaluator.Evaluate(Parse(sqlPrefix));

                // We'll try to connect a few times because Oracle start is slow
                int retries = 5;
                Exception? lastEx = null;
                while (retries-- > 0)
                {
                    try
                    {
                        var script = Parse("CREATE CONNECTION ds AS ORACLE(@conn);");
                        await evaluator.Evaluate(script);

                        var connStr = evaluator.GetVariable("@conn")?.ToString();
                        AnsiConsole.MarkupLine($"      [grey]Oracle Conn String: {connStr}[/]");

                        // Run the actual test commands
                        var testSql = @"
                            CREATE TABLE ds.DockerTest (Val INT);
                            INSERT INTO ds.DockerTest (Val) VALUES (1);
                            SELECT Val FROM ds.DockerTest;
                        ";
                        var testScript = Parse(testSql);
                        await evaluator.Evaluate(testScript);

                        var res = evaluator.LastResult;
                        Assert.NotNull(res);
                        Assert.Single(res.Rows);
                        Assert.Equal(1, Convert.ToInt32(res.Rows[0]["VAL"]));

                        return; // Success
                    }
                    catch (Exception e)
                    {
                        lastEx = e;
                        AnsiConsole.MarkupLine($"      [yellow]Oracle connection attempt failed, retrying... ({retries} left)[/] [grey]{e.Message}[/]");
                        await Task.Delay(5000);
                        // We need to keep the container running between retries
                    }
                }
                if (lastEx != null) throw lastEx;
                else Assert.Fail("Oracle Docker connection failed after retries");
            }
            finally
            {
                await evaluator.DisposeAsync();
            }
        }

        [Fact]
        public async Task TestVariableConnectionTarget()
        {
            var sql = @"
                DECLARE @path varchar(200) = 'test_var.csv';
                CREATE CONNECTION ds AS FLATFILE(@path);
                SELECT 100 AS ID, 'VarTest' AS Name INTO #t;
                INSERT INTO ds SELECT * FROM #t;
                CREATE CONNECTION ds2 AS FLATFILE(@path);
                SELECT * FROM ds2;
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                var script = Parse(sql);
                await evaluator.Evaluate(script);

                var res = evaluator.LastResult;
                Assert.NotNull(res);
                Assert.Single(res.Rows);
                Assert.Equal(100, Convert.ToInt32(res.Rows[0]["ID"]));
            }
            finally
            {
                if (System.IO.File.Exists("test_var.csv")) System.IO.File.Delete("test_var.csv");
                await evaluator.DisposeAsync();
            }
        }

        [Fact]
        public async Task TestExecuteBeginEnd()
        {
            var sql = @"USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
CREATE CONNECTION ds AS MSSQL(@conn);
EXECUTE ds
BEGIN
  IF OBJECT_ID('Employee', 'U') IS NOT NULL DROP TABLE Employee;
  CREATE TABLE Employee (id int, employee_name varchar(500));
  INSERT INTO  Employee(id, employee_name) VALUES (1, 'New');
END

SELECT 1";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                var script = Parse(sql);
                await evaluator.Evaluate(script);

                var res = evaluator.LastResult;
                Assert.NotNull(res);
                Assert.Single(res.Rows);
                Assert.Equal(1, Convert.ToInt32(res.Rows.First().Columns.First().Value));
            }
            finally
            {
                await evaluator.DisposeAsync();
            }
        }

        [Fact]
        public async Task TestExecuteStringLiteral()
        {
            var sql = @"USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
CREATE CONNECTION ds AS MSSQL(@conn);
EXECUTE (
  'IF OBJECT_ID(''Employee'', ''U'') IS NOT NULL DROP TABLE Employee;
  CREATE TABLE Employee (id INT, employee_name NVARCHAR(MAX));
  INSERT INTO  Employee(id, employee_name) VALUES (1, ''New'');'
) AT ds

SELECT 1";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                var script = Parse(sql);
                await evaluator.Evaluate(script);

                var res = evaluator.LastResult;
                Assert.NotNull(res);
                Assert.Single(res.Rows);
                Assert.Equal(1, Convert.ToInt32(res.Rows.First().Columns.First().Value));
            }
            finally
            {
                await evaluator.DisposeAsync();
            }
        }

        [Fact]
        public async Task TestMultipleDockerContainersWithAliases()
        {
            var sql = @"
                USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS dms;
                DECLARE @conn varchar(500) = dms.CONNECTION_STRING;

                USE DOCKER('postgres:15-alpine') AS dpost;
                DECLARE @post_conn varchar(500) = dpost.CONNECTION_STRING;

                SELECT 1;

                CLOSE DOCKER dms;
                CLOSE DOCKER dpost;
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            try
            {
                var script = Parse(sql);
                await evaluator.Evaluate(script);

                var res = evaluator.LastResult;
                Assert.NotNull(res);
                Assert.Single(res.Rows);
                Assert.Equal(1, Convert.ToInt32(res.Rows.First().Columns.First().Value));
            }
            finally
            {
                await evaluator.DisposeAsync();
            }
        }
    }
}
