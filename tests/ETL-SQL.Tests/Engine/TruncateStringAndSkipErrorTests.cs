using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class TruncateStringAndSkipErrorTests
    {
        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task Run(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
        }

        private static async Task<Evaluator> RunAndGetEval(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        [Fact]
        public async Task TruncateString_Default_FailsFast()
        {
            var sql = @"
                CREATE TABLE #T (Val VARCHAR(5));
                INSERT INTO #T VALUES ('ABCDEF');
            ";
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(sql));
            Assert.Contains("trying to insert a string with length 6 into a 5 character column", ex.Message);
        }

        [Fact]
        public async Task TruncateString_On_TruncatesValue()
        {
            var sql = @"
                SET TRUNCATE_STRING = ON;
                CREATE TABLE #T (Val VARCHAR(5));
                INSERT INTO #T VALUES ('ABCDEF');
                SELECT Val FROM #T;
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal("ABCDE", result.Rows[0]["Val"]);
        }

        [Fact]
        public async Task SkipError_Default_FailsFast()
        {
            var sql = @"
                CREATE TABLE #T (Val INT);
                INSERT INTO #T VALUES ('abc');
            ";
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(sql));
            Assert.Contains("cannot be converted to target type INT", ex.Message);
        }

        [Fact]
        public async Task SkipError_On_CoercesToNull()
        {
            var sql = @"
                SET SKIP_ERROR = ON;
                CREATE TABLE #T (Val INT);
                INSERT INTO #T VALUES ('abc');
                SELECT Val FROM #T;
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.True(result.Rows[0]["Val"] == null || result.Rows[0]["Val"] == DBNull.Value);
        }

        [Fact]
        public async Task SkipError_On_DuplicatePrimaryKey_SkipsRow()
        {
            var sql = @"
                CREATE TABLE #T (Id INT PRIMARY KEY, Val VARCHAR(10));
                INSERT INTO #T VALUES (1, 'First');
                SET SKIP_ERROR = ON;
                INSERT INTO #T VALUES (1, 'Second');
                SELECT COUNT(*) AS N FROM #T;
            ";
            var eval = await RunAndGetEval(sql);
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal(1m, result.Rows[0]["N"]);
        }

        [Fact]
        public async Task AggregatedDiagnostics_CollectsMultipleErrors()
        {
            var sql = @"
                CREATE TABLE #T (ColA VARCHAR(2), ColB INT);
                INSERT INTO #T VALUES ('abc', 'not-a-number');
            ";
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(sql));
            Assert.Contains("ColA", ex.Message);
            Assert.Contains("ColB", ex.Message);
            Assert.Contains("trying to insert a string with length 3 into a 2 character column", ex.Message);
            Assert.Contains("cannot be converted to target type INT", ex.Message);
        }
    }
}
