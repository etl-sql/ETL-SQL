using System.Text.Json.Nodes;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public class ChildProcessIdentityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "etlsql-child-identity-" + Guid.NewGuid().ToString("N"));
    private string Store => Path.Combine(_directory, "children.json");
    private readonly FakeFactory _factory = new();

    private ChildProcessTracker Tracker() => new(NullLogger<ChildProcessTracker>.Instance, Store, _factory);

    [Theory]
    [InlineData("start")]
    [InlineData("executable")]
    [InlineData("owner")]
    [InlineData("legacy")]
    public void RestartDoesNotKillUnverifiedProcess(string mismatch)
    {
        Tracker().Register(123, "owned-script.etlsql");
        switch (mismatch)
        {
            case "start": _factory.Process.Identity = new(456, "worker"); break;
            case "executable": _factory.Process.Identity = new(123, "unrelated"); break;
            case "owner":
                var entries = JsonNode.Parse(File.ReadAllText(Store))!;
                entries[0]!["Owner"] = "another-service-account";
                File.WriteAllText(Store, entries.ToJsonString());
                break;
            case "legacy": File.WriteAllText(Store, "[{\"Pid\":123,\"ScriptPath\":\"legacy\"}]"); break;
        }
        Tracker().CleanupOrphans();
        Assert.Equal(0, _factory.Process.Kills);
        Assert.Equal("[]", File.ReadAllText(Store));
    }

    [Fact]
    public void RestartKillsOnlyMatchingRegisteredChild()
    {
        Tracker().Register(123, "owned-script.etlsql");
        Tracker().CleanupOrphans();
        Assert.Equal(1, _factory.Process.Kills);
        Assert.Equal("[]", File.ReadAllText(Store));
    }

    [Fact]
    public void FailedTerminationRetainsIdentityForRetry()
    {
        Tracker().Register(123, "owned-script.etlsql");
        _factory.Process.FailKill = true;
        Tracker().CleanupOrphans();
        Assert.Contains("StartedUtcTicks", File.ReadAllText(Store));
        _factory.Process.FailKill = false;
        Tracker().CleanupOrphans();
        Assert.Equal(1, _factory.Process.Kills);
    }

    [Fact]
    public void Unregister_CoalescesSpillSweepsAcrossBurstWorkerExits()
    {
        var spillRootParent = Path.Combine(_directory, "spill-parent");
        Directory.CreateDirectory(spillRootParent);

        var deadMarkerDir1 = Path.Combine(spillRootParent, "dead-worker-1");
        Directory.CreateDirectory(deadMarkerDir1);
        File.WriteAllText(
            ETL_SQL.Core.Spill.SpillRootOwnership.MarkerPath(deadMarkerDir1),
            $"{Environment.MachineName}\n{int.MaxValue}\n0");

        using var tracker = new ChildProcessTracker(
            NullLogger<ChildProcessTracker>.Instance,
            Store,
            _factory,
            sweepCoalesceInterval: TimeSpan.FromSeconds(30))
        {
            SpillParentOverride = spillRootParent
        };

        // First Unregister runs an immediate sweep.
        tracker.Register(123, "job-1.etlsql");
        tracker.Unregister(123);
        Assert.Equal(1, tracker.SweepExecutionCount);
        Assert.False(Directory.Exists(deadMarkerDir1));

        // Additional exits inside the coalesce window mark one deferred sweep instead of scanning N times.
        var deadMarkerDir2 = Path.Combine(spillRootParent, "dead-worker-2");
        Directory.CreateDirectory(deadMarkerDir2);
        File.WriteAllText(
            ETL_SQL.Core.Spill.SpillRootOwnership.MarkerPath(deadMarkerDir2),
            $"{Environment.MachineName}\n{int.MaxValue}\n0");

        for (int i = 0; i < 5; i++)
        {
            tracker.Register(123, $"job-burst-{i}.etlsql");
            tracker.Unregister(123);
        }

        Assert.Equal(1, tracker.SweepExecutionCount);
        Assert.True(Directory.Exists(deadMarkerDir2));

        // Flushing the coalesced sweep performs a single follow-up scan for the entire burst.
        tracker.FlushCoalescedSweep();
        Assert.Equal(2, tracker.SweepExecutionCount);
        Assert.False(Directory.Exists(deadMarkerDir2));
    }

    [Fact]
    public void WorkerMemoryTracking_SeparatesAggregateFromUnaccountedExcess()
    {
        const long mb = 1024L * 1024L;
        var factory = new WeightedFactory();
        using var tracker = new ChildProcessTracker(NullLogger<ChildProcessTracker>.Instance, Store, factory);

        // PID 201 is backed by a 30 MB BufferManager reservation and currently uses 18 MB working set.
        factory.Add(201, workingSetBytes: 18 * mb);
        tracker.Register(201, "backed-within.etlsql", reservedMemoryBytes: 30 * mb, backedByBufferManager: true);

        // PID 202 is backed by 20 MB in BufferManager, with 26 MB working set (6 MB excess over grant).
        factory.Add(202, workingSetBytes: 26 * mb);
        tracker.Register(202, "backed-over.etlsql", reservedMemoryBytes: 20 * mb, backedByBufferManager: true);

        // PID 203 is unbacked with 15 MB working set.
        factory.Add(203, workingSetBytes: 15 * mb);
        tracker.Register(203, "unbacked.etlsql", reservedMemoryBytes: 0, backedByBufferManager: false);

        Assert.Equal((30 + 26 + 15) * mb, tracker.GetAggregateWorkerMemoryBytes());
        Assert.Equal((0 + 6 + 15) * mb, tracker.GetUnaccountedWorkerMemoryBytes());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class WeightedFactory : ITrackedChildProcessFactory
    {
        private readonly Dictionary<int, WeightedProcess> _map = new();
        public void Add(int pid, long workingSetBytes) => _map[pid] = new WeightedProcess(pid, workingSetBytes);
        public ITrackedChildProcess Open(int pid) => _map[pid];

        private sealed class WeightedProcess(int pid, long workingSetBytes) : ITrackedChildProcess
        {
            public bool HasExited => false;
            public ChildProcessIdentity Identity { get; } = new(pid * 10L, $"worker-{pid}.exe");
            public long WorkingSetBytes => workingSetBytes;
            public void Kill() { }
            public void Dispose() { }
        }
    }

    private sealed class FakeFactory : ITrackedChildProcessFactory
    {
        public FakeProcess Process { get; } = new();
        public ITrackedChildProcess Open(int pid)
        {
            Assert.Equal(123, pid);
            return Process;
        }
    }

    private sealed class FakeProcess : ITrackedChildProcess
    {
        public ChildProcessIdentity Identity { get; set; } = new(123, "worker");
        public bool HasExited => false;
        public bool FailKill { get; set; }
        public int Kills { get; private set; }
        public void Kill()
        {
            if (FailKill) throw new UnauthorizedAccessException("Synthetic termination failure");
            Kills++;
        }
        public void Dispose() { }
    }
}
