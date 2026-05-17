using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtMatchRecognizeTests
    {
        private static Evaluator Build()
            => DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql)
            => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        [Fact]
        public async Task MatchRecognize_DetectsLinearPatternWithMeasures()
        {
            var ev = Build();
            var sql = @"
                SELECT start_ts, end_ts
                FROM (VALUES
                    ('acct1', 1, 10),
                    ('acct1', 2, 80),
                    ('acct1', 3, 90),
                    ('acct1', 4, 20)
                ) AS e(account_id, ts, amount)
                MATCH_RECOGNIZE (
                    PARTITION BY account_id
                    ORDER BY ts
                    MEASURES FIRST(A.ts) AS start_ts, LAST(B.ts) AS end_ts
                    ONE ROW PER MATCH
                    PATTERN (A B+)
                    DEFINE A AS A.amount < 50, B AS B.amount >= 80
                ) AS mr;";

            var batch = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Single(batch.Rows);
            Assert.Equal(1m, Convert.ToDecimal(batch.Rows[0]["start_ts"]));
            Assert.Equal(3m, Convert.ToDecimal(batch.Rows[0]["end_ts"]));
        }

        [Fact]
        public async Task MatchRecognize_AllRowsPerMatch_EmitsClassifier()
        {
            var ev = Build();
            var sql = @"
                SELECT MATCH_NUMBER, CLASSIFIER
                FROM (VALUES (1, 10), (2, 20)) AS e(ts, amount)
                MATCH_RECOGNIZE (
                    ORDER BY ts
                    ALL ROWS PER MATCH
                    PATTERN (A+)
                    DEFINE A AS A.amount >= 10
                ) AS mr
                ORDER BY MATCH_NUMBER, CLASSIFIER;";

            var batch = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(2, batch.Rows.Count);
            Assert.All(batch.Rows, row => Assert.Equal("A", row["CLASSIFIER"]?.ToString()));
            Assert.All(batch.Rows, row => Assert.Equal(1m, Convert.ToDecimal(row["MATCH_NUMBER"])));
        }
    }
}
