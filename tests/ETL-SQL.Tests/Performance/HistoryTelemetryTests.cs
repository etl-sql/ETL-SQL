using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Tests.Performance
{
    public class HistoryTelemetryTests
    {
        private readonly ITestOutputHelper _output;

        public HistoryTelemetryTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task JobHistory_PersistsResourceTelemetry_Correctly()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var store = services.GetRequiredService<IJobHistoryStore>();
            
            // Clean/Initialize
            await store.InitializeAsync();
            
            var entry = new JobHistoryEntry
            {
                JobName = "TelemetryTestJob",
                Status = "Success",
                StartTime = DateTime.Now.AddMinutes(-5),
                EndTime = DateTime.Now,
                PeakMemoryBytes = 1024 * 1024 * 150, // 150 MB
                CpuTimeSeconds = 12.5,
                RowsProcessed = 500000,
                ErrorMessage = null
            };

            _output.WriteLine("Saving telemetry entry...");
            await store.LogJobEndAsync(entry);

            _output.WriteLine("Retrieving history...");
            var history = await store.GetHistoryAsync(10);
            var saved = history.FirstOrDefault(h => h.JobName == "TelemetryTestJob");

            Assert.NotNull(saved);
            Assert.Equal(entry.PeakMemoryBytes, saved.PeakMemoryBytes);
            Assert.Equal(entry.CpuTimeSeconds, saved.CpuTimeSeconds);
            Assert.Equal(entry.RowsProcessed, saved.RowsProcessed);
            
            _output.WriteLine($"Verified Telemetry: RAM={saved.PeakMemoryBytes / (1024 * 1024):N0} MB, CPU={saved.CpuTimeSeconds:N1} s");
        }
    }
}
