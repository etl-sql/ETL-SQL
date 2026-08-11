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

namespace ETL_SQL.Tests.Hardening
{
    public class ScaleCorrectnessSuite
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task Scale_Join_SpillToDisk_CorrectResults()
        {
            var ev = NewEvaluator();
            // Set thresholds lower to ensure spill-to-disk triggers with 10k rows for testing
            ev.JoinSpillThreshold = 5000;

            await TestHelpers.Execute(ev, "CREATE TABLE #t1 (id INT, val VARCHAR);");
            await TestHelpers.Execute(ev, "CREATE TABLE #t2 (id INT, score INT);");

            // 1. Insert 10,000 rows into #t1
            var dt1 = new DataTable();
            dt1.SetColumns(new List<string> { "id", "val" });
            for (int i = 1; i <= 10000; i++)
            {
                var r = new Row(dt1.Schema);
                r["id"] = i;
                r["val"] = $"Value_{i}";
                await dt1.AddRowAsync(r);
            }
            // Use Connections for temp tables (# prefix)
            await ev.Connections["#T1"].WriteBatches(AsyncEnumerable.ToAsyncEnumerable((IEnumerable<DataTable>)new[] { dt1 }));

            // 2. Insert 10,000 rows into #t2
            var dt2 = new DataTable();
            dt2.SetColumns(new List<string> { "id", "score" });
            for (int i = 1; i <= 10000; i++)
            {
                var r = new Row(dt2.Schema);
                r["id"] = i;
                r["score"] = i * 10;
                await dt2.AddRowAsync(r);
            }
            await ev.Connections["#T2"].WriteBatches(AsyncEnumerable.ToAsyncEnumerable((IEnumerable<DataTable>)new[] { dt2 }));

            // 3. Perform a JOIN that should spill to disk
            var res = await TestHelpers.ReadAllRows(
                ev.ExecuteQuery(TestHelpers.Parse("SELECT t1.id, t2.score FROM #t1 AS t1 JOIN #t2 AS t2 ON t1.id = t2.id ORDER BY t1.id;").Statements[0]));

            Assert.Equal(10000, res.Rows.Count);
            Assert.Equal(1, Convert.ToInt32(res.Rows[0][0]));
            Assert.Equal(10m, Convert.ToDecimal(res.Rows[0][1]));
            Assert.Equal(10000, Convert.ToInt32(res.Rows[9999][0]));
            Assert.Equal(100000m, Convert.ToDecimal(res.Rows[9999][1]));
        }

        [Fact]
        public async Task Scale_Aggregate_100kRows_CorrectResults()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (grp INT, val INT);");

            // Insert 100,000 rows: 10 groups of 10,000
            var dt = new DataTable();
            dt.SetColumns(new List<string> { "grp", "val" });
            for (int g = 1; g <= 10; g++)
            {
                for (int i = 0; i < 10000; i++)
                {
                    var r = new Row(dt.Schema);
                    r["grp"] = g;
                    r["val"] = 1;
                    await dt.AddRowAsync(r);
                }
            }
            await ev.Connections["#T"].WriteBatches(AsyncEnumerable.ToAsyncEnumerable((IEnumerable<DataTable>)new[] { dt }));

            var res = await TestHelpers.ReadAllRows(
                ev.ExecuteQuery(TestHelpers.Parse("SELECT grp, SUM(val) AS total FROM #t GROUP BY grp ORDER BY grp;").Statements[0]));

            Assert.Equal(10, res.Rows.Count);
            for (int j = 0; j < 10; j++)
            {
                Assert.Equal(j + 1, Convert.ToInt32(res.Rows[j][0]));
                Assert.Equal(10000m, Convert.ToDecimal(res.Rows[j][1]));
            }
        }
    }
}
