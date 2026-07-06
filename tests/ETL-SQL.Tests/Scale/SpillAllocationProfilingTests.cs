using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale
{
    /// <summary>
    /// v0.15.0 Phase 1 profiling harness for the Gate F <c>#temp</c> spill round trip
    /// (<c>SELECT ... INTO #result</c> over a streaming source with forced spill, then a COUNT
    /// readback — the same shape as the certified TempTableSpill scenario).
    ///
    /// Captures the Phase 1 baseline metrics BEFORE any implementation change: cumulative managed
    /// allocation and allocation rate, allocation-by-type (sampled via GCAllocationTick),
    /// retained-bytes delta across the run, GC counts and pause time, CPU time, process I/O
    /// transfer counters, and spill bytes. Emits a JSON report on the output stream
    /// (<c>SPILL_PROFILE_METRIC:</c>) and to <c>SPILL_PROFILE_OUTPUT</c> when set.
    ///
    /// Scale with <c>SPILL_PROFILE_ROWS</c> (default 50k keeps the harness validated in the
    /// standard lane); operator runs use <c>scripts/Test-SpillAllocProfile.ps1</c>. Call-site
    /// attribution requires stacks, which in-process listeners cannot see — the report includes
    /// the <c>dotnet-trace</c> command for that drill-down.
    /// </summary>
    [Collection("ScaleCertification")]
    [Trait("Category", "ScaleCertification")]
    public sealed class SpillAllocationProfilingTests(ITestOutputHelper output)
    {
        [Fact]
        public async Task Profile_TempTableSpillRoundTrip_AllocationGcCpuAndIo()
        {
            var rows = ReadRows("SPILL_PROFILE_ROWS", 50_000);

            // Settle the heap so retained/allocated deltas measure the scenario, not prior tests.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            var retainedBeforeBytes = GC.GetTotalMemory(forceFullCollection: true);

            using var allocationProfiler = new AllocationTypeProfiler();
            using var sampler = new ScenarioResourceSampler();
            var ioBefore = ProcessIoCounters.Capture();
            allocationProfiler.Start();

            long resultCount;
            long spillBytes;
            var stopwatch = Stopwatch.StartNew();
            {
                await using var evaluator = NewEvaluator();
                evaluator.Connections["#cert"] = StreamingRowSource.GrpVal(rows, 10);
                // Retain one configured batch, then force all subsequent batches through spill —
                // identical forcing to the certified TempTableSpill scenario.
                evaluator.TempTableSpillThresholdRows = evaluator.BatchSize;
                evaluator.Telemetry.Clear();

                await evaluator.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #result FROM #cert;"));
                var countRow = (await evaluator.ExecuteQuery(
                    TestHelpers.Parse("SELECT COUNT(*) AS n FROM #result;").Statements[0]).FirstAsync()).Rows[0];
                resultCount = Convert.ToInt64(countRow["n"], CultureInfo.InvariantCulture);
                spillBytes = evaluator.Telemetry.TotalSpilledBytes;
            }
            stopwatch.Stop();

            allocationProfiler.Stop();
            var resources = sampler.SnapshotAndReset();
            var io = ProcessIoCounters.Delta(ioBefore, ProcessIoCounters.Capture());

            // Retained delta: what the round trip left behind after the evaluator is disposed.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            var retainedAfterBytes = GC.GetTotalMemory(forceFullCollection: true);

            // Correctness and harness sanity — this is a measurement gate, not a perf gate.
            Assert.Equal(rows, resultCount);
            Assert.True(spillBytes > 0, "Profiling run must exercise the physical spill path.");
            Assert.True(resources.AllocatedBytes > 0, "Allocation counters did not advance.");
            var topAllocations = allocationProfiler.Snapshot(top: 25);
            Assert.NotEmpty(topAllocations);

            var elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
            var totalSampled = Math.Max(1, allocationProfiler.TotalSampledBytes);
            var report = new
            {
                scenario = "SpillAllocProfile_TempTableRoundTrip",
                schema = 1,
                rows,
                elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
                rowsPerSecond = Math.Round(rows / elapsedSeconds, 1),
                spillBytes,
                allocation = new
                {
                    cumulativeBytes = resources.AllocatedBytes,
                    ratePerSecondBytes = (long)(resources.AllocatedBytes / elapsedSeconds),
                    bytesPerRow = Math.Round(resources.AllocatedBytes / (double)rows, 1),
                    retainedBeforeBytes,
                    retainedAfterBytes,
                    retainedDeltaBytes = retainedAfterBytes - retainedBeforeBytes
                },
                gc = new
                {
                    gen0 = resources.Gen0Collections,
                    gen1 = resources.Gen1Collections,
                    gen2 = resources.Gen2Collections,
                    pauseMs = Math.Round(resources.GcPauseTime.TotalMilliseconds, 1),
                    peakManagedHeapMB = Math.Round(resources.PeakManagedHeapBytes / 1048576d, 1)
                },
                cpu = new
                {
                    timeMs = Math.Round(resources.CpuTime.TotalMilliseconds, 1),
                    utilizationPercent = resources.CpuUtilizationPercent
                },
                memory = new
                {
                    peakWorkingSetMB = Math.Round(resources.PeakWorkingSetBytes / 1048576d, 1),
                    peakPrivateBytesMB = Math.Round(resources.PeakPrivateBytes / 1048576d, 1)
                },
                io = new
                {
                    supported = io.Supported,
                    readBytes = io.ReadBytes,
                    writeBytes = io.WriteBytes,
                    readOperations = io.ReadOperations,
                    writeOperations = io.WriteOperations
                },
                // Sampled per-type attribution (~1 tick per 100 KB per type). Proportions are the
                // signal; use them to rank churn sources, then confirm call sites via:
                //   dotnet-trace collect -p <pid> --profile gc-verbose
                topAllocations = topAllocations.Select(sample => new
                {
                    type = sample.TypeName,
                    sampledBytes = sample.SampledBytes,
                    ticks = sample.Ticks,
                    sharePercent = Math.Round(sample.SampledBytes * 100d / totalSampled, 1)
                })
            };

            var json = JsonSerializer.Serialize(report);
            output.WriteLine("SPILL_PROFILE_METRIC:" + json);
            var outputPath = Environment.GetEnvironmentVariable("SPILL_PROFILE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(outputPath, json);
            }
        }

        private static Evaluator NewEvaluator()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.IsPersistentSession = false;
            // Same governed setup as the certification host: private arbiter (never the shared
            // budget), modest default ceiling so the spill path is genuinely exercised.
            if (!int.TryParse(Environment.GetEnvironmentVariable("CERT_MEMORY_GRANT_MB"), out var grantMb))
                grantMb = 2048;
            if (grantMb > 0)
                evaluator.MemoryArbiter = new MemoryGrantArbiter((long)grantMb * 1024 * 1024);
            if (int.TryParse(Environment.GetEnvironmentVariable("CERT_BATCH_ROWS"), out var batchRows)
                && batchRows > 0)
                evaluator.BatchSize = batchRows;
            return evaluator;
        }

        private static int ReadRows(string name, int fallback)
            => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
    }
}
