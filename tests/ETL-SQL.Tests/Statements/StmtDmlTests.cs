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

namespace ETL_SQL.Tests.Statements
{
    public class DmlTests
    {
        [Fact]
        public async Task TestInsertValues()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T1 (ID INT, Name STRING);"));
            await ev.Evaluate(Parse("INSERT INTO #T1 (ID, Name) VALUES (1, 'A');"));

            var res = await ev.ExecuteQuery(Parse("SELECT * FROM #T1;").Statements[0]).FirstAsync();
            Assert.Single(res.Rows);
            Assert.Equal(1m, res.Rows[0]["ID"]);
        }

        [Fact]
        public async Task TestInsertSelect()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #S (Val DECIMAL);"));
            await ev.Evaluate(Parse("INSERT INTO #S (Val) VALUES (10);"));
            await ev.Evaluate(Parse("CREATE TABLE #D (Result DECIMAL);"));
            await ev.Evaluate(Parse("INSERT INTO #D (Result) SELECT Val * 2 FROM #S;"));

            var res = await ev.ExecuteQuery(Parse("SELECT * FROM #D;").Statements[0]).FirstAsync();
            Assert.Single(res.Rows);
            Assert.Equal(20m, res.Rows[0]["Result"]);
        }

        [Fact]
        public async Task TestUpdateWhere()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (V INT);"));
            await ev.Evaluate(Parse("INSERT INTO #T (V) VALUES (1);"));
            await ev.Evaluate(Parse("UPDATE #T SET V = 10 WHERE V = 1;"));

            var res = await ev.ExecuteQuery(Parse("SELECT V FROM #T;").Statements[0]).FirstAsync();
            Assert.Equal(10m, res.Rows[0]["V"]);
        }

        [Fact]
        public async Task TestDeleteWhere()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (V INT);"));
            await ev.Evaluate(Parse("INSERT INTO #T (V) VALUES (1);"));
            await ev.Evaluate(Parse("DELETE FROM #T WHERE V = 1;"));

            var res = await ev.ExecuteQuery(Parse("SELECT COUNT(*) AS C FROM #T;").Statements[0]).FirstAsync();
            Assert.Equal(0m, res.Rows[0]["C"]);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }


    }
}
