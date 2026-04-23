using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;
using Spectre.Console;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Integration
{
    public class RemoteExecuteTests
    {

        [Fact]
        public async Task TestExecuteBlockAtConnection()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["mock"] = mockDb;

            var script = @"
EXECUTE (
    CREATE TABLE t_block(a INT);
    INSERT INTO t_block(a) VALUES(100);
) AT mock";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.True(mockDb.ExecutedSql.Any(s => s.Contains("CREATE TABLE t_block")), "Mock DB should have received CREATE TABLE from block");
            Assert.True(mockDb.ExecutedSql.Any(s => s.Contains("INSERT INTO t_block")), "Mock DB should have received INSERT INTO from block");
        }

        [Fact]
        public async Task TestLocalPushdownCheck()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["mock"] = mockDb;

            // SELECT 1 should NOT be pushed down even if mock exists, 
            // because it has no FROM mock.
            var script = "SELECT 1;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.True(mockDb.ExecutedSql.Count == 0, "Mock DB should NOT have received any SQL for local SELECT 1");
        }

        [Fact]
        public async Task TestExecuteAtConnection()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["mock"] = mockDb;

            var script = "EXEC ('CREATE TABLE remote_t(id INT)') AT mock;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.True(mockDb.ExecutedSql.Any(s => s.Contains("CREATE TABLE remote_t")), "Mock DB should have received remote SQL");
        }

        [Fact]
        public async Task TestExecuteRemoteBlock()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["mock"] = mockDb;

            var script = @"
EXECUTE mock
BEGIN
    CREATE TABLE t1(a INT);
    INSERT INTO t1(a) VALUES(1);
END";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.True(mockDb.ExecutedSql.Any(s => s.Contains("CREATE TABLE t1")), "Mock DB should have received CREATE TABLE");
            Assert.True(mockDb.ExecutedSql.Any(s => s.Contains("INSERT INTO t1")), "Mock DB should have received INSERT INTO");
        }

        [Fact]
        public async Task TestSingleRemoteExecute()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockDatabaseSource();
            evaluator.Connections["mock"] = mockDb;

            var script = "EXECUTE mock CREATE TABLE t2(a INT);";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.True(mockDb.ExecutedSql.Any(s => s.Contains("CREATE TABLE t2")), "Mock DB should have received single remote command");
        }

        [Fact]
        public async Task TestSelect1Dual()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = "SELECT 1;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.True(evaluator.LastResult != null, "LastResult should not be null for SELECT 1");
            Assert.True(evaluator.LastResult.Rows.Count == 1, "SELECT 1 should return exactly 1 row");
            
            var val = evaluator.LastResult.Rows[0].Columns.Values.First();
            Assert.True(val != null && val.ToString() == "1", $"Expected 1, got {val}");
        }

        [Fact]
        public async Task TestExecuteAtConnectionIntoTemp()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            evaluator.Connections["mock"] = mockDb;

            // EXECUTE (@stmt) AT connection INTO #temp — VS Code bug #5
            var script = @"
DECLARE @stmt VARCHAR(500) = 'SELECT * FROM Employee';
EXECUTE (@stmt) AT mock INTO #emp;
SELECT * FROM #emp;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            Assert.True(evaluator.LastResult.Rows.Count > 0, "INTO #emp should have received rows from remote execution");
            Assert.True(evaluator.Connections.ContainsKey("#emp"), "Temp table #emp should exist after EXECUTE ... INTO");
        }

        [Fact]
        public async Task TestExecuteAtConnectionIntoExistingTemp()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var mockDb = new MockSqlDataSource(SystemExecutionContext.Instance, "mock://", "MSSQL");
            evaluator.Connections["mock"] = mockDb;

            // Pre-create the temp table, then load into it
            var script = @"
CREATE TABLE #emp (ID INT, Name VARCHAR(100));
DECLARE @stmt VARCHAR(500) = 'SELECT * FROM Employee';
EXECUTE (@stmt) AT mock INTO #emp;
SELECT COUNT(*) AS cnt FROM #emp;";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            var cnt = Convert.ToInt32(evaluator.LastResult.Rows[0].Columns.Values.First());
            Assert.True(cnt > 0, "Pre-created #emp should have rows after EXECUTE ... INTO");
        }

        [Fact]
        public async Task TestDockerCloseSyntax()
        {
            // Legacy syntax (still supported via DOCKER fallback)
            var script = "DOCKER CLOSE 'mssql';";
            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();
            
            Assert.True(ast.Statements[0] is DockerActionStatement, "Should parse as DockerActionStatement (legacy)");
            var dcs = (DockerActionStatement)ast.Statements[0];
            Assert.Equal(DockerAction.Close, dcs.Action);

            // New standard syntax
            script = "CLOSE DOCKER 'mssql';";
            parser = new Parser(new Lexer(script).Tokenize());
            ast = parser.Parse();
            Assert.True(ast.Statements[0] is DockerActionStatement, "Should parse as DockerActionStatement (new)");
            
            script = "CLOSE DOCKER;";
            parser = new Parser(new Lexer(script).Tokenize());
            ast = parser.Parse();
            Assert.True(ast.Statements[0] is DockerActionStatement, "Should parse as DockerActionStatement (empty)");
            Assert.Equal(DockerTargetMode.LastStarted, ((DockerActionStatement)ast.Statements[0]).TargetMode);
            
            await Task.CompletedTask;
        }

        
    }

}
