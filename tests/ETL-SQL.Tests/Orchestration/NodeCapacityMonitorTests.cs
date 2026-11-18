using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        [Fact]
        public void ResolveSpillVolumePath_FollowsConfiguredPrecedence()
        {
            var spillPriority = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:SpillRoot"] = "/mnt/fast-spill",
                ["Session:Root"] = "/mnt/sessions",
                ["Orchestration:SandboxExecution:Enabled"] = "true",
                ["Orchestration:SandboxExecution:WorkspaceRoot"] = "/mnt/sandbox"
            }).Build();
            Assert.Equal("/mnt/fast-spill", NodeCapacityMonitor.ResolveSpillVolumePath(spillPriority));

            var sessionFallback = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Session:Root"] = "/mnt/sessions",
                ["Orchestration:SandboxExecution:Enabled"] = "false",
                ["Orchestration:SandboxExecution:WorkspaceRoot"] = "/mnt/sandbox"
            }).Build();
            Assert.Equal("/mnt/sessions", NodeCapacityMonitor.ResolveSpillVolumePath(sessionFallback));

            var sandboxFallback = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:SandboxExecution:Enabled"] = "true",
                ["Orchestration:SandboxExecution:WorkspaceRoot"] = "/mnt/sandbox"
            }).Build();
            Assert.Equal("/mnt/sandbox", NodeCapacityMonitor.ResolveSpillVolumePath(sandboxFallback));
        }

        [Fact]
        public void Capture_ConfiguredSpillVolumeReserveAndHysteresis_GovernsOverloadState()
        {
            const long mb = 1024L * 1024L;
            var configuredSpillPath = Path.Combine(Path.GetTempPath(), $"etlsql-spill-vol-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:SpillRoot"] = configuredSpillPath,
                ["Engine:SpillMinimumFreeDiskMB"] = "256",
                ["Engine:TotalMemoryGrantMB"] = "0"
            }).Build();

            long spillFreeBytes = 300 * mb;
            long stateFreeBytes = 4096 * mb;
            var monitor = new NodeCapacityMonitor(
                configuration,
                memoryArbiter: null,
                childProcessTracker: null,
                memoryLoadPercentProvider: () => 25.0,
                processCpuPercentProvider: () => 10.0,
                hostCpuPercentProvider: () => 15.0,
                freeBytesProvider: path => path == configuredSpillPath ? spillFreeBytes : stateFreeBytes,
                cacheWindow: TimeSpan.Zero);

            var healthy = monitor.Capture();
            Assert.False(healthy.IsOverloaded);
            Assert.Equal(configuredSpillPath, healthy.SpillVolumePath);
            Assert.Equal(300 * mb, healthy.SpillDiskFreeBytes);
            Assert.Equal(256 * mb, healthy.SpillMinimumFreeBytes);

            // Dropping below 256 MB enters overload.
            spillFreeBytes = 200 * mb;
            Assert.True(monitor.Capture().IsOverloaded);

            // Recovering slightly above 256 MB (to 280 MB, still below 256 + 64 = 320 MB) stays overloaded.
            spillFreeBytes = 280 * mb;
            Assert.True(monitor.Capture().IsOverloaded);

            // Clearing the hysteresis band (330 MB >= 320 MB) restores admission.
            spillFreeBytes = 330 * mb;
            Assert.False(monitor.Capture().IsOverloaded);
        }

        [Fact]
        public async Task Capture_AggregateWorkerReservationsWithHysteresis_DoesNotDoubleCountBackedWorkers()
        {
            const long mb = 1024L * 1024L;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:TotalMemoryGrantMB"] = "100",
                ["Engine:SpillMinimumFreeDiskMB"] = "0",
                ["Orchestration:ResourceManagement:HysteresisMemoryMB"] = "20",
                ["Orchestration:ResourceManagement:SystemMemoryFloorMB"] = "0"
            }).Build();

            using var bufferManager = new BufferManager(
                Options.Create(new BufferManagerOptions
                {
                    MaxGlobalMemoryMB = 100,
                    HysteresisMemoryMB = 20,
                    SystemMemoryFloorMB = 0
                }),
                NullLogger<BufferManager>.Instance,
                new UnlimitedSystemResources(),
                configuration);

            var pidStore = Path.Combine(Path.GetTempPath(), $"etlsql-ncm-pids-{Guid.NewGuid():N}.json");
            var fakeFactory = new FakeTrackedFactory();
            using var tracker = new ChildProcessTracker(NullLogger<ChildProcessTracker>.Instance, pidStore, fakeFactory);
            try
            {
                var monitor = new NodeCapacityMonitor(
                    configuration,
                    memoryArbiter: bufferManager,
                    childProcessTracker: tracker,
                    memoryLoadPercentProvider: () => 20.0,
                    processCpuPercentProvider: () => 10.0,
                    hostCpuPercentProvider: () => 20.0,
                    freeBytesProvider: _ => 10_000 * mb,
                    cacheWindow: TimeSpan.FromSeconds(5));

                // Worker 1 reserves 40 MB through BufferManager and holds 25 MB working set: must count as 40 MB, not 65 MB.
                using var backedReservation = await bufferManager.ReserveMemoryAsync("worker-1", 40 * mb);
                fakeFactory.RegisterWorkingSet(1001, 25 * mb);
                tracker.Register(1001, "worker-1.etlsql", reservedMemoryBytes: 40 * mb, backedByBufferManager: true);

                var initial = monitor.Capture();
                Assert.Equal(40 * mb, initial.AggregateWorkerReservedBytes);
                Assert.Equal(40.0, initial.WorkerMemoryReservationPercent, precision: 1);
                Assert.False(initial.IsOverloaded);

                // Unbacked Worker 2 claims 50 MB -> total 90 MB (90% >= 90%) -> enters overload immediately (cache bypassed).
                fakeFactory.RegisterWorkingSet(1002, 50 * mb);
                tracker.Register(1002, "worker-2.etlsql", reservedMemoryBytes: 50 * mb, backedByBufferManager: false);
                var pressured = monitor.Capture();
                Assert.Equal(90 * mb, pressured.AggregateWorkerReservedBytes);
                Assert.True(pressured.IsOverloaded);

                // Unregistering Worker 2 while holding a 45 MB lease stays overloaded until dropping <= 80% safe band:
                // Hold another 45 MB in BufferManager first (total 85 MB = 85%), unregister PID 1002 -> remains overloaded!
                using var extraReservation = await bufferManager.ReserveMemoryAsync("in-proc-2", 45 * mb);
                fakeFactory.SetExited(1002);
                tracker.Unregister(1002);
                var partialRecovery = monitor.Capture();
                Assert.Equal(85 * mb, partialRecovery.AggregateWorkerReservedBytes);
                Assert.True(partialRecovery.IsOverloaded);

                // Releasing the extra 45 MB drops usage to 40 MB (40% <= 80%) -> overload clears.
                extraReservation.Dispose();
                var recovered = monitor.Capture();
                Assert.Equal(40 * mb, recovered.AggregateWorkerReservedBytes);
                Assert.False(recovered.IsOverloaded);
            }
            finally
            {
                if (File.Exists(pidStore)) File.Delete(pidStore);
            }
        }

        [Fact]
        public void DefaultSystemResources_ComputeMemoryLoadPercent_DoesNotMisreadHostMemoryLoadAsProcessHeapLimitExhaustion()
        {
            const long gb = 1024L * 1024L * 1024L;
            const long mb = 1024L * 1024L;

            // Reproduce GCHeapHardLimit = 12 GB on a 32 GB host with ~16 GB used across all processes,
            // while this process only holds 256 MB working set. Previously 16 / 12 = 133% -> 100%.
            double loadWithHardLimit = DefaultSystemResources.ComputeMemoryLoadPercent(
                hostTotalBytes: 32 * gb,
                hostAvailableBytes: 16 * gb,
                cgroupLimitBytes: null,
                cgroupHeadroomBytes: null,
                gcTotalAvailableBytes: 12 * gb,
                gcMemoryLoadBytes: 16 * gb,
                processWorkingSetBytes: 256 * mb);

            Assert.InRange(loadWithHardLimit, 49.0, 51.0);

            // If this process itself pushes close to its 4 GB GC heap limit, process load wins.
            double processDominatedLoad = DefaultSystemResources.ComputeMemoryLoadPercent(
                hostTotalBytes: 32 * gb,
                hostAvailableBytes: 24 * gb,
                cgroupLimitBytes: null,
                cgroupHeadroomBytes: null,
                gcTotalAvailableBytes: 4 * gb,
                gcMemoryLoadBytes: 8 * gb,
                processWorkingSetBytes: (long)(3.9 * gb));

            Assert.True(processDominatedLoad >= 95.0, $"Expected >= 95%, got {processDominatedLoad:F1}%");
        }

        private sealed class UnlimitedSystemResources : ISystemResources
        {
            public long GetAvailableMemoryBytes() => 16L * 1024 * 1024 * 1024;
            public long GetTotalMemoryBytes() => 32L * 1024 * 1024 * 1024;
        }

        private sealed class FakeTrackedFactory : ITrackedChildProcessFactory
        {
            private readonly Dictionary<int, FakeProcess> _processes = new();

            public void RegisterWorkingSet(int pid, long workingSetBytes) =>
                _processes[pid] = new FakeProcess(pid, workingSetBytes);

            public void SetExited(int pid)
            {
                if (_processes.TryGetValue(pid, out var p))
                    p.HasExited = true;
            }

            public ITrackedChildProcess Open(int pid) =>
                _processes.TryGetValue(pid, out var p)
                    ? p
                    : throw new ArgumentException($"Unknown test PID {pid}");

            private sealed class FakeProcess(int pid, long workingSetBytes) : ITrackedChildProcess
            {
                public bool HasExited { get; set; }
                public ChildProcessIdentity Identity { get; } = new(pid * 100L, $"worker-{pid}.exe");
                public long WorkingSetBytes => HasExited ? 0L : workingSetBytes;
                public void Kill() => HasExited = true;
                public void Dispose() { }
            }
        }
    }
}
