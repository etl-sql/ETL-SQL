using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Operations
{
    public class AliasTests
    {

        [Fact]
        public async Task TestFileAlias()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // Test primary name
            await eval.Evaluate(new Parser(new Lexer("CREATE CONNECTION TestFile1 AS FLATFILE('test1.csv');").Tokenize()).Parse());
            if (eval.Connections.TryGetValue("TestFile1", out var conn1))
            {
                Assert.NotNull(conn1);
            }
            else Assert.Fail("Failed to create connection with FILE");

            // Test alias
            await eval.Evaluate(new Parser(new Lexer("CREATE CONNECTION TestFile2 AS CSV('test2.csv');").Tokenize()).Parse());
            if (eval.Connections.TryGetValue("TestFile2", out var conn2))
            {
                Assert.NotNull(conn2);
            }
            else Assert.Fail("Failed to create connection with CSV alias");
        }

        [Fact]
        public async Task TestSqlServerAlias()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            // Test primary name
            await eval.Evaluate(new Parser(new Lexer("CREATE CONNECTION TestSql1 AS MSSQL('server=localhost');").Tokenize()).Parse());
            Assert.True(eval.Connections.ContainsKey("TestSql1"), "Failed to create connection with MSSQL");

            // Test alias
            await eval.Evaluate(new Parser(new Lexer("CREATE CONNECTION TestSql2 AS SQLSERVER('server=localhost');").Tokenize()).Parse());
            Assert.True(eval.Connections.ContainsKey("TestSql2"), "Failed to create connection with SQLSERVER alias");
        }
    }
}
