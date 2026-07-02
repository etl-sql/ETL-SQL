using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Scale
{
    /// <summary>
    /// Smoke-tier scale certification tests (50k–100k rows). Run with:
    ///   dotnet test --filter "Category=ScaleCertification"
    ///
    /// Each test emits a JSON metrics line to ITestOutputHelper, consumed by
    /// scripts/Test-ScaleCertification.ps1 to produce the certification report.
    ///
    /// Row-level assertions use aggregate summary queries (COUNT, SUM, MIN, MAX) rather
    /// than collecting all rows in memory, since ExecuteQuery streams one batch at a time.
    /// </summary>
    [CollectionDefinition("ScaleCertification", DisableParallelization = true)]
    public sealed class ScaleCertificationCollection
    {
    }

    [Collection("ScaleCertification")]
    [Trait("Category", "ScaleCertification")]
    public class ScaleCertificationTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly ScenarioResourceSampler _resourceSampler;

        public ScaleCertificationTests(ITestOutputHelper output)
        {
            _out = output;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            _resourceSampler = new ScenarioResourceSampler();
        }

        public void Dispose() => _resourceSampler.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Evaluator NewEvaluator()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            // Certification hosts are disposable and must expose spill under the runner's monitored
            // temp root. Persisting these sessions both hides live disk use from the HUD and leaves
            // multi-gigabyte artifacts after a successful run.
            ev.IsPersistentSession = false;
            // The cert runs on dev workstations / CI agents (not dedicated DB hosts) and drives large
            // inputs, so cap the RAM-governor ceiling to a modest value rather than inheriting the
            // production auto (~80% of physical RAM) default. This keeps even a 50M run within a few GB
            // and actively exercises the governor's spill/repartition paths. Override with
            // CERT_MEMORY_GRANT_MB; set it to 0 to fall back to the production default.
            var raw = Environment.GetEnvironmentVariable("CERT_MEMORY_GRANT_MB");
            if (!int.TryParse(raw, out var grantMb)) grantMb = 2048;
            if (grantMb > 0)
                MemoryGrantArbiter.Shared.TotalBudgetBytes = (long)grantMb * 1024 * 1024;
            var rawBatchRows = Environment.GetEnvironmentVariable("CERT_BATCH_ROWS");
            if (int.TryParse(rawBatchRows, out var batchRows) && batchRows > 0)
                ev.BatchSize = batchRows;
            return ev;
        }

        private static int ScaleRows(int baseRows)
        {
            var raw = Environment.GetEnvironmentVariable("CERT_ROW_SCALE");
            if (!double.TryParse(raw, out var scale) || scale <= 0)
            {
                scale = 1.0;
            }

            return Math.Max(1_000, (int)Math.Round(baseRows * scale));
        }

        private static void AssertSpilled(Evaluator ev, string scenario)
        {
            Assert.True(ev.Telemetry.TotalSpilledBytes > 0,
                $"{scenario} expected spill evidence, but TotalSpilledBytes was 0.");
        }

        private static async Task<Evaluator> EvWithRows(int rowCount, int groups = 10)
        {
            var ev = NewEvaluator();
            ev.Connections["#cert"] = await SourceWithRows(rowCount, groups);
            return ev;
        }

        // Streaming generator sources — the rows are produced lazily one batch at a time, so the input
        // never materializes in memory (critical for the 50M Huge tier; see StreamingRowSource).
        private static Task<IDataSource> SourceWithRows(int rowCount, int groups = 10)
            => Task.FromResult<IDataSource>(StreamingRowSource.GrpVal(rowCount, groups));

        private static Task<IDataSource> SourceWithCubeRows(int rowCount, int groups = 10, int buckets = 5)
            => Task.FromResult<IDataSource>(StreamingRowSource.GrpBucketVal(rowCount, groups, buckets));

        private static async Task<(long Count, decimal Sum)> CountAndSum(IAsyncEnumerable<DataTable> batches, string valueColumn = "val")
        {
            long count = 0;
            decimal sum = 0;

            await foreach (var batch in batches)
            {
                count += batch.Rows.Count;
                foreach (var row in batch.Rows)
                    sum += Convert.ToDecimal(row[valueColumn]);
            }

            return (count, sum);
        }

        // Execute a single-row aggregate query and return its first (only) row.
        private static async Task<Row> AggQuery(Evaluator ev, string sql)
        {
            var res = await ev.ExecuteQuery(TestHelpers.Parse(sql).Statements[0]).FirstAsync();
            return res.Rows[0];
        }

        private static int CountFiles(string dir)
            => Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length : 0;

        private static double RowScale()
        {
            var raw = Environment.GetEnvironmentVariable("CERT_ROW_SCALE");
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale <= 0)
            {
                return 1.0;
            }

            return scale;
        }

        private static double RowScaleFrom(string variableName, double defaultScale)
        {
            var raw = Environment.GetEnvironmentVariable(variableName);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale <= 0)
            {
                return defaultScale;
            }

            return scale;
        }

        private async Task RunSmokeScenarioSetWithScale(double rowScale, string certificationTier)
        {
            var previous = Environment.GetEnvironmentVariable("CERT_ROW_SCALE");
            var previousTier = Environment.GetEnvironmentVariable("CERT_CERTIFICATION_TIER");
            Environment.SetEnvironmentVariable("CERT_ROW_SCALE", rowScale.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("CERT_CERTIFICATION_TIER", certificationTier);

            // Ordered scenario list so we can report live progress (which scenario, X of N) to a
            // side-channel file — ITestOutputHelper buffers until the whole [Fact] finishes, so it
            // cannot drive a live HUD during a multi-hour Huge run.
            var scenarios = new (string Name, Func<Task> Run)[]
            {
                ("ExternalSort", Cert_Smoke_ExternalSort_50kRows_AllRowsMaterialized),
                ("ExternalAggregate", Cert_Smoke_ExternalAggregate_100kRows_CorrectSums),
                ("ExternalJoin", Cert_Smoke_ExternalJoin_50kRows_CorrectResults),
                ("TempTableSpill", Cert_Smoke_TempTableSpill_50kRows_CorrectCount),
                ("StreamingSelect", Cert_Smoke_StreamingSelect_ResultCapEnforced),
                ("WindowFunction", Cert_Smoke_WindowFunction_50kRows_CorrectRankValues),
                ("CsvIngest", Cert_Smoke_CsvIngest_50kRows_CorrectChecksum),
                ("ParquetRoundTrip", Cert_Smoke_ParquetRoundTrip_50kRows_CorrectChecksum),
                ("ReportDatasetSnapshotReload", Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum),
                ("CubeGroupingSets", Cert_Smoke_CubeGroupingSets_50kRows_CorrectExpansionAndChecksum),
                ("ScalarSubqueryCache", Cert_Smoke_ScalarSubqueryCache_50kRows_ReusesRepeatedKeys),
                ("SpillCleanup_Success", Cert_Smoke_SpillCleanup_AfterSuccessfulTempSpill_RemovesNonPersistentFiles),
                ("SpillCleanup_Failure", Cert_Smoke_SpillCleanup_AfterFailedTempSpill_RemovesNonPersistentFiles),
            };

            try
            {
                for (int i = 0; i < scenarios.Length; i++)
                {
                    WriteProgress(certificationTier, i + 1, scenarios.Length, scenarios[i].Name);
                    await scenarios[i].Run();
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                }
                WriteProgress(certificationTier, scenarios.Length, scenarios.Length, "done");
            }
            finally
            {
                Environment.SetEnvironmentVariable("CERT_ROW_SCALE", previous);
                Environment.SetEnvironmentVariable("CERT_CERTIFICATION_TIER", previousTier);
            }
        }

        /// <summary>
        /// Best-effort live progress to the file named by CERT_PROGRESS_FILE (set by
        /// Test-ScaleCertification.ps1). Written immediately (unlike ITestOutputHelper, which buffers
        /// until the test completes) so an external HUD can show the current scenario and X/N progress.
        /// No-op when the env var is unset, so normal test runs are unaffected.
        /// </summary>
        private static void WriteProgress(string tier, int index, int total, string scenario)
        {
            var file = Environment.GetEnvironmentVariable("CERT_PROGRESS_FILE");
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                File.WriteAllText(file,
                    $"{tier}|{index}|{total}|{scenario}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            }
            catch { /* progress reporting must never affect the run */ }
        }

        private async Task RunProviderScenarioSetWithScale(double rowScale)
        {
            var previous = Environment.GetEnvironmentVariable("CERT_ROW_SCALE");
            var previousTier = Environment.GetEnvironmentVariable("CERT_CERTIFICATION_TIER");
            Environment.SetEnvironmentVariable("CERT_ROW_SCALE", rowScale.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("CERT_CERTIFICATION_TIER", "Provider");

            try
            {
                await Cert_Smoke_CsvIngest_50kRows_CorrectChecksum();
                await Cert_Smoke_ParquetRoundTrip_50kRows_CorrectChecksum();
                await Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum();
            }
            finally
            {
                Environment.SetEnvironmentVariable("CERT_ROW_SCALE", previous);
                Environment.SetEnvironmentVariable("CERT_CERTIFICATION_TIER", previousTier);
            }
        }

        private static string MemoryTier(double rowScale)
        {
            if (rowScale <= 1.0)
            {
                return "Smoke";
            }

            return rowScale <= 10.0 ? "Standard" : "Stress";
        }

        private static double MemoryBoundMB(double rowScale)
        {
            var raw = Environment.GetEnvironmentVariable("CERT_MEMORY_BOUND_MB");
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var configured) && configured > 0)
            {
                return Math.Round(configured, 1);
            }

            var bound = MemoryTier(rowScale) switch
            {
                "Smoke" => 1_024.0,
                "Standard" => 4_096.0,
                _ when string.Equals(Environment.GetEnvironmentVariable("CERT_CERTIFICATION_TIER"),
                    "Huge", StringComparison.OrdinalIgnoreCase) => 16_384.0,
                _ => 8_192.0
            };

            return Math.Round(bound, 1);
        }

        private void EmitMetrics(string scenario, int rowCount, long elapsedMs,
            long spillBytes, long resultRows, decimal checksum, bool passed,
            ITelemetryContext? telemetry = null)
        {
            var rowScale = RowScale();
            var memoryTier = MemoryTier(rowScale);
            var certificationTier = Environment.GetEnvironmentVariable("CERT_CERTIFICATION_TIER");
            if (string.IsNullOrWhiteSpace(certificationTier))
            {
                certificationTier = memoryTier;
            }

            var resources = _resourceSampler.SnapshotAndReset();
            const double bytesPerMb = 1024.0 * 1024.0;
            var peakWorkingSetMB = Math.Round(resources.PeakWorkingSetBytes / bytesPerMb, 1);
            var peakPrivateBytesMB = Math.Round(resources.PeakPrivateBytes / bytesPerMb, 1);
            var peakManagedHeapMB = Math.Round(resources.PeakManagedHeapBytes / bytesPerMb, 1);
            var allocatedMB = Math.Round(resources.AllocatedBytes / bytesPerMb, 1);
            var memoryBoundMB = MemoryBoundMB(rowScale);
            var rowsPerSecond = elapsedMs <= 0 ? 0 : Math.Round(rowCount / (elapsedMs / 1000.0), 1);
            var memoryPassed = peakWorkingSetMB <= memoryBoundMB;
            var minimumThroughput = double.TryParse(
                Environment.GetEnvironmentVariable("CERT_MIN_ROWS_PER_SECOND"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var configuredThroughput)
                && configuredThroughput > 0 ? configuredThroughput : (double?)null;
            var throughputPassed = minimumThroughput.HasValue
                ? rowsPerSecond >= minimumThroughput.Value
                : (bool?)null;
            var certificationPassed = passed && memoryPassed && throughputPassed != false;

            var metrics = new
            {
                scenario,
                tier = certificationTier,
                memoryTier,
                rowCount,
                elapsedMs,
                rowsPerSecond,
                spillBytes,
                spillWriteBytes = spillBytes,
                spillReadBytes = telemetry?.SpillReadBytes ?? 0,
                spillExtentCount = telemetry?.SpillExtentCount ?? 0,
                partitionPassCount = telemetry?.PartitionPassCount ?? 0,
                resultRows,
                checksum,
                peakProcessWorkingSetMB = peakWorkingSetMB,
                peakPrivateBytesMB,
                peakManagedHeapMB,
                allocatedMB,
                gcGen0Collections = resources.Gen0Collections,
                gcGen1Collections = resources.Gen1Collections,
                gcGen2Collections = resources.Gen2Collections,
                gcPauseMs = Math.Round(resources.GcPauseTime.TotalMilliseconds, 1),
                cpuTimeMs = Math.Round(resources.CpuTime.TotalMilliseconds, 1),
                cpuUtilizationPercent = resources.CpuUtilizationPercent,
                serverGcEnabled = GCSettings.IsServerGC,
                memoryBoundMB,
                memoryMetric = "peak process working set",
                minimumRowsPerSecond = minimumThroughput,
                correctnessPassed = passed,
                memoryPassed,
                throughputPassed,
                passed = certificationPassed
            };
            _out.WriteLine("CERT_METRIC:" + JsonSerializer.Serialize(metrics));

            Assert.True(memoryPassed,
                $"{scenario} peak process working set {peakWorkingSetMB} MB exceeded {memoryTier} tier bound {memoryBoundMB} MB. " +
                "Set CERT_MEMORY_BOUND_MB to an explicit machine-specific bound when certifying on constrained agents.");
            Assert.True(throughputPassed != false,
                $"{scenario} throughput {rowsPerSecond} rows/s was below the configured minimum of {minimumThroughput} rows/s.");
        }

        [Fact]
        [Trait("Tier", "Standard")]
        public Task Cert_Standard_SmokeScenarioSet_RowScale10()
            => RunSmokeScenarioSetWithScale(RowScaleFrom("CERT_STANDARD_ROW_SCALE", 10.0), "Standard");

        [Fact]
        [Trait("Tier", "Stress")]
        public Task Cert_Stress_SmokeScenarioSet_RowScale100()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CERT_STRESS_ROW_SCALE")))
            {
                _out.WriteLine("SKIP: Set CERT_STRESS_ROW_SCALE to run stress-tier scale tests (100x = 5M+ rows).");
                return Task.CompletedTask;
            }
            return RunSmokeScenarioSetWithScale(RowScaleFrom("CERT_STRESS_ROW_SCALE", 100.0), "Stress");
        }

        [Fact]
        [Trait("Tier", "Huge")]
        public Task Cert_Huge_SmokeScenarioSet_RowScale1000()
        {
            // ~50M+ rows (1000x of the 50k base). Very heavy — opt-in only, and needs a capable host
            // (lots of RAM, free disk for spill, and time). Run with a real 50M tier to measure the
            // large-tier behavior (external sort merge fan-in, DISTINCT high-cardinality partitions,
            // statistical/holistic aggregate buffering, external-aggregate spill).
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CERT_HUGE_ROW_SCALE")))
            {
                _out.WriteLine("SKIP: Set CERT_HUGE_ROW_SCALE to run huge-tier scale tests (1000x ~= 50M+ rows; capable host required).");
                return Task.CompletedTask;
            }
            return RunSmokeScenarioSetWithScale(RowScaleFrom("CERT_HUGE_ROW_SCALE", 1000.0), "Huge");
        }

        [Fact]
        [Trait("Tier", "Provider")]
        [Trait("CertificationClass", "LocalReal")]
        public Task Cert_Provider_LocalFileConnectors_RowScale1()
            => RunProviderScenarioSetWithScale(RowScaleFrom("CERT_PROVIDER_ROW_SCALE", 1.0));

        // ── 1. External Sort (ORDER BY) ───────────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_ExternalSort_50kRows_AllRowsMaterialized()
        {
            var Rows = ScaleRows(50_000);
            // Expected sum of 1..N = N*(N+1)/2
            var expectedSum = (decimal)Rows * (Rows + 1) / 2;

            var ev = await EvWithRows(Rows);
            // Force multiple runs at every tier without pinning large-tier certification to the
            // tiny smoke run size. Cap at the production default so the measurement remains honest.
            ev.ExternalSortChunkSize = Math.Min(100_000, Math.Max(5_000, Rows / 64));

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            // Sort into a temp table so we can run aggregate verification queries against it.
            await ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #sorted FROM #cert ORDER BY val DESC;"));

            sw.Stop();

            var countRow = await AggQuery(ev, "SELECT COUNT(*) AS n FROM #sorted;");
            var aggRow = await AggQuery(ev, "SELECT MIN(val) AS mn, MAX(val) AS mx, SUM(val) AS s FROM #sorted;");

            var n = Convert.ToInt64(countRow["n"]);
            var mn = Convert.ToDecimal(aggRow["mn"]);
            var mx = Convert.ToDecimal(aggRow["mx"]);
            var s = Convert.ToDecimal(aggRow["s"]);

            Assert.Equal(Rows, n);
            Assert.Equal(1m, mn);
            Assert.Equal((decimal)Rows, mx);
            Assert.Equal(expectedSum, s);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "ExternalSort");
            EmitMetrics($"ExternalSort_{Rows}_DESC", Rows, sw.ElapsedMilliseconds, spillBytes, n, s, true, ev.Telemetry);
        }

        // ── 2. External Aggregate (GROUP BY) ─────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_ExternalAggregate_100kRows_CorrectSums()
        {
            var Rows = ScaleRows(100_000);
            const int Groups = 10;
            var ev = await EvWithRows(Rows, Groups);
            ev.OperatorMemoryGrantMB = 1;  // small grant forces ExternalAggregateEngine

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            var res = await ev.ExecuteQuery(
                TestHelpers.Parse("SELECT grp, SUM(val) AS total, COUNT(*) AS cnt FROM #cert GROUP BY grp ORDER BY grp;").Statements[0])
                .FirstAsync();

            sw.Stop();

            // 10 groups × 10k rows each → all 10 groups fit in one batch
            Assert.Equal(Groups, res.Rows.Count);
            var totalCount = res.Rows.Sum(r => Convert.ToInt32(r["cnt"]));
            Assert.Equal(Rows, totalCount);

            var firstGroupSum = Convert.ToDecimal(res.Rows[0]["total"]);
            Assert.True(firstGroupSum > 0);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "ExternalAggregate");
            EmitMetrics($"ExternalAggregate_{Rows}_10grps", Rows, sw.ElapsedMilliseconds, spillBytes, res.Rows.Count, firstGroupSum, true, ev.Telemetry);
        }

        // ── 3. External Join ──────────────────────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_ExternalJoin_50kRows_CorrectResults()
        {
            var Rows = ScaleRows(50_000);
            // score = id * 2, so SUM(score) = 2 * SUM(1..N) = N*(N+1)
            var expectedScoreSum = (decimal)Rows * (Rows + 1);

            var ev = NewEvaluator();
            ev.JoinSpillThreshold = 5_000;  // force external hash join

            // Stream both join inputs (id = 1..Rows; score = id*2) so they never materialize in
            // memory — at the Huge tier the old in-memory build of both sides was the dominant hog.
            ev.Connections["#certL"] = new StreamingRowSource(Rows,
                ("id", i => (int)(i + 1)),
                ("val", i => "v" + (i + 1)));
            ev.Connections["#certR"] = new StreamingRowSource(Rows,
                ("id", i => (int)(i + 1)),
                ("score", i => (decimal)(i + 1) * 2));

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            // JOIN into temp table for aggregate verification.
            await ev.Evaluate(TestHelpers.Parse(
                "SELECT l.id, r.score INTO #joined FROM #certL l JOIN #certR r ON l.id = r.id;"));

            sw.Stop();

            var aggRow = await AggQuery(ev,
                "SELECT COUNT(*) AS n, MIN(id) AS mn, MAX(id) AS mx, SUM(score) AS s FROM #joined;");

            var n = Convert.ToInt64(aggRow["n"]);
            var mn = Convert.ToInt32(aggRow["mn"]);
            var mx = Convert.ToInt32(aggRow["mx"]);
            var s = Convert.ToDecimal(aggRow["s"]);

            Assert.Equal(Rows, n);
            Assert.Equal(1, mn);
            Assert.Equal(Rows, mx);
            Assert.Equal(expectedScoreSum, s);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "ExternalJoin");
            EmitMetrics($"ExternalJoin_{Rows}_equality", Rows, sw.ElapsedMilliseconds, spillBytes, n, s, true, ev.Telemetry);
        }

        // ── 4. Temp table spill (SELECT INTO) ────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_TempTableSpill_50kRows_CorrectCount()
        {
            var Rows = ScaleRows(50_000);
            var ev = await EvWithRows(Rows);
            // Retain one configured batch, then force all subsequent batches through spill.
            // Gate F raises BatchSize to reduce scheduler/allocation overhead while preserving
            // bounded memory and a complete physical spill/readback validation.
            ev.TempTableSpillThresholdRows = ev.BatchSize;

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            await ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #result FROM #cert;"));
            var countRow = await AggQuery(ev, "SELECT COUNT(*) AS n FROM #result;");

            sw.Stop();

            var n = Convert.ToInt64(countRow["n"]);
            Assert.Equal(Rows, n);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "TempTableSpill");
            EmitMetrics($"TempTableSpill_{Rows}_SELECT_INTO", Rows, sw.ElapsedMilliseconds, spillBytes, n, (decimal)n, n == Rows, ev.Telemetry);
        }

        // ── 5. Streaming SELECT — result cap check ────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_StreamingSelect_ResultCapEnforced()
        {
            var Rows = ScaleRows(100_000);
            var Cap = Math.Min(50_000, Math.Max(1_000, Rows / 2));
            var ev = await EvWithRows(Rows);
            ev.MaxLastResultRows = Cap;

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            await ev.Evaluate(TestHelpers.Parse("SELECT grp, val FROM #cert;"));

            sw.Stop();

            Assert.NotNull(ev.LastResult);
            Assert.True(ev.LastResult.Rows.Count <= Cap,
                $"Result rows {ev.LastResult.Rows.Count} exceeded cap {Cap}");

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            EmitMetrics($"StreamingSelect_{Rows}_cap{Cap}", Rows, sw.ElapsedMilliseconds, spillBytes,
                ev.LastResult.Rows.Count, (decimal)ev.LastResult.Rows.Count, ev.LastResult.Rows.Count <= Cap, ev.Telemetry);
        }

        // ── 6. Window function at scale ───────────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_WindowFunction_50kRows_CorrectRankValues()
        {
            var Rows = ScaleRows(50_000);
            // SUM of ROW_NUMBERs 1..N = N*(N+1)/2
            var expectedRnSum = (decimal)Rows * (Rows + 1) / 2;

            var ev = await EvWithRows(Rows, groups: 1);  // single group → single partition
            ev.WindowSpillThreshold = 5_000;  // force window spill

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            await ev.Evaluate(TestHelpers.Parse(
                "SELECT val, ROW_NUMBER() OVER (ORDER BY val) AS rn INTO #windowed FROM #cert;"));

            sw.Stop();

            var aggRow = await AggQuery(ev,
                "SELECT COUNT(*) AS n, MIN(rn) AS mn, MAX(rn) AS mx, SUM(rn) AS s FROM #windowed;");

            var n = Convert.ToInt64(aggRow["n"]);
            var mn = Convert.ToDecimal(aggRow["mn"]);
            var mx = Convert.ToDecimal(aggRow["mx"]);
            var s = Convert.ToDecimal(aggRow["s"]);

            Assert.Equal(Rows, n);
            Assert.Equal(1m, mn);
            Assert.Equal((decimal)Rows, mx);
            Assert.Equal(expectedRnSum, s);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "WindowFunction");
            EmitMetrics($"WindowFunction_ROW_NUMBER_{Rows}", Rows, sw.ElapsedMilliseconds, spillBytes, n, s, true, ev.Telemetry);
        }

        // ── 7. CSV ingest ────────────────────────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        [Trait("CertificationClass", "LocalReal")]
        [Trait("Connector", "CSV")]
        public async Task Cert_Smoke_CsvIngest_50kRows_CorrectChecksum()
        {
            var Rows = ScaleRows(50_000);
            var expectedSum = (decimal)Rows * (Rows + 1) / 2;
            var dir = CreateTempDir();
            var path = Path.Combine(dir, "cert.csv");

            try
            {
                var source = await SourceWithRows(Rows);
                var writer = new FlatFileDataSource(SystemExecutionContext.Instance, path,
                    new Dictionary<string, string> { ["HEADER"] = "ON" });
                await writer.WriteBatches(source.ReadBatches(10_000));

                var reader = new FlatFileDataSource(SystemExecutionContext.Instance, path,
                    new Dictionary<string, string> { ["HEADER"] = "ON" });

                var sw = Stopwatch.StartNew();
                var (count, sum) = await CountAndSum(reader.ReadBatches(10_000));
                sw.Stop();

                Assert.Equal(Rows, count);
                Assert.Equal(expectedSum, sum);
                EmitMetrics($"CsvIngest_{Rows}", Rows, sw.ElapsedMilliseconds, 0, count, sum, true);
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        // ── 8. Parquet round-trip ────────────────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        [Trait("CertificationClass", "LocalReal")]
        [Trait("Connector", "PARQUET")]
        public async Task Cert_Smoke_ParquetRoundTrip_50kRows_CorrectChecksum()
        {
            var Rows = ScaleRows(50_000);
            var expectedSum = (decimal)Rows * (Rows + 1) / 2;
            var dir = CreateTempDir();
            var path = Path.Combine(dir, "cert.parquet");

            try
            {
                var source = await SourceWithRows(Rows);
                var writer = new ParquetDataSource(SystemExecutionContext.Instance, path);
                await writer.WriteBatches(source.ReadBatches(10_000));

                var reader = new ParquetDataSource(SystemExecutionContext.Instance, path);

                var sw = Stopwatch.StartNew();
                var (count, sum) = await CountAndSum(reader.ReadBatches(10_000));
                sw.Stop();

                Assert.Equal(Rows, count);
                Assert.Equal(expectedSum, sum);
                EmitMetrics($"ParquetRoundTrip_{Rows}", Rows, sw.ElapsedMilliseconds, 0, count, sum, true);
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        // ── 9. Report dataset Parquet cache ──────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        [Trait("CertificationClass", "LocalReal")]
        [Trait("Connector", "PARQUET")]
        public async Task Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum()
        {
            var Rows = ScaleRows(50_000);
            var expectedSum = (decimal)Rows * (Rows + 1) / 2;
            var dir = CreateTempDir();
            var reportDir = Path.Combine(dir, "reports");
            Directory.CreateDirectory(reportDir);

            try
            {
                var registry = new InMemoryDatasetRegistry(Path.Combine(dir, "datasets"));
                var scriptPath = Path.Combine(reportDir, "cert.rptsql");

                var first = await EvWithRows(Rows);
                first.DatasetRegistry = registry;
                first.CurrentScriptPath = scriptPath;

                var sw = Stopwatch.StartNew();
                await first.Evaluate(TestHelpers.Parse("""
                    CREATE DATASET &cert TTL = '1h' AS (
                        SELECT grp, val FROM #cert
                    );
                    """));
                sw.Stop();

                var metadata = await registry.Lookup("&cert", "IsAdmin=true");
                Assert.NotNull(metadata);
                Assert.True(File.Exists(metadata!.ParquetFilePath));
                Assert.Equal(Rows, metadata.RowCount);

                var second = NewEvaluator();
                second.DatasetRegistry = registry;
                second.CurrentScriptPath = scriptPath;

                var reloadSw = Stopwatch.StartNew();
                await second.Evaluate(TestHelpers.Parse("""
                    CREATE DATASET &cert TTL = '1h' AS (
                        SELECT 0 AS grp, 0 AS val
                    );
                    """));
                reloadSw.Stop();

                var row = await AggQuery(second, "SELECT COUNT(*) AS n, SUM(val) AS s FROM &cert;");
                var count = Convert.ToInt64(row["n"]);
                var sum = Convert.ToDecimal(row["s"]);

                Assert.Equal(Rows, count);
                Assert.Equal(expectedSum, sum);
                EmitMetrics($"ReportDatasetSnapshotReload_{Rows}", Rows, sw.ElapsedMilliseconds + reloadSw.ElapsedMilliseconds, 0, count, sum, true);
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        // ── 10. Grouping sets / CUBE at scale ───────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_CubeGroupingSets_50kRows_CorrectExpansionAndChecksum()
        {
            var Rows = ScaleRows(50_000);
            const int Groups = 10;
            const int Buckets = 5;
            var expectedInputSum = (decimal)Rows * (Rows + 1) / 2;
            var expectedCubeRows = Groups * Buckets + Groups + Buckets + 1;

            var ev = NewEvaluator();
            ev.Connections["#cube_src"] = await SourceWithCubeRows(Rows, Groups, Buckets);
            ev.OperatorMemoryGrantMB = 1;

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            await ev.Evaluate(TestHelpers.Parse("""
                SELECT grp, bucket, SUM(val) AS total
                INTO #cube_result
                FROM #cube_src
                GROUP BY CUBE(grp, bucket);
                """));

            sw.Stop();

            var row = await AggQuery(ev, "SELECT COUNT(*) AS n, SUM(total) AS s FROM #cube_result;");
            var count = Convert.ToInt64(row["n"]);
            var sum = Convert.ToDecimal(row["s"]);

            Assert.Equal(expectedCubeRows, count);
            Assert.Equal(expectedInputSum * 4, sum);
            AssertSpilled(ev, "CubeGroupingSets");
            EmitMetrics($"CubeGroupingSets_{Rows}_{Groups}x{Buckets}", Rows, sw.ElapsedMilliseconds,
                ev.Telemetry.TotalSpilledBytes, count, sum, true, ev.Telemetry);
        }

        // ── 11. Scalar subquery cache at scale ───────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_ScalarSubqueryCache_50kRows_ReusesRepeatedKeys()
        {
            var Rows = ScaleRows(50_000);
            var distinctKeys = Math.Min(1_000, Rows);
            decimal expectedScoreSum = 0;

            var fact = new DataTable();
            fact.SetColumns(new[] { "id", "val" });
            for (int i = 0; i < Rows; i++)
            {
                var id = i % distinctKeys + 1;
                expectedScoreSum += id * 2m;

                var r = new Row(fact.Schema);
                r["id"] = id;
                r["val"] = (decimal)(i + 1);
                await fact.AddRowAsync(r);
            }

            var lookup = new DataTable();
            lookup.SetColumns(new[] { "id", "score" });
            for (int i = 1; i <= distinctKeys; i++)
            {
                var r = new Row(lookup.Schema);
                r["id"] = i;
                r["score"] = i * 2m;
                await lookup.AddRowAsync(r);
            }

            var ev = NewEvaluator();
            var factSrc = new InMemoryDataSource();
            await factSrc.WriteBatches(new[] { fact }.ToAsyncEnumerable());
            ev.Connections["#fact"] = factSrc;

            var lookupSrc = new InMemoryDataSource();
            await lookupSrc.WriteBatches(new[] { lookup }.ToAsyncEnumerable());
            ev.Connections["#lookup"] = lookupSrc;

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            await ev.Evaluate(TestHelpers.Parse("""
                SELECT id,
                       (SELECT score FROM #lookup l WHERE l.id = #fact.id) AS score
                INTO #subq_result
                FROM #fact;
                """));

            sw.Stop();

            var row = await AggQuery(ev, "SELECT COUNT(*) AS n, SUM(score) AS s FROM #subq_result;");
            var count = Convert.ToInt64(row["n"]);
            var sum = Convert.ToDecimal(row["s"]);

            Assert.Equal(Rows, count);
            Assert.Equal(expectedScoreSum, sum);
            Assert.Equal(distinctKeys, ev.Telemetry.SubqueryCacheMisses);
            Assert.Equal(Rows - distinctKeys, ev.Telemetry.SubqueryCacheHits);
            EmitMetrics($"ScalarSubqueryCache_{Rows}_{distinctKeys}keys", Rows, sw.ElapsedMilliseconds,
                ev.Telemetry.TotalSpilledBytes, count, sum, true, ev.Telemetry);
        }

        // ── 12. Spill cleanup after success ─────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_SpillCleanup_AfterSuccessfulTempSpill_RemovesNonPersistentFiles()
        {
            var Rows = ScaleRows(50_000);
            var ev = await EvWithRows(Rows);
            ev.IsPersistentSession = false;
            ev.TempTableSpillThresholdRows = 10_000;

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();
            await ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #cleanup_result FROM #cert;"));
            sw.Stop();

            var spillRoot = ev.SpillStore.RootPath;
            var filesBeforeDispose = CountFiles(spillRoot);
            AssertSpilled(ev, "SpillCleanupSuccess");
            Assert.True(filesBeforeDispose > 0, "Expected spill files before evaluator disposal.");

            await ev.DisposeAsync();

            Assert.False(Directory.Exists(spillRoot), $"Expected spill directory '{spillRoot}' to be removed after evaluator disposal.");
            EmitMetrics($"SpillCleanupSuccess_{Rows}", Rows, sw.ElapsedMilliseconds,
                ev.Telemetry.TotalSpilledBytes, filesBeforeDispose, filesBeforeDispose, true, ev.Telemetry);
        }

        // ── 13. Spill cleanup after forced failure ──────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_SpillCleanup_AfterFailedTempSpill_RemovesNonPersistentFiles()
        {
            var Rows = ScaleRows(50_000);
            var ev = NewEvaluator();
            ev.IsPersistentSession = false;
            ev.TempTableSpillThresholdRows = 1_000;
            ev.Connections["#faulty"] = new ThrowingBatchDataSource(Rows, batchSize: 5_000, throwAfterBatches: 3);

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<ExecutionException>(() =>
                ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #failed_cleanup FROM #faulty;")));
            sw.Stop();

            var spillRoot = ev.SpillStore.RootPath;
            var filesBeforeDispose = CountFiles(spillRoot);
            AssertSpilled(ev, "SpillCleanupFailure");
            Assert.Equal(0, filesBeforeDispose); // incomplete extent is deleted eagerly on failure

            await ev.DisposeAsync();

            Assert.False(Directory.Exists(spillRoot), $"Expected spill directory '{spillRoot}' to be removed after failed evaluator disposal.");
            EmitMetrics($"SpillCleanupFailure_{Rows}", Rows, sw.ElapsedMilliseconds,
                ev.Telemetry.TotalSpilledBytes, filesBeforeDispose, filesBeforeDispose, true, ev.Telemetry);
        }

        private static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etl-scale-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteTempDir(string dir)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        private sealed class InMemoryDatasetRegistry : IDatasetRegistry
        {
            private readonly string _root;
            private readonly Dictionary<string, DatasetMetadata> _items = new();
            private int _nextId = 1;

            public InMemoryDatasetRegistry(string root)
            {
                _root = root;
                Directory.CreateDirectory(_root);
            }

            public Task<int> RegisterOrUpdate(DatasetMetadata metadata)
            {
                if (_items.TryGetValue(metadata.Name, out var existing))
                    metadata.Id = existing.Id;
                else if (metadata.Id == 0)
                    metadata.Id = _nextId++;

                _items[metadata.Name] = metadata;
                return Task.FromResult(metadata.Id);
            }

            public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "")
            {
                _items.TryGetValue(name, out var metadata);
                return Task.FromResult(metadata);
            }

            public Task<bool> Exists(string name)
                => Task.FromResult(_items.ContainsKey(name));

            // Scale tests run as admin and don't exercise edit-gating; allow edits.
            public Task<bool> CanEditAsync(string name, string callerPermissions)
                => Task.FromResult(_items.ContainsKey(name));

            public Task SetStale(string name)
            {
                if (_items.TryGetValue(name, out var metadata))
                    metadata.LastRefresh = null;
                return Task.CompletedTask;
            }

            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
                => Task.FromResult<IEnumerable<DatasetMetadata>>(_items.Values.ToList());

            public Task Delete(string name)
            {
                _items.Remove(name);
                return Task.CompletedTask;
            }

            public string BuildDatasetFilePath(int datasetId, string name)
            {
                var safeName = name.TrimStart('&', '#').Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
                Directory.CreateDirectory(_root);
                return Path.Combine(_root, $"{safeName}_{datasetId}.parquet");
            }
        }

        private sealed class ThrowingBatchDataSource : IDataSource
        {
            private readonly int _rowCount;
            private readonly int _batchSize;
            private readonly int _throwAfterBatches;

            public ThrowingBatchDataSource(int rowCount, int batchSize, int throwAfterBatches)
            {
                _rowCount = rowCount;
                _batchSize = batchSize;
                _throwAfterBatches = throwAfterBatches;
            }

            public string Path => "";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "FAULTY";

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
            {
                var emitted = 0;
                var batchNumber = 0;
                while (emitted < _rowCount)
                {
                    if (batchNumber >= _throwAfterBatches)
                        throw new ExecutionException("Forced scale certification source failure.");

                    var take = Math.Min(_batchSize, _rowCount - emitted);
                    var table = new DataTable();
                    table.SetColumns(new[] { "grp", "val" });

                    for (int i = 0; i < take; i++)
                    {
                        var r = new Row(table.Schema);
                        r["grp"] = (emitted + i) % 10;
                        r["val"] = (decimal)(emitted + i + 1);
                        await table.AddRowAsync(r);
                    }

                    emitted += take;
                    batchNumber++;
                    yield return table;
                }
            }

            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
                => throw new NotSupportedException();

            public Task<IEnumerable<string>> GetColumnsAsync()
                => Task.FromResult<IEnumerable<string>>(new[] { "grp", "val" });

            public IDataSource WithTable(string tableName) => this;
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
