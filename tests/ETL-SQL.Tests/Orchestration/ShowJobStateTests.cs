using System;
using System.Linq;
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
    /// SHOW JOB STATE [jobName] [INTO #t] — the administrator's cross-job read surface over the
    /// SET_JOB_STATE key/value store. GET_JOB_STATE is scoped to the caller's own context and needs
    /// the key up front; SHOW JOB STATE lists every key for any orchestrator job (e.g. inspecting a
    /// watermark or the backup template's last_backup_* markers from an ad-hoc session).
    /// </summary>
    public class ShowJobStateTests
    {
        [Fact]
        public async Task ShowJobState_Into_ExposesMarkersAcrossJobs()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var store = provider.GetRequiredService<IJobHistoryStore>();
            var eval = provider.GetRequiredService<Evaluator>();
            await store.InitializeAsync();

            var jobA = "state_" + Guid.NewGuid().ToString("N")[..8];
            var jobB = "state_" + Guid.NewGuid().ToString("N")[..8];
            await store.SetJobStateAsync(jobA, "last_backup_status", "SUCCESS");
            await store.SetJobStateAsync(jobA, "last_backup_exit_code", "0");
            await store.SetJobStateAsync(jobB, "Watermark", "2026-07-01");

            // Filtered to one job: only its keys, readable from a completely different context.
            await TestHelpers.Execute(eval, $@"
SELECT * INTO #st FROM eng.job_state WHERE job_name = '{jobA}';
DECLARE @count INT = (SELECT COUNT(*) FROM #st);
DECLARE @status STRING = (SELECT state_value FROM #st WHERE state_key = 'last_backup_status');");

            Assert.Equal(2, Convert.ToInt32(eval.GetVariable("@count")));
            Assert.Equal("SUCCESS", eval.GetVariable("@status")?.ToString());
        }

        [Fact]
        public async Task ShowJobState_Unfiltered_ListsAllJobs_NewStoreMethodOrdersByJobThenKey()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            var suffix = Guid.NewGuid().ToString("N")[..8];
            await store.SetJobStateAsync($"zjob_{suffix}", "k2", "v2");
            await store.SetJobStateAsync($"ajob_{suffix}", "k1", "v1");

            var all = await store.GetJobStatesAsync();
            var mine = all.Where(e => e.JobName.EndsWith(suffix)).ToList();
            Assert.Equal(2, mine.Count);
            Assert.Equal($"ajob_{suffix}", mine[0].JobName); // ordered by job then key
            Assert.Equal("v1", mine[0].StateValue);
            Assert.True(mine[0].UpdatedAt > DateTime.UtcNow.AddMinutes(-5));
        }
    }
}
