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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
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
