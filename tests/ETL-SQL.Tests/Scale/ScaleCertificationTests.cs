using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    public class ScaleCertificationTests
    {
        private readonly ITestOutputHelper _out;
        private readonly double _memoryBaselineMB;

        public ScaleCertificationTests(ITestOutputHelper output)
        {
            _out = output;
            _memoryBaselineMB = GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

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

        private static async Task<InMemoryDataSource> SourceWithRows(int rowCount, int groups = 10)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "grp", "val" });

            for (int i = 0; i < rowCount; i++)
            {
                var r = new Row(table.Schema);
                r["grp"] = i % groups;
                r["val"] = (decimal)(i + 1);
                await table.AddRowAsync(r);
            }

            var src = new InMemoryDataSource();
            await src.WriteBatches(new[] { table }.ToAsyncEnumerable());
            return src;
        }

        private static async Task<InMemoryDataSource> SourceWithCubeRows(int rowCount, int groups = 10, int buckets = 5)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "grp", "bucket", "val" });

            for (int i = 0; i < rowCount; i++)
            {
                var r = new Row(table.Schema);
                r["grp"] = i % groups;
                r["bucket"] = (i / groups) % buckets;
                r["val"] = (decimal)(i + 1);
                await table.AddRowAsync(r);
            }

            var src = new InMemoryDataSource();
            await src.WriteBatches(new[] { table }.ToAsyncEnumerable());
            return src;
        }

        private static async Task<DataTable> TableWithRows(int rowCount, int groups = 10)
        {
            var src = await SourceWithRows(rowCount, groups);
            return await src.ReadBatches(rowCount).FirstAsync();
        }

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

            try
            {
                await Cert_Smoke_ExternalSort_50kRows_AllRowsMaterialized();
                await Cert_Smoke_ExternalAggregate_100kRows_CorrectSums();
                await Cert_Smoke_ExternalJoin_50kRows_CorrectResults();
                await Cert_Smoke_TempTableSpill_50kRows_CorrectCount();
                await Cert_Smoke_StreamingSelect_ResultCapEnforced();
                await Cert_Smoke_WindowFunction_50kRows_CorrectRankValues();
                await Cert_Smoke_CsvIngest_50kRows_CorrectChecksum();
                await Cert_Smoke_ParquetRoundTrip_50kRows_CorrectChecksum();
                await Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum();
                await Cert_Smoke_CubeGroupingSets_50kRows_CorrectExpansionAndChecksum();
                await Cert_Smoke_ScalarSubqueryCache_50kRows_ReusesRepeatedKeys();
                await Cert_Smoke_SpillCleanup_AfterSuccessfulTempSpill_RemovesNonPersistentFiles();
                await Cert_Smoke_SpillCleanup_AfterFailedTempSpill_RemovesNonPersistentFiles();
            }
            finally
            {
                Environment.SetEnvironmentVariable("CERT_ROW_SCALE", previous);
                Environment.SetEnvironmentVariable("CERT_CERTIFICATION_TIER", previousTier);
            }
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

        private static double MemoryBoundMB(int rowCount, double rowScale)
        {
            var raw = Environment.GetEnvironmentVariable("CERT_MEMORY_BOUND_MB");
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var configured) && configured > 0)
            {
                return Math.Round(configured, 1);
            }

            var bound = MemoryTier(rowScale) switch
            {
                "Smoke" => Math.Max(512.0, rowCount * 0.02),
                "Standard" => Math.Max(2_048.0, rowCount * 0.012),
                _ => Math.Max(8_192.0, rowCount * 0.008)
            };

            return Math.Round(bound, 1);
        }

        private void EmitMetrics(string scenario, int rowCount, long elapsedMs,
            long spillBytes, long resultRows, decimal checksum, bool passed)
        {
            var rowScale = RowScale();
            var memoryTier = MemoryTier(rowScale);
            var certificationTier = Environment.GetEnvironmentVariable("CERT_CERTIFICATION_TIER");
            if (string.IsNullOrWhiteSpace(certificationTier))
            {
                certificationTier = memoryTier;
            }

            var managedMemoryMB = Math.Round(
                Math.Max(0.0, GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0) - _memoryBaselineMB), 1);
            var memoryBoundMB = MemoryBoundMB(rowCount, rowScale);

            Assert.True(managedMemoryMB <= memoryBoundMB,
                $"{scenario} managed memory {managedMemoryMB} MB exceeded {memoryTier} tier bound {memoryBoundMB} MB. " +
                "Set CERT_MEMORY_BOUND_MB to an explicit machine-specific bound when certifying on constrained agents.");

            var metrics = new
            {
                scenario,
                tier = certificationTier,
                memoryTier,
                rowCount,
                elapsedMs,
                spillBytes,
                resultRows,
                checksum,
                peakManagedMemoryMB = managedMemoryMB,
                memoryBoundMB,
                passed
            };
            _out.WriteLine("CERT_METRIC:" + JsonSerializer.Serialize(metrics));
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
            ev.ExternalSortChunkSize = 5_000;  // force multiple sort chunks

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            // Sort into a temp table so we can run aggregate verification queries against it.
            await ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #sorted FROM #cert ORDER BY val DESC;"));

            sw.Stop();

            var countRow = await AggQuery(ev, "SELECT COUNT(*) AS n FROM #sorted;");
            var aggRow   = await AggQuery(ev, "SELECT MIN(val) AS mn, MAX(val) AS mx, SUM(val) AS s FROM #sorted;");

            var n  = Convert.ToInt64(countRow["n"]);
            var mn = Convert.ToDecimal(aggRow["mn"]);
            var mx = Convert.ToDecimal(aggRow["mx"]);
            var s  = Convert.ToDecimal(aggRow["s"]);

            Assert.Equal(Rows, n);
            Assert.Equal(1m, mn);
            Assert.Equal((decimal)Rows, mx);
            Assert.Equal(expectedSum, s);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "ExternalSort");
            EmitMetrics($"ExternalSort_{Rows}_DESC", Rows, sw.ElapsedMilliseconds, spillBytes, n, s, true);
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
            EmitMetrics($"ExternalAggregate_{Rows}_10grps", Rows, sw.ElapsedMilliseconds, spillBytes, res.Rows.Count, firstGroupSum, true);
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

            var left = new DataTable();
            left.SetColumns(new[] { "id", "val" });
            var right = new DataTable();
            right.SetColumns(new[] { "id", "score" });

            for (int i = 1; i <= Rows; i++)
            {
                var lr = new Row(left.Schema);
                lr["id"] = i; lr["val"] = $"v{i}";
                await left.AddRowAsync(lr);

                var rr = new Row(right.Schema);
                rr["id"] = i; rr["score"] = (decimal)i * 2;
                await right.AddRowAsync(rr);
            }

            var lSrc = new InMemoryDataSource();
            await lSrc.WriteBatches(new[] { left }.ToAsyncEnumerable());
            ev.Connections["#certL"] = lSrc;

            var rSrc = new InMemoryDataSource();
            await rSrc.WriteBatches(new[] { right }.ToAsyncEnumerable());
            ev.Connections["#certR"] = rSrc;

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            // JOIN into temp table for aggregate verification.
            await ev.Evaluate(TestHelpers.Parse(
                "SELECT l.id, r.score INTO #joined FROM #certL l JOIN #certR r ON l.id = r.id;"));

            sw.Stop();

            var aggRow = await AggQuery(ev,
                "SELECT COUNT(*) AS n, MIN(id) AS mn, MAX(id) AS mx, SUM(score) AS s FROM #joined;");

            var n  = Convert.ToInt64(aggRow["n"]);
            var mn = Convert.ToInt32(aggRow["mn"]);
            var mx = Convert.ToInt32(aggRow["mx"]);
            var s  = Convert.ToDecimal(aggRow["s"]);

            Assert.Equal(Rows, n);
            Assert.Equal(1, mn);
            Assert.Equal(Rows, mx);
            Assert.Equal(expectedScoreSum, s);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "ExternalJoin");
            EmitMetrics($"ExternalJoin_{Rows}_equality", Rows, sw.ElapsedMilliseconds, spillBytes, n, s, true);
        }

        // ── 4. Temp table spill (SELECT INTO) ────────────────────────────────

        [Fact]
        [Trait("Tier", "Smoke")]
        public async Task Cert_Smoke_TempTableSpill_50kRows_CorrectCount()
        {
            var Rows = ScaleRows(50_000);
            var ev = await EvWithRows(Rows);
            ev.TempTableSpillThresholdRows = 10_000;  // force temp table spill

            ev.Telemetry.Clear();
            var sw = Stopwatch.StartNew();

            await ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #result FROM #cert;"));
            var countRow = await AggQuery(ev, "SELECT COUNT(*) AS n FROM #result;");

            sw.Stop();

            var n = Convert.ToInt64(countRow["n"]);
            Assert.Equal(Rows, n);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "TempTableSpill");
            EmitMetrics($"TempTableSpill_{Rows}_SELECT_INTO", Rows, sw.ElapsedMilliseconds, spillBytes, n, (decimal)n, n == Rows);
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
                ev.LastResult.Rows.Count, (decimal)ev.LastResult.Rows.Count, ev.LastResult.Rows.Count <= Cap);
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

            var n  = Convert.ToInt64(aggRow["n"]);
            var mn = Convert.ToDecimal(aggRow["mn"]);
            var mx = Convert.ToDecimal(aggRow["mx"]);
            var s  = Convert.ToDecimal(aggRow["s"]);

            Assert.Equal(Rows, n);
            Assert.Equal(1m, mn);
            Assert.Equal((decimal)Rows, mx);
            Assert.Equal(expectedRnSum, s);

            var spillBytes = ev.Telemetry.TotalSpilledBytes;
            AssertSpilled(ev, "WindowFunction");
            EmitMetrics($"WindowFunction_ROW_NUMBER_{Rows}", Rows, sw.ElapsedMilliseconds, spillBytes, n, s, true);
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
                var source = await TableWithRows(Rows);
                var writer = new FlatFileDataSource(SystemExecutionContext.Instance, path,
                    new Dictionary<string, string> { ["HEADER"] = "ON" });
                await writer.WriteBatches(new[] { source }.ToAsyncEnumerable());

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
                var source = await TableWithRows(Rows);
                var writer = new ParquetDataSource(SystemExecutionContext.Instance, path);
                await writer.WriteBatches(new[] { source }.ToAsyncEnumerable());

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

                var metadata = await registry.Lookup("&cert", reportDir, "IsAdmin=true");
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
                ev.Telemetry.TotalSpilledBytes, count, sum, true);
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
                ev.Telemetry.TotalSpilledBytes, count, sum, true);
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
                ev.Telemetry.TotalSpilledBytes, filesBeforeDispose, filesBeforeDispose, true);
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
            Assert.True(filesBeforeDispose > 0, "Expected spill files before evaluator disposal after forced failure.");

            await ev.DisposeAsync();

            Assert.False(Directory.Exists(spillRoot), $"Expected spill directory '{spillRoot}' to be removed after failed evaluator disposal.");
            EmitMetrics($"SpillCleanupFailure_{Rows}", Rows, sw.ElapsedMilliseconds,
                ev.Telemetry.TotalSpilledBytes, filesBeforeDispose, filesBeforeDispose, true);
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
            private readonly Dictionary<(string Name, string Folder), DatasetMetadata> _items = new();

            public InMemoryDatasetRegistry(string root)
            {
                _root = root;
                Directory.CreateDirectory(_root);
            }

            public Task RegisterOrUpdate(DatasetMetadata metadata)
            {
                _items[(metadata.Name, metadata.FolderPath)] = metadata;
                return Task.CompletedTask;
            }

            public Task<DatasetMetadata?> Lookup(string name, string folderPath, string callerPermissions = "")
            {
                _items.TryGetValue((name, folderPath), out var metadata);
                return Task.FromResult(metadata);
            }

            public Task<bool> Exists(string name, string folderPath)
                => Task.FromResult(_items.ContainsKey((name, folderPath)));

            public Task SetStale(string name, string folderPath)
            {
                if (_items.TryGetValue((name, folderPath), out var metadata))
                    metadata.LastRefresh = null;
                return Task.CompletedTask;
            }

            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
                => Task.FromResult<IEnumerable<DatasetMetadata>>(_items.Values.ToList());

            public Task Delete(string name, string folderPath)
            {
                _items.Remove((name, folderPath));
                return Task.CompletedTask;
            }

            public string BuildDatasetFilePath(string name, string folderPath)
            {
                var safeFolder = folderPath.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_').Trim('_');
                var safeName = name.TrimStart('&', '#').Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
                var dir = Path.Combine(_root, safeFolder.Length == 0 ? "root" : safeFolder);
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, safeName + ".parquet");
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
