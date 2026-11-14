using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Spill;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Tests.Engine;

// Spill-volume faults the HA certification injects for real: a volume below its free-space reserve,
// and the temp root of a process that died without cleaning up.
public class SpillRootLifecycleTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "etlsql-spill-sweep-" + Guid.NewGuid().ToString("N"));

    public SpillRootLifecycleTests() => Directory.CreateDirectory(_parent);

    public void Dispose()
    {
        if (Directory.Exists(_parent)) Directory.Delete(_parent, true);
    }

    private const string Load = "WITH numbers AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM numbers WHERE n < 50) SELECT n AS id INTO #reserve FROM numbers;";

    private static async Task<(ServiceProvider Provider, Evaluator Evaluator)> SpillingEvaluatorAsync(long reserveBytes)
    {
        var provider = (ServiceProvider)global::ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider();
        var ev = provider.GetRequiredService<Evaluator>();
        ev.IsPersistentSession = false;
        ev.TempTableSpillThresholdRows = 1;
        ev.SpillMinimumFreeBytes = reserveBytes;
        await Task.CompletedTask;
        return (provider, ev);
    }

    [Fact]
    public async Task SpillBelowFreeSpaceReserve_FailsBeforeWritingAnExtent()
    {
        var (provider, ev) = await SpillingEvaluatorAsync(long.MaxValue);
        await using var _ = provider;

        var failure = await Record.ExceptionAsync(() => ev.Evaluate(TestHelpers.Parse(
            Load)));

        var error = Assert.IsType<ExecutionException>(failure);
        Assert.Contains("Spill storage is low on space", error.Message);
        Assert.Contains("Engine:SpillMinimumFreeDiskMB", error.Message);
        Assert.DoesNotContain(ev.SpillStore.RootPath, error.Message);
        Assert.Equal(0, ev.Telemetry.SpillExtentCount);
        Assert.Empty(Directory.GetFiles(ev.SpillStore.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SpillWithReserveDisabled_Spills()
    {
        var (provider, ev) = await SpillingEvaluatorAsync(0);
        await using var _ = provider;

        await ev.Evaluate(TestHelpers.Parse(Load));

        Assert.True(ev.Telemetry.SpillExtentCount > 0);
    }

    [Fact]
    public async Task SpillingStatement_ReportsItsSpillWriteCost()
    {
        var (provider, ev) = await SpillingEvaluatorAsync(0);
        await using var _ = provider;
        ev.Telemetry.IsProfiling = true;

        await ev.Evaluate(TestHelpers.Parse(Load));

        var spilled = Assert.Single(ev.Telemetry.ProfileMetrics, metric => metric.SpilledBytes > 0);
        Assert.True(spilled.SpillWriteMsPerMB > 0, "A statement that spilled reports its spill write cost.");
        Assert.Equal(0, spilled.StoragePressureEvents);
        Assert.True(ev.AdaptiveMetrics.TotalSpillWriteBytes > 0);
    }

    [Fact]
    public async Task DisposableTempRoot_RecordsItsOwnerUntilDisposed()
    {
        var (provider, ev) = await SpillingEvaluatorAsync(0);
        var root = ev.SpillStore.RootPath;
        var marker = SpillRootOwnership.MarkerPath(root);

        Assert.True(File.Exists(marker), "A disposable temp spill root must name its owner.");
        Assert.False(SpillRootOwnership.IsAbandoned(root), "The live owner must not be reported abandoned.");

        await provider.DisposeAsync();
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(marker), "Disposal removes the marker with the root.");
    }

    [Fact]
    public void Ownership_IsAbandonedOnlyForADeadOrReplacedOwnerOnThisHost()
    {
        using var self = Process.GetCurrentProcess();
        var started = self.StartTime.ToUniversalTime().Ticks;
        string Root(string name, string marker)
        {
            var root = Path.Combine(_parent, name);
            Directory.CreateDirectory(root);
            File.WriteAllText(SpillRootOwnership.MarkerPath(root), marker);
            return root;
        }

        Assert.False(SpillRootOwnership.IsAbandoned(Root("live", $"{Environment.MachineName}\n{self.Id}\n{started}")));
        Assert.True(SpillRootOwnership.IsAbandoned(Root("dead", $"{Environment.MachineName}\n{int.MaxValue}\n{started}")));
        // A restarted container reuses its process id; a different start time means a different process.
        Assert.True(SpillRootOwnership.IsAbandoned(Root("reused", $"{Environment.MachineName}\n{self.Id}\n{started - TimeSpan.TicksPerMinute}")));
        Assert.False(SpillRootOwnership.IsAbandoned(Root("foreign", $"another-host\n{int.MaxValue}\n{started}")));
        Assert.False(SpillRootOwnership.IsAbandoned(Root("garbled", "not a marker")));
        Assert.False(SpillRootOwnership.IsAbandoned(Path.Combine(_parent, "unmarked")));
    }

    [Fact]
    public void Sweep_RemovesAbandonedAndAgedRootsAndKeepsLiveOnes()
    {
        using var self = Process.GetCurrentProcess();
        var started = self.StartTime.ToUniversalTime().Ticks;
        var old = DateTime.UtcNow.AddDays(-2);
        string Root(string name, string? marker, DateTime? lastWrite = null)
        {
            var root = Path.Combine(_parent, name);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "extent.tmp"), "x");
            if (marker != null) File.WriteAllText(SpillRootOwnership.MarkerPath(root), marker);
            if (lastWrite is { } time) Directory.SetLastWriteTimeUtc(root, time);
            return root;
        }

        var crashed = Root("crashed", $"{Environment.MachineName}\n{int.MaxValue}\n{started}");
        var liveLongJob = Root("live-long-job", $"{Environment.MachineName}\n{self.Id}\n{started}", old);
        var agedUnmarked = Root("aged-unmarked", null, old);
        var freshUnmarked = Root("fresh-unmarked", null);
        var staleMarker = SpillRootOwnership.MarkerPath(Path.Combine(_parent, "already-gone"));
        File.WriteAllText(staleMarker, "x");

        var removed = ChildProcessTracker.SweepSpillRoots(_parent, TimeSpan.FromHours(24), NullLogger.Instance);

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(crashed));
        Assert.False(File.Exists(SpillRootOwnership.MarkerPath(crashed)));
        Assert.False(Directory.Exists(agedUnmarked));
        Assert.True(Directory.Exists(liveLongJob), "A live owner keeps its root whatever its age.");
        Assert.True(Directory.Exists(freshUnmarked));
        Assert.False(File.Exists(staleMarker));
        Assert.Equal(new[] { "fresh-unmarked", "live-long-job" },
            Directory.GetDirectories(_parent).Select(Path.GetFileName).Order().ToArray());
    }
}
