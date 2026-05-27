using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests.Statements
{
    public class QueryTests
    {
        [Fact]
        [Trait("Category", "Smoke.Core")]
        public async Task TestStandaloneSelect()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var res = await ev.ExecuteQuery(Parse("SELECT 100 AS Val;").Statements[0]).FirstAsync();
            Assert.Equal(100m, res.Rows[0]["Val"]);
        }

        [Fact]
        public async Task TestCsvAdvancedOptions()
        {
            string tempCsv = "query_test_adv.csv";
            File.WriteAllText(tempCsv, "SKIP1\nSKIP2\nID,Name\n1,One\n2,Two\n3,Three\nFooter: 3");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse($"CREATE CONNECTION c AS FLATFILE('{tempCsv}', START_AT=2, COUNT_AT_END='Footer: COUNT');"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM c;").Statements[0]).FirstAsync();
            Assert.Equal(3, res.Rows.Count);
            File.Delete(tempCsv);
        }

        [Fact]
        public async Task TestCsvHeaderOption()
        {
            string dataFile = Path.Combine(Directory.GetCurrentDirectory(), "header_data_q.csv");
            string headerFile = Path.Combine(Directory.GetCurrentDirectory(), "header_ext_q.txt");
            File.WriteAllText(dataFile, "JUNK_H1,JUNK_H2\n1,One");
            File.WriteAllText(headerFile, "ExtID,ExtName");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse($"CREATE CONNECTION c AS FLATFILE('{dataFile.Replace("\\", "/")}', HEADER='{headerFile.Replace("\\", "/")}');"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM c;").Statements[0]).FirstAsync();
            Assert.True(res.ColumnNames.Contains("ExtID", StringComparer.OrdinalIgnoreCase), $"External header failed. Found columns: {string.Join(", ", res.ColumnNames)}");
            File.Delete(dataFile);
            File.Delete(headerFile);
        }

        [Fact]
        [Trait("Category", "Smoke.Core")]
        public async Task TestQualifiedSelect()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE T1 (ID INT, Name STRING);"));
            await ev.Evaluate(Parse("INSERT INTO T1 (ID, Name) VALUES (1, 'Smith');"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT T1.Name AS Name FROM T1 WHERE T1.ID = 1;").Statements[0]).FirstAsync();
            Assert.Equal("Smith", res.Rows[0]["Name"]?.ToString());
        }

        [Fact]
        public async Task TestCteBasic()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = "WITH Cte AS (SELECT 1 AS ID) SELECT * FROM Cte;";
            var res = await ev.ExecuteQuery(Parse(script).Statements[0]).FirstAsync();
            Assert.Equal(1m, Convert.ToDecimal(res.Rows[0]["ID"]));
        }

        [Fact]
        [Trait("Category", "Smoke.Core")]
        public async Task TestJoins()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #A (ID INT); INSERT INTO #A (ID) VALUES (1);"));
            await ev.Evaluate(Parse("CREATE TABLE #B (ID INT); INSERT INTO #B (ID) VALUES (1); INSERT INTO #B (ID) VALUES (2);"));
            
            // LEFT JOIN
            var resLeft = await ev.ExecuteQuery(Parse("SELECT #A.ID, #B.ID AS BID FROM #A LEFT JOIN #B ON #A.ID = #B.ID;").Statements[0]).FirstAsync();
            Assert.Single(resLeft.Rows);
            
            // RIGHT JOIN (Now supported)
            var resRight = await ev.ExecuteQuery(Parse("SELECT #A.ID, #B.ID AS BID FROM #A RIGHT JOIN #B ON #A.ID = #B.ID;").Statements[0]).FirstAsync();
            Assert.Equal(2, resRight.Rows.Count);
        }

        [Fact]
        [Trait("Category", "Smoke.Core")]
        public async Task TestWindowFunctions()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #W (V INT); INSERT INTO #W (V) VALUES (10); INSERT INTO #W (V) VALUES (20);"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT V, ROW_NUMBER() OVER(ORDER BY V) AS RN FROM #W;").Statements[0]).FirstAsync();
            Assert.Equal(1m, res.Rows[0]["RN"]);
            Assert.Equal(2m, res.Rows[1]["RN"]);
        }

        [Fact]
        [Trait("Category", "Smoke.Core")]
        public async Task TestExistsSubquery()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T1 (ID INT, Name STRING); INSERT INTO #T1 VALUES (1, 'Alice'), (2, 'Bob');"));
            await ev.Evaluate(Parse("CREATE TABLE #T2 (PID INT); INSERT INTO #T2 VALUES (1);"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT Name FROM #T1 WHERE EXISTS (SELECT 1 FROM #T2 WHERE #T2.PID = #T1.ID);").Statements[0]).FirstAsync();
            Assert.Single(res.Rows);
            Assert.Equal("Alice", res.Rows[0]["Name"]?.ToString());
        }

        [Fact]
        public async Task TestInSubquery()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T1 (ID INT, Name STRING); INSERT INTO #T1 VALUES (1, 'Alice'), (2, 'Bob');"));
            await ev.Evaluate(Parse("CREATE TABLE #T2 (PID INT); INSERT INTO #T2 VALUES (1);"));
            
            var res = await ev.ExecuteQuery(Parse("SELECT Name FROM #T1 WHERE ID IN (SELECT PID FROM #T2);").Statements[0]).FirstAsync();
            Assert.Single(res.Rows);
            Assert.Equal("Alice", res.Rows[0]["Name"]?.ToString());
        }

        [Fact]
        public async Task TestSemiAntiJoin()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T1 (ID INT); INSERT INTO #T1 VALUES (1), (2), (3);"));
            await ev.Evaluate(Parse("CREATE TABLE #T2 (ID INT); INSERT INTO #T2 VALUES (1), (2);"));
            
            // SEMI JOIN
            var resSemi = await ev.ExecuteQuery(Parse("SELECT #T1.ID AS ID FROM #T1 SEMI JOIN #T2 ON #T1.ID = #T2.ID;").Statements[0]).FirstAsync();
            Assert.Equal(2, resSemi.Rows.Count);
            
            // ANTI JOIN
            var resAnti = await ev.ExecuteQuery(Parse("SELECT #T1.ID AS ID FROM #T1 ANTI JOIN #T2 ON #T1.ID = #T2.ID;").Statements[0]).FirstAsync();
            Assert.Single(resAnti.Rows);
            Assert.Equal(3m, resAnti.Rows[0]["ID"]);
        }

        [Fact]
        public async Task TestTransactions()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.AutoRollbackOnFinish = false; // Allow transaction state to persist across Execute calls for this test
            await Execute(eval, "CREATE TABLE #t (ID INT);");
            
            // 1. Rollback test
            await Execute(eval, "BEGIN TRANSACTION;");
            await Execute(eval, "INSERT INTO #t (ID) VALUES (1);");
            var res1 = await eval.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(1, Convert.ToInt32(res1.Rows[0]["C"]));
            
            await Execute(eval, "ROLLBACK TRANSACTION;");
            var res2 = await eval.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(0, Convert.ToInt32(res2.Rows[0]["C"]));

            // 2. Commit test
            await Execute(eval, "BEGIN TRANSACTION;");
            await Execute(eval, "INSERT INTO #t (ID) VALUES (2);");
            await Execute(eval, "COMMIT TRANSACTION;");
            var res3 = await eval.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #t;").Statements[0]).FirstAsync();
            Assert.Equal(1, Convert.ToInt32(res3.Rows[0]["C"]));

            // 3. @@TRANCOUNT test
            await Execute(eval, "BEGIN TRANSACTION;");
            await Execute(eval, "BEGIN TRANSACTION;");
            Assert.Equal(2, Convert.ToInt32(eval.GetVariable("@@TRANCOUNT")));
            
            await Execute(eval, "ROLLBACK;");
            Assert.Equal(0, Convert.ToInt32(eval.GetVariable("@@TRANCOUNT")));
        }

        private static async Task Execute(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            await eval.Evaluate(script);
        }

        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }

        
    }
}
