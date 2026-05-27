using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Statements
{
    public class WhatIfTests
    {
        [Fact]
        public async Task TestWhatIfInsertSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // 1. Setup table
            await ev.Evaluate(Parse("CREATE TABLE #WhatIfTest (ID INT);"));
            
            // 2. Enable WHAT_IF
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            Assert.True(ev.IsWhatIf);

            // 3. Attempt INSERT
            await ev.Evaluate(Parse("INSERT INTO #WhatIfTest (ID) VALUES (1);"));
            
            // 4. Verify no rows
            var res = await ev.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #WhatIfTest;").Statements[0]).FirstAsync();
            Assert.Equal(0, Convert.ToInt32(res.Rows[0]["C"]));

            // 5. Disable WHAT_IF
            await ev.Evaluate(Parse("SET WHAT_IF OFF;"));
            Assert.False(ev.IsWhatIf);

            // 6. Attempt INSERT again
            await ev.Evaluate(Parse("INSERT INTO #WhatIfTest (ID) VALUES (1);"));
            
            // 7. Verify row exists
            var res2 = await ev.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #WhatIfTest;").Statements[0]).FirstAsync();
            Assert.Equal(1, Convert.ToInt32(res2.Rows[0]["C"]));
        }

        [Fact]
        public async Task TestWhatIfUpdateDeleteSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await ev.Evaluate(Parse("CREATE TABLE #T (ID INT); INSERT INTO #T VALUES (1);"));
            
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            
            // Attempt UPDATE
            await ev.Evaluate(Parse("UPDATE #T SET ID = 2;"));
            var resUpdate = await ev.ExecuteQuery(Parse("SELECT ID FROM #T;").Statements[0]).FirstAsync();
            Assert.Equal(1m, resUpdate.Rows[0]["ID"]);

            // Attempt DELETE
            await ev.Evaluate(Parse("DELETE FROM #T;"));
            var resDelete = await ev.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #T;").Statements[0]).FirstAsync();
            Assert.Equal(1, Convert.ToInt32(resDelete.Rows[0]["C"]));
        }

        [Fact]
        public async Task TestWhatIfMergeSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await ev.Evaluate(Parse("CREATE TABLE #Src (ID INT); INSERT INTO #Src VALUES (1);"));
            await ev.Evaluate(Parse("CREATE TABLE #Tgt (ID INT);"));
            
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            
            string mergeSql = @"
                MERGE INTO #Tgt AS T
                USING #Src AS S ON T.ID = S.ID
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (ID) VALUES (S.ID);";
            
            await ev.Evaluate(Parse(mergeSql));
            
            var res = await ev.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #Tgt;").Statements[0]).FirstAsync();
            Assert.Equal(0, Convert.ToInt32(res.Rows[0]["C"]));
        }

        [Fact]
        public async Task TestWhatIfForkPropagation()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            
            var forked = (Evaluator)ev.Fork();
            Assert.True(forked.IsWhatIf);
            
            await ev.Evaluate(Parse("SET WHAT_IF OFF;"));
            var forked2 = (Evaluator)ev.Fork();
            Assert.False(forked2.IsWhatIf);
        }

        [Fact]
        public async Task TestWhatIfConnectionSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            
            // Should not create the connection
            await ev.Evaluate(Parse("CREATE CONNECTION TestConn AS MOCKDB();"));
            Assert.False(ev.Connections.ContainsKey("TestConn"));
        }

        [Fact]
        public async Task TestWhatIfAlterTableSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (ID INT);"));
            
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            await ev.Evaluate(Parse("ALTER TABLE #T ADD Name VARCHAR(50);"));
            
            var cols = await (await ev.ResolveDataSourceAsync(new TableReference("#T"))).GetColumnsAsync();
            Assert.Single(cols);
            Assert.Equal("ID", cols.First());
        }

        [Fact]
        public async Task TestWhatIfDockerSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            
            // USE DOCKER should be suppressed
            await ev.Evaluate(Parse("USE DOCKER ('postgres:latest') AS db;"));
            Assert.Null(ev.DockerManager.GetConnectionString("db"));
        }

        [Fact]
        public async Task TestWhatIfExecuteNativeSuppression()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (ID INT);"));
            await ev.Evaluate(Parse("CREATE CONNECTION m AS MOCKDB();"));
            
            await ev.Evaluate(Parse("SET WHAT_IF ON;"));
            
            // This pushdown should be suppressed
            string sql = "EXECUTE m BEGIN INSERT INTO #T VALUES (1) END;";
            await ev.Evaluate(Parse(sql));
            
            var res = await ev.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #T;").Statements[0]).FirstAsync();
            Assert.Equal(0, Convert.ToInt32(res.Rows[0]["C"]));
        }

        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }
    }
}
