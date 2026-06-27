using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Benchmarks;

/// <summary>Storage, rendering, and local scheduling service benchmarks.</summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class RuntimeServiceBenchmarks
{
    private string _root = null!;
    private string _snapshotPath = null!;
    private ReportManifest _manifest = null!;
    private SnapshotStore _snapshots = null!;
    private MarkdownRenderer _markdown = null!;
    private JobThrottle _throttle = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ETL-SQL-RuntimeBenchmarks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _snapshotPath = Path.Combine(_root, "dashboard.etlsnap");
        _manifest = new ReportManifest
        {
            Source = "benchmark.rptsql",
            Title = "Runtime Benchmark",
            Visuals =
            {
                new VisualManifest
                {
                    Name = "Transactions",
                    VisualType = "TABLE",
                    Columns = ["Id", "Category", "Amount"],
                    Rows = Enumerable.Range(0, 12_000)
                        .Select(i => new System.Collections.Generic.List<string?>
                        {
                            i.ToString(), $"Category-{i % 100}", (i * 1.25m).ToString()
                        }).ToList()
                }
            }
        };
        _snapshots = new SnapshotStore();
        _markdown = new MarkdownRenderer();
        await _snapshots.SaveAsync(_manifest, _snapshotPath);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Orchestrator:DatabasePath"] = Path.Combine(_root, "orchestrator.db")
        }).Build();
        _throttle = new JobThrottle(
            Options.Create(new JobThrottleOptions { MaxConcurrentJobs = 1, PollJitterRatio = 0 }),
            NullLogger<JobThrottle>.Instance,
            configuration);
    }

    [Benchmark(Description = "SnapshotSave — encrypted hybrid Arrow package")]
    public Task SnapshotSave() => _snapshots.SaveAsync(_manifest, _snapshotPath);

    [Benchmark(Description = "SnapshotLoad — encrypted hybrid Arrow package")]
    public Task<ReportManifest?> SnapshotLoad() => _snapshots.LoadAsync(_snapshotPath);

    [Benchmark(Description = "ReportRender — 12k-row Markdown table")]
    public string ReportRender() => _markdown.Render(_manifest);

    [Benchmark(Description = "OrchestratorSlot — SQLite scheduling acquire/release")]
    public async Task OrchestratorSlot()
    {
        using var slot = await _throttle.AcquireAsync("benchmark-job");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        while (_throttle.GetMetrics().ActiveJobs > 0) await Task.Delay(10);
        _throttle.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
