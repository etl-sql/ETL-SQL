using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Services;
using Spectre.Console;

namespace ETL_SQL.Tests.Integration.Integration
{
    public class SessionPersistenceTests : IDisposable
    {
        private readonly string _sessionDir;
        private readonly string _sessionId;

        public SessionPersistenceTests()
        {
            _sessionDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sessionDir);
            _sessionId = "test-session-" + Guid.NewGuid().ToString("N");
        }

        public void Dispose()
        {
            if (Directory.Exists(_sessionDir))
            {
                try { Directory.Delete(_sessionDir, true); } catch { }
            }
        }

        async Task<(Evaluator, int)> RunSessionStep(string sql)
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = true;
            
            
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            var sessionManager = new SessionStateManager(NullLogger.Instance, security, config, _sessionDir);

            // 1. Load Session
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.IsPersistentSession = true;
            evaluator.SessionId = _sessionId;
            evaluator.SessionRoot = _sessionDir;
            
            var state = await sessionManager.LoadSession(_sessionId);
            if (state != null)
            {
                await evaluator.LoadSessionState(state);
            }
            
            // 2. Run Script
            var lexer = new Lexer(sql);
            var parser = new Parser(lexer.Tokenize(), sql);
            var script = parser.Parse();
            
            try 
            {
                await evaluator.Evaluate(script);
                
                // 3. Save Session
                await sessionManager.SaveSession(_sessionId, evaluator, sql);
                return (evaluator, 0);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]ERROR in RunSessionStep:[/] {ex.Message}");
                if (evaluator.LastError != null)
                {
                    AnsiConsole.MarkupLine($"[yellow]Evaluator Error {evaluator.LastError.Number}:[/] {evaluator.LastError.Message}");
                }
                return (evaluator, 1);
            }
        }

        [Fact]
        public async Task TestAdHocSessionPersistence_18Steps()
        {
            AnsiConsole.MarkupLine("[bold blue]Starting 18-Step Session Persistence Integration Test...[/]");

            // 1st run: Setup Docker & Connection
            AnsiConsole.MarkupLine("  - Step 1: Initialize Docker and Connection 'm'");
            var (eval1, code1) = await RunSessionStep(@"
                USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
                DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
                CREATE CONNECTION m ON MSSQL(@conn);
            ");
            Assert.Equal(0, code1);
            Assert.True(eval1.Connections.ContainsKey("m"));
            var connStr = eval1.GetVariable("@conn")?.ToString();
            Assert.NotNull(connStr);

            // 2nd run: Create remote table
            AnsiConsole.MarkupLine("  - Step 2: Create remote table dbo.SessionStateTestEmployee");
            var (eval2, code2) = await RunSessionStep(@"
                EXECUTE m
                BEGIN
                    IF OBJECT_ID('dbo.SessionStateTestEmployee', 'U') IS NOT NULL DROP TABLE dbo.SessionStateTestEmployee;
                    CREATE TABLE dbo.SessionStateTestEmployee (
                        id INT PRIMARY KEY,
                        [name] NVARCHAR(100)
                    );
                    INSERT INTO dbo.SessionStateTestEmployee (id, [name]) VALUES (1, 'Alice'), (2, 'Bob'), (3, 'John');   
                END
            ");
            Assert.Equal(0, code2);

            // 3rd run: SELECT INTO #emp (Pushdown with local INTO)
            AnsiConsole.MarkupLine("  - Step 3: Fast Pushdown SELECT INTO #emp");
            var (eval3, code3) = await RunSessionStep(@"
                EXECUTE m
                SELECT * INTO #emp FROM m.dbo.SessionStateTestEmployee;
            ");
            Assert.Equal(0, code3);
            Assert.True(eval3.Connections.ContainsKey("#emp"));

            // 4th run: Query #emp
            AnsiConsole.MarkupLine("  - Step 4: Verify #emp content");
            var (eval4, code4) = await RunSessionStep("SELECT * FROM #emp;");
            Assert.Equal(0, code4);
            Assert.Equal(3, eval4.LastResult?.Rows.Count);

            // 5th run: Query remote m.dbo.SessionStateTestEmployee
            AnsiConsole.MarkupLine("  - Step 5: Verify remote table content");
            var (eval5, code5) = await RunSessionStep("SELECT * FROM m.dbo.SessionStateTestEmployee;");
            Assert.Equal(0, code5);
            Assert.Equal(3, eval5.LastResult?.Rows.Count);

            // 6th run: Lineage check (id) - Now persisted!
            AnsiConsole.MarkupLine("  - Step 6: Verify lineage for #emp.id");
            var (eval6, code6) = await RunSessionStep("LINEAGE #emp;");
            Assert.Equal(0, code6);
            var lineageId = eval6.LineageTracker.GetLineage("#emp").ToList();
            Assert.NotEmpty(lineageId);
            Assert.Contains(lineageId, r => r.TargetTable == "#emp" && r.SourceTables.Contains("m.dbo.SessionStateTestEmployee"));

            // 7th run: Lineage check (name)
            AnsiConsole.MarkupLine("  - Step 7: Verify column lineage for #emp.name");
            var (eval7, code7) = await RunSessionStep("LINEAGE #emp;");
            Assert.Equal(0, code7);

            // 8th run: Another SELECT from #emp
            AnsiConsole.MarkupLine("  - Step 8: Re-verify #emp persistence");
            var (eval8, code8) = await RunSessionStep("SELECT * FROM #emp;");
            Assert.Equal(0, code8);
            Assert.Equal(3, eval8.LastResult?.Rows.Count);

            // 9th run: DOCKER CLOSE & DROP CONNECTION
            AnsiConsole.MarkupLine("  - Step 9: Close Docker and drop connection 'm'");
            var (eval9, code9) = await RunSessionStep(@"
                CLOSE DOCKER;
                DROP CONNECTION m;
            ");
            Assert.Equal(0, code9);
            await Task.Delay(1000); // Allow OS to release ports

            // 10th run: #emp should still survive!
            AnsiConsole.MarkupLine("  - Step 10: Verify #emp survives Docker closure");
            var (eval10, code10) = await RunSessionStep("SELECT * FROM #emp;");
            Assert.Equal(0, code10);
            Assert.Equal(3, eval10.LastResult?.Rows.Count);

            // 11th run: Fails gracefully (m gone)
            AnsiConsole.MarkupLine("  - Step 11: Verify connection 'm' is correctly marked as missing");
            var (eval11, code11) = await RunSessionStep("SELECT * FROM m.dbo.SessionStateTestEmployee;");
            Assert.Equal(1, code11); // Error expected

            // 12th run: Fails gracefully
            AnsiConsole.MarkupLine("  - Step 12: Verify connection failure to closed Docker");
            var (eval12, code12) = await RunSessionStep(@"
                CREATE CONNECTION m ON MSSQL(@conn);
                SELECT TOP 1 * FROM m.dbo.SessionStateTestEmployee;
            ");
            Assert.Equal(1, code12); // Should fail to connect

            // 13th run: Re-open Docker
            AnsiConsole.MarkupLine("  - Step 13: Re-initialize Docker container");
            var (eval13, code13) = await RunSessionStep(@"
                USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
                SET @conn = DOCKER.CONNECTION_STRING;
                CREATE CONNECTION m ON MSSQL(@conn);
            ");
            Assert.Equal(0, code13);

            // 14th run: Create new data in new container
            AnsiConsole.MarkupLine("  - Step 14: Populate new remote table");
            var (eval14, code14) = await RunSessionStep(@"
                EXECUTE m
                BEGIN
                    IF OBJECT_ID('dbo.SessionStateTestEmployee', 'U') IS NOT NULL DROP TABLE dbo.SessionStateTestEmployee;
                    CREATE TABLE dbo.SessionStateTestEmployee (
                        id INT PRIMARY KEY,
                        [name] NVARCHAR(100)
                    );
                    INSERT INTO dbo.SessionStateTestEmployee (id, [name]) VALUES (1, 'Mike'), (2, 'Steve'), (3, 'Angus');   
                END
            ");
            Assert.Equal(0, code14);

            // 15th run: #emp still has Alice (from first run's persistence)
            AnsiConsole.MarkupLine("  - Step 15: Verify #emp still holds old data (Alice)");
            var (eval15, code15) = await RunSessionStep("SELECT * FROM #emp WHERE id = 1;");
            Assert.Equal(0, code15);
            Assert.Equal("Alice", eval15.LastResult?.Rows[0]["NAME"]?.ToString());

            // 16th run: Update #emp from m and verify
            AnsiConsole.MarkupLine("  - Step 16: Refresh #emp from current remote data");
            var (eval16, code16) = await RunSessionStep(@"
                SELECT * INTO #emp FROM m.dbo.SessionStateTestEmployee;
                SELECT * FROM #emp WHERE id = 1;
            ");
            Assert.Equal(0, code16);
            Assert.Equal("Mike", eval16.LastResult?.Rows[0]["NAME"]?.ToString());

            // 17th run: Lineage check again
            AnsiConsole.MarkupLine("  - Step 17: Re-verify updated lineage");
            var (eval17, code17) = await RunSessionStep("LINEAGE #emp;");
            Assert.Equal(0, code17);

            // 18th run: Final cleanup
            AnsiConsole.MarkupLine("  - Step 18: Final Docker cleanup");
            var (eval18, code18) = await RunSessionStep(@"
                CLOSE DOCKER;
                DROP CONNECTION m;
            ");
            Assert.Equal(0, code18);

            AnsiConsole.MarkupLine("[bold green]18-Step Session Persistence Integration Test PASSED![/]");
        }
    }
}
