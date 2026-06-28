using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtMinorConveniencesTests
    {
        [Fact]
        public async Task LikeAny_MatchesAnyPattern()
        {
            var rows = await Run(@"
                SELECT x FROM (VALUES ('apple'), ('banana'), ('cherry')) AS t(x)
                WHERE x LIKE ANY ('a%', 'b%')
                ORDER BY x;");
            Assert.Equal(new[] { "apple", "banana" }, rows.Select(r => r["x"]?.ToString()).ToArray());
        }

        [Fact]
        public async Task LikeAll_MatchesEveryPattern()
        {
            var rows = await Run(@"
                SELECT x FROM (VALUES ('abc'), ('abx')) AS t(x)
                WHERE x LIKE ALL ('a%', '%c')
                ORDER BY x;");
            Assert.Equal(new[] { "abc" }, rows.Select(r => r["x"]?.ToString()).ToArray());
        }

        [Fact]
        public async Task NotLikeAny_NegatesTheGroup()
        {
            var rows = await Run(@"
                SELECT x FROM (VALUES ('apple'), ('grape')) AS t(x)
                WHERE x NOT LIKE ANY ('a%', 'b%')
                ORDER BY x;");
            Assert.Equal(new[] { "grape" }, rows.Select(r => r["x"]?.ToString()).ToArray());
        }

        [Fact]
        public async Task Describe_ListsColumnsLikeShowColumns()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #d (id INT, name NVARCHAR(50));"));
            await ev.Evaluate(Parse("DESCRIBE #d;"));

            Assert.NotNull(ev.LastResult);
            var colNames = ev.LastResult!.Rows.Select(r => r["ColumnName"]?.ToString()).ToList();
            Assert.Contains("id", colNames);
            Assert.Contains("name", colNames);
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
