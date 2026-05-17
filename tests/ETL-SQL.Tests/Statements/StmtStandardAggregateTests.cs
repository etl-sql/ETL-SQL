using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtStandardAggregateTests
    {
        [Fact]
        public async Task EveryAnyAndSomeAggregateBooleanExpressions()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT
                    EVERY(flag) AS all_flags,
                    ANY(flag) AS any_flag,
                    SOME(flag) AS some_flag
                FROM (VALUES (TRUE), (FALSE), (NULL)) AS v(flag);";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.False((bool)res.Rows[0]["all_flags"]!);
            Assert.True((bool)res.Rows[0]["any_flag"]!);
            Assert.True((bool)res.Rows[0]["some_flag"]!);
        }

        [Fact]
        public async Task EveryIgnoresNullsAndReturnsNullWhenNoNonNullInput()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var trueRes = await ev.ExecuteQuery(Parse("SELECT EVERY(flag) AS all_flags FROM (VALUES (TRUE), (NULL)) AS v(flag);").Statements[0]).FirstAsync();
            var nullRes = await ev.ExecuteQuery(Parse("SELECT EVERY(flag) AS all_flags FROM (VALUES (NULL)) AS v(flag);").Statements[0]).FirstAsync();

            Assert.True((bool)trueRes.Rows[0]["all_flags"]!);
            Assert.Null(nullRes.Rows[0]["all_flags"]);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
