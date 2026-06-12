using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class IndexTests
    {

        private static async Task Execute(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            await eval.Evaluate(script);
        }

        private static async Task<List<DataTable>> Query(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            return await eval.ExecuteQuery(script.Statements.Last()).ToListAsync();
        }

        [Fact]
        public async Task TestBasicIndex()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Execute(eval, @"
                CREATE TABLE #data (ID INT, Name VARCHAR(50));
                INSERT INTO #data VALUES (1, 'Alice');
                INSERT INTO #data VALUES (2, 'Bob');
                INSERT INTO #data VALUES (3, 'Charlie');
                
                CREATE INDEX idx_name ON #data (Name);
            ");

            var results = await Query(eval, "SELECT * FROM #data WHERE Name = 'Bob'");
            Assert.Single(results);
            Assert.Single(results[0].Rows);
            Assert.Equal("Bob", results[0].Rows[0]["Name"]?.ToString());
        }

        [Fact]
        public async Task TestUniqueIndex()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Execute(eval, "CREATE TABLE #uniq (ID INT);");
            await Execute(eval, "INSERT INTO #uniq VALUES (1);");
            await Execute(eval, "CREATE UNIQUE INDEX idx_uniq ON #uniq (ID);");

            await Assert.ThrowsAsync<ExecutionException>(async () => await Execute(eval, "INSERT INTO #uniq VALUES (1);"));
        }

        [Fact]
        public async Task TestIndexJoin()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Execute(eval, @"
                CREATE TABLE #users (UserID INT, UserName VARCHAR(50));
                CREATE TABLE #orders (OrderID INT, UserID INT);
                
                INSERT INTO #users VALUES (1, 'Alice'), (2, 'Bob');
                INSERT INTO #orders VALUES (101, 1), (102, 1), (103, 2);
                
                CREATE INDEX idx_user_id ON #orders (UserID);
                
                SELECT u.UserName AS UserName, o.OrderID AS OrderID
                INTO #joined
                FROM #users u
                JOIN #orders o ON u.UserID = o.UserID;
            ");

            var results = await Query(eval, "SELECT * FROM #joined ORDER BY OrderID");
            var rows = results[0].Rows;

            Assert.Equal(3, rows.Count);

            var aliceOrders = rows.Where(r => r["USERNAME"]?.ToString() == "Alice").Select(r => Convert.ToInt32(r["ORDERID"])).ToList();
            var bobOrders = rows.Where(r => r["USERNAME"]?.ToString() == "Bob").Select(r => Convert.ToInt32(r["ORDERID"])).ToList();

            Assert.Equal(2, aliceOrders.Count);
            Assert.Contains(101, aliceOrders);
            Assert.Contains(102, aliceOrders);

            Assert.Single(bobOrders);
            Assert.Contains(103, bobOrders);
        }

        [Fact]
        public async Task TestExplain()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Execute(eval, @"
                CREATE TABLE #t1 (A INT);
                CREATE TABLE #t2 (B INT);
                CREATE INDEX idx_b ON #t2 (B);
            ");

            await Execute(eval, "EXPLAIN SELECT * FROM #t1 JOIN #t2 ON #t1.A = #t2.B");
        }
    }
}
