using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// SHOW HOST METRICS [nodeId] [INTO #t] read surface over the host-utilization time series.
    /// Runs in-process: the same singleton store the handler reads is seeded directly.
    /// </summary>
    public class ShowHostMetricsTests
    {
        [Fact]
        public async Task ShowHostMetrics_Into_ExposesSamplesForFilteringAndReporting()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var store = provider.GetRequiredService<IHostMetricsStore>();
            var eval = provider.GetRequiredService<Evaluator>();
            var node = "node-" + Guid.NewGuid().ToString("N")[..8];

            // 1 GiB state free, 2 GiB spill free (schema auto-created on first append).
            await store.AppendHostMetricAsync(new HostMetricSample(
                node, DateTime.UtcNow, MemoryLoadPercent: 40, ProcessCpuPercent: 12,
                HostCpuPercent: null, StateDiskFreeBytes: 1_073_741_824, SpillDiskFreeBytes: 2_147_483_648));

            await TestHelpers.Execute(eval, $@"
SHOW HOST METRICS '{node}' INTO #hm;
DECLARE @count = (SELECT COUNT(*) FROM #hm);
DECLARE @stateFreeMb = (SELECT StateDiskFreeMB FROM #hm);");

            Assert.Equal(1, Convert.ToInt32(eval.GetVariable("@count")));
            // 1 GiB = 1024 MiB, reported in MB for readability.
            Assert.Equal(1024.0, Convert.ToDouble(eval.GetVariable("@stateFreeMb")), 1);
        }
    }
}
