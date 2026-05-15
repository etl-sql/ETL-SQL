using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Benchmarks;

namespace ETL_SQL.Benchmarks.Tests
{
    public class BenchSetupTest
    {
        [Fact]
        public async Task TestSetup()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            Assert.NotNull(bench);
        }

        [Fact]
        public async Task TestRunQ1()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ1();
            var result = bench.LastResult;
            Assert.NotNull(result);
            // Q1 groups by (l_returnflag, l_linestatus) — seeder produces R, A, N flags × F, O statuses → up to 6 groups
            Assert.True(result.Rows.Count > 0, "Q1 should return at least one pricing summary group");
            Assert.Contains(result.ColumnNames, c => c.Equals("sum_qty", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TestRunQ6()
        {
            var bench = new TpcHBenchmarks();
            await bench.Setup();
            await bench.RunQ6();
            var result = bench.LastResult;
            Assert.NotNull(result);
            Assert.True(result.Rows.Count > 0, "Q6 should return a revenue row (seeder covers 1994 date range)");
            Assert.Contains(result.ColumnNames, c => c.Equals("revenue", StringComparison.OrdinalIgnoreCase));
        }
    }
}
