using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class JoinHintsTest
    {
        [Fact]
        public async Task TestJoinAlgorithmHints()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();

            string setupSql = @"
                CREATE TABLE #SmallTable (ID INT, Name VARCHAR(50));
                INSERT INTO #SmallTable VALUES (1, 'Alice');
                INSERT INTO #SmallTable VALUES (2, 'Bob');

                CREATE TABLE #LargeTable (ID INT, Value VARCHAR(50));
                INSERT INTO #LargeTable VALUES (1, 'V1');
                INSERT INTO #LargeTable VALUES (2, 'V2');
                INSERT INTO #LargeTable VALUES (3, 'V3');
                INSERT INTO #LargeTable VALUES (4, 'V4');
            ";
            await evaluator.Evaluate(new Parser(new Lexer(setupSql).Tokenize()).Parse());

            // Inner Loop Join
            string sqlLoop = "SELECT s.Name, l.Value FROM #SmallTable s INNER LOOP JOIN #LargeTable l ON s.ID = l.ID;";
            await evaluator.Evaluate(new Parser(new Lexer(sqlLoop).Tokenize()).Parse());
            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(2, evaluator.LastResult.Rows.Count);

            // Inner Hash Join
            string sqlHash = "SELECT s.Name, l.Value FROM #SmallTable s INNER HASH JOIN #LargeTable l ON s.ID = l.ID;";
            await evaluator.Evaluate(new Parser(new Lexer(sqlHash).Tokenize()).Parse());
            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(2, evaluator.LastResult.Rows.Count);

            // Left Hash Join
            string sqlLeftHash = "SELECT s.Name, l.Value FROM #SmallTable s LEFT HASH JOIN #LargeTable l ON s.ID = l.ID;";
            await evaluator.Evaluate(new Parser(new Lexer(sqlLeftHash).Tokenize()).Parse());
            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(2, evaluator.LastResult.Rows.Count);

            // Insert unmapped record and test Left Hash Join again
            await evaluator.Evaluate(new Parser(new Lexer("INSERT INTO #SmallTable VALUES (5, 'Charlie');").Tokenize()).Parse());
            await evaluator.Evaluate(new Parser(new Lexer(sqlLeftHash).Tokenize()).Parse());
            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(3, evaluator.LastResult.Rows.Count);
        }
    }
}
