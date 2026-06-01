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

namespace ETL_SQL.Tests.Hardening.Performance
{
    [Trait("Category", "Performance")]
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
            
            // 1. Log Start
            _output.WriteLine("Logging job start...");
            var entryId = await store.LogJobStartAsync("TelemetryTestJob");
            
            // 2. Log End with telemetry
            _output.WriteLine("Logging job end with telemetry...");
            const long rows = 500000;
            const long ram = 1024 * 1024 * 150;
            const double cpu = 12.5;
            await store.LogJobEndAsync(entryId, "Success", null, rows, ram, cpu);

            _output.WriteLine("Retrieving history...");
            var history = await store.GetHistoryAsync("TelemetryTestJob", 10);
            var saved = history.FirstOrDefault();

            Assert.NotNull(saved);
            Assert.Equal(ram, saved.PeakMemoryBytes);
            Assert.Equal(cpu, saved.CpuTimeSeconds);
            Assert.Equal(rows, saved.RowsProcessed);
            
            _output.WriteLine($"Verified Telemetry: RAM={saved.PeakMemoryBytes / (1024 * 1024):N0} MB, CPU={saved.CpuTimeSeconds:N1} s");
        }
    }
}
