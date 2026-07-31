using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Proves the mechanic behind samples/admin_operations/capacity_report.etlsql: aggregate the
    /// host-utilization series (min free disk / peak memory per node) and job history (runs / failures
    /// per job) from the SHOW ... INTO temp tables. Runs fully in-process against the seeded stores.
    /// </summary>
    public class CapacityReportTemplateTests
    {
        [Fact]
        public async Task CapacityAggregations_OverShowIntoTables_Compute()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var hostStore = provider.GetRequiredService<IHostMetricsStore>();
            var jobStore = provider.GetRequiredService<IJobHistoryStore>();
            var eval = provider.GetRequiredService<Evaluator>();
            var node = "node-" + Guid.NewGuid().ToString("N")[..8];
            var job = "cap_" + Guid.NewGuid().ToString("N")[..8];

            // Two host samples for one node: min state free = 500 MiB.
            await hostStore.AppendHostMetricAsync(new HostMetricSample(node, DateTime.UtcNow, 40, 10, null, 1_073_741_824, 2_147_483_648)); // 1024 MiB
            await hostStore.AppendHostMetricAsync(new HostMetricSample(node, DateTime.UtcNow, 70, 30, null, 524_288_000, 2_147_483_648));   // 500 MiB

            // Two runs of a job: one FAILURE, one SUCCESS.
            var f = await jobStore.LogJobStartAsync(job);
            await jobStore.LogJobEndAsync(f, "FAILURE", "boom");
            var s = await jobStore.LogJobStartAsync(job);
            await jobStore.LogJobEndAsync(s, "SUCCESS");

            await TestHelpers.Execute(eval, $@"
SELECT * INTO #hm FROM eng.host_metrics WHERE node_id = '{node}';
SELECT node_id, MIN(state_disk_free_mb) AS min_state_free_mb, MIN(spill_disk_free_mb) AS min_spill_free_mb,
       MAX(memory_load_percent) AS peak_mem_pct, MAX(process_cpu_percent) AS peak_cpu_pct
INTO #hostsum FROM #hm GROUP BY node_id;
-- The template's exact host-line body expression.
DECLARE @hostLines = (
    SELECT STRING_AGG(
        CONCAT(node_id, ': state ', CAST(min_state_free_mb AS INT), ' MB free, spill ',
               CAST(min_spill_free_mb AS INT), ' MB free, peak mem ', CAST(peak_mem_pct AS INT),
               '%, peak cpu ', CAST(peak_cpu_pct AS INT), '%'),
        CHAR(10))
    FROM #hostsum);

SELECT * INTO #jh FROM eng.job_history WHERE job_name = '{job}';
DECLARE @runs = (SELECT COUNT(*) FROM #jh);
DECLARE @failures = (SELECT COUNT(*) FROM #jh WHERE status NOT IN ('SUCCESS', 'RUNNING'));");

            Assert.Contains($"{node}: state 500 MB free", eval.GetVariable("@hostLines")?.ToString() ?? "");
            Assert.Contains("peak mem 70%", eval.GetVariable("@hostLines")?.ToString() ?? "");
            Assert.Equal(2, Convert.ToInt32(eval.GetVariable("@runs")));
            Assert.Equal(1, Convert.ToInt32(eval.GetVariable("@failures")));
        }

        [Fact]
        public void ShippedTemplate_ParsesWithoutErrors()
        {
            var path = FindRepoFile("samples/admin_operations/capacity_report.etlsql");
            var text = File.ReadAllText(path);
            var script = new Parser(new Lexer(text).Tokenize(), text).Parse();
            var errors = script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                "Template has parse errors: " + string.Join("; ", errors.Select(e => e.Message)));
        }

        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"Could not locate {relative} above {AppContext.BaseDirectory}");
        }
    }
}
