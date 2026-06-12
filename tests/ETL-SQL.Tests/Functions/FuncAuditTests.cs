using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    public class InconsistencyAuditSuite
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task Report_ParameterizedFilter_IsDeterministic()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #data (id INT, region VARCHAR);");
            await TestHelpers.Execute(ev, "INSERT INTO #data VALUES (1, 'North'), (2, 'South'), (3, 'East');");

            // Pattern common in .rptsql: WHERE @p = 'All' OR col = @p
            string query = "SELECT id FROM #data WHERE @p = 'All' OR region = @p ORDER BY id;";

            // 1. Test case: @p = 'All'
            ev.DeclareVariable("@p", "All");
            var resAll = await ev.ExecuteQuery(TestHelpers.Parse(query).Statements[0]).FirstAsync();
            Assert.Equal(3, resAll.Rows.Count);

            // 2. Test case: @p = 'North'
            ev.DeclareVariable("@p", "North");
            var resNorth = await ev.ExecuteQuery(TestHelpers.Parse(query).Statements[0]).FirstAsync();
            Assert.Single(resNorth.Rows);
            Assert.Equal(1, Convert.ToInt32(resNorth.Rows[0][0]));

            // 3. Test case: @p = 'West' (No matches)
            ev.DeclareVariable("@p", "West");
            var resWest = await ev.ExecuteQuery(TestHelpers.Parse(query).Statements[0]).FirstAsync();
            Assert.Empty(resWest.Rows);
        }

        [Fact]
        public async Task Report_NullParameter_DoesNotCrash_3VL()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #data (id INT, region VARCHAR);");
            await TestHelpers.Execute(ev, "INSERT INTO #data VALUES (1, 'North'), (2, NULL);");

            string query = "SELECT id FROM #data WHERE region = @p;";

            // If @p is NULL, region = @p is UNKNOWN. Should return 0 rows (even for the NULL row).
            ev.DeclareVariable("@p", null);
            var res = await ev.ExecuteQuery(TestHelpers.Parse(query).Statements[0]).FirstAsync();
            Assert.Empty(res.Rows);

            // Standard IS NULL should still work
            string queryNull = "SELECT id FROM #data WHERE region IS NULL;";
            var resNull = await ev.ExecuteQuery(TestHelpers.Parse(queryNull).Statements[0]).FirstAsync();
            Assert.Single(resNull.Rows);
            Assert.Equal(2, Convert.ToInt32(resNull.Rows[0][0]));
        }
    }
}
