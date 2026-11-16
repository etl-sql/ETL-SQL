using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    /// <summary>
    /// LIKE with a NULL operand is UNKNOWN. The engine returned false, so NOT LIKE turned it into
    /// true and kept the NULL rows a database (or a pushed-down query) filters out.
    /// </summary>
    [Trait("CompatBreak", "0.22.0")]
    public class NullLikeSemanticsTests
    {
        private static async Task<int[]> Ids(string where)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await TestHelpers.Execute(ev, "CREATE TABLE #people (id INT, name VARCHAR);");
            await TestHelpers.Execute(ev, "INSERT INTO #people VALUES (1, 'Alice'), (2, NULL), (3, 'Bob');");
            var result = await ev.ExecuteQuery(
                TestHelpers.Parse($"SELECT id FROM #people WHERE {where} ORDER BY id;").Statements[0]).FirstAsync();
            return result.Rows.Select(row => Convert.ToInt32(row[0])).ToArray();
        }

        [Fact]
        public async Task NotLike_DropsNullRows() =>
            Assert.Equal(new[] { 3 }, await Ids("name NOT LIKE 'A%'"));

        [Fact]
        public async Task Like_DropsNullRows_EvenForMatchAll() =>
            Assert.Equal(new[] { 1, 3 }, await Ids("name LIKE '%'"));

        [Fact]
        public async Task NegatedLike_DropsNullRows() =>
            Assert.Equal(new[] { 3 }, await Ids("NOT (name LIKE 'A%')"));

        [Fact]
        public async Task NullPattern_MatchesNothing()
        {
            Assert.Empty(await Ids("name LIKE NULL"));
            Assert.Empty(await Ids("name NOT LIKE NULL"));
        }
    }
}
