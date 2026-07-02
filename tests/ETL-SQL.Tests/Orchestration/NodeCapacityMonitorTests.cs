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
    }
}
