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
    public class StmtColumnsExprTests
    {
        [Fact]
        public async Task ColumnsStar_ExpandsAll()
        {
            var b = await RunFirstBatch("SELECT COLUMNS(*) FROM (VALUES (1, 2, 3)) AS t(a, b, c);");
            Assert.Equal(new[] { "a", "b", "c" }, b.ColumnNames.ToArray());
        }

        [Fact]
        public async Task ColumnsStarExclude_DropsColumns()
        {
            var b = await RunFirstBatch("SELECT COLUMNS(* EXCLUDE (secret)) FROM (VALUES (1, 'a', 'x')) AS t(id, name, secret);");
            Assert.Equal(new[] { "id", "name" }, b.ColumnNames.ToArray());
        }

        [Fact]
        public async Task ColumnsRegex_SelectsMatchingColumns()
        {
            var b = await RunFirstBatch("SELECT COLUMNS('^a') FROM (VALUES (1, 2, 3)) AS t(amount, age, balance);");
            Assert.Equal(new[] { "amount", "age" }, b.ColumnNames.ToArray());
        }

        private static async Task<DataTable> RunFirstBatch(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches[0];
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
