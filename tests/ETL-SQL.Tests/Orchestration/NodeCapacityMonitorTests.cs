using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
        public void Capture_FirstSampleAfterStartUp_DoesNotReadAFewBusyThreadsAsAnOverloadedNode()
        {
            // A host captures within a millisecond of building its monitor. Processor time arrives
            // in clock ticks, so over that window two busy threads once read as 100% of every core
            // and the node refused work for its first second.
            using var stop = new CancellationTokenSource();
            var busy = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                var value = 0d;
                while (!stop.IsCancellationRequested) value += Math.Sqrt(value + 1);
            })
            { IsBackground = true }).ToList();
            busy.ForEach(thread => thread.Start());
            try
            {
                for (var trial = 0; trial < 2000; trial++)
                {
                    var monitor = new NodeCapacityMonitor();
                    Thread.SpinWait(2000 + trial % 50 * 400);
                    var snapshot = monitor.Capture();
                    Assert.False(snapshot.IsOverloaded && snapshot.MemoryLoadPercent < 95,
                        $"trial {trial}: first sample reported CPU {snapshot.ProcessCpuPercent:F0}%");
                    Assert.Equal(0d, snapshot.ProcessCpuPercent);
                }
            }
            finally
            {
                stop.Cancel();
                busy.ForEach(thread => thread.Join());
            }
        }

        [Fact]
        public async Task Capture_OverARealWindow_ReportsTheProcessCpuRate()
        {
            var monitor = new NodeCapacityMonitor();
            using var stop = new CancellationTokenSource();
            var busy = new Thread(() =>
            {
                var value = 0d;
                while (!stop.IsCancellationRequested) value += Math.Sqrt(value + 1);
            })
            { IsBackground = true };
            busy.Start();
            try
            {
                await Task.Delay(1200);
                // One saturated thread out of N cores, with slack for a shared runner.
                var expected = 100.0 / Environment.ProcessorCount;
                Assert.InRange(monitor.Capture().ProcessCpuPercent, expected * 0.4, 100);
            }
            finally
            {
                stop.Cancel();
                busy.Join();
            }
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
