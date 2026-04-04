using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests
{
    public class DockerTests
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
                CREATE CONNECTION ds ON MSSQL(@conn);
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
                CREATE CONNECTION ds ON POSTGRES(@conn);
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
                // We'll try to connect a few times because Oracle start is slow
                int retries = 5;
                Exception? lastEx = null;
                while (retries-- > 0)
                {
                    try
                    {
                        var script = Parse(sqlPrefix + "CREATE CONNECTION ds ON ORACLE(@conn);");
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
                CREATE CONNECTION ds ON FLATFILE(@path);
                SELECT 100 AS ID, 'VarTest' AS Name INTO #t;
                INSERT INTO ds SELECT * FROM #t;
                CREATE CONNECTION ds2 ON FLATFILE(@path);
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
CREATE CONNECTION ds ON MSSQL(@conn);
EXECUTE ds
BEGIN
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
CREATE CONNECTION ds ON MSSQL(@conn);
EXECUTE (
  'CREATE TABLE Employee (id INT, employee_name NVARCHAR(MAX));
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

                CLOSE_DOCKER dms;
                CLOSE_DOCKER dpost;
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
