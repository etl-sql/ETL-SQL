using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ETL_SQL.Orchestrator.Scheduling;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class NodeCapacityMonitorTests
    {
        [Fact]
        public void Capture_ReportsFreeDiskOnStateAndSpillVolumes()
        {
            var snapshot = new NodeCapacityMonitor().Capture();

            // The volumes hosting the app and the temp/spill directory always have some free space
            // on a machine that can run the test host; the point is that disk headroom is now reported
            // alongside memory/CPU (the outage-critical host signal that was previously absent).
            Assert.True(snapshot.StateDiskFreeBytes > 0, "State-volume free disk should be reported.");
            Assert.True(snapshot.SpillDiskFreeBytes > 0, "Spill-volume free disk should be reported.");
        }

        [Fact]
        public void Capture_StillReportsMemoryAndCpu()
        {
            var snapshot = new NodeCapacityMonitor().Capture();

            Assert.True(snapshot.ProcessorCount >= 1);
            Assert.InRange(snapshot.MemoryLoadPercent, 0, 100);
            Assert.InRange(snapshot.ProcessCpuPercent, 0, 100);
        }

        [Fact]
        public void Capture_FirstSample_HasNoHostCpuYet()
        {
            // Whole-host CPU is a rate: it needs two OS counter samples, so the very first Capture
            // reports null (never a bogus 0/100 spike).
            var snapshot = new NodeCapacityMonitor().Capture();
            Assert.Null(snapshot.HostCpuPercent);
        }

        [Fact]
        public async Task Capture_SecondSample_ReportsHostCpuOnSupportedPlatforms()
        {
            var monitor = new NodeCapacityMonitor();
            monitor.Capture(); // establishes the baseline

            // The snapshot is cached for 1s; wait past that so the next Capture recomputes over a real
            // interval of OS-level CPU counters.
            await Task.Delay(1200);
            var second = monitor.Capture();

            // Always: a reported value is a valid percentage.
            if (second.HostCpuPercent is not null)
                Assert.InRange(second.HostCpuPercent.Value, 0, 100);

            // On Windows and Linux the counters are available, so the second sample must be populated.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Assert.NotNull(second.HostCpuPercent);
        }
    }
}
