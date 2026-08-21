using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;

namespace ETL_SQL.Tests.Reporting.PayloadCrossover;

public enum WorkloadType
{
    DenseNumeric,
    MixedTyped,
    NullableSparse,
    TemporalEvents,
    StringHeavy
}

public enum PayloadFormat
{
    JsonRowOriented,
    JsonColumnar,
    ArrowIpcStream
}

public record DatasetSchema(
    string Name,
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<IArrowType> ColumnTypes);

public record TableData(
    DatasetSchema Schema,
    IReadOnlyList<string> Columns,
    IReadOnlyList<List<object?>> Rows);

public record FormatMeasurement(
    PayloadFormat Format,
    long RawBytes,
    long GzipBytes,
    long BrotliBytes,
    double EncodeTimeMs,
    long EncodeAllocatedBytes,
    double DecodeTimeMs,
    long DecodeAllocatedBytes,
    double PostParseFilterTimeMs,
    int FilteredRowCount,
    string ChecksumSha256);

public record WorkloadBenchmarkResult(
    WorkloadType Workload,
    int RowCount,
    IReadOnlyDictionary<PayloadFormat, FormatMeasurement> Formats,
    string WinnerByRawSize,
    string WinnerByGzipSize,
    string WinnerByDecodeTime,
    string WinnerByMemory);

public record MachineMetadata(
    string OSDescription,
    string ProcessArchitecture,
    string FrameworkDescription,
    int ProcessorCount,
    long TotalAvailableMemoryMb,
    bool IsServerGC);

public record PayloadCrossoverBenchmarkReport(
    DateTime TimestampUtc,
    string GitBranch,
    MachineMetadata RuntimeEnvironment,
    IReadOnlyList<WorkloadBenchmarkResult> Results,
    IReadOnlyDictionary<string, string> CrossoverSummaryByWorkload);

public static class PayloadCrossoverMeasurementHarness
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    public static MachineMetadata GetMachineMetadata()
    {
        return new MachineMetadata(
            OSDescription: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessorCount: Environment.ProcessorCount,
            TotalAvailableMemoryMb: GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
            IsServerGC: System.Runtime.GCSettings.IsServerGC);
    }

    public static TableData GenerateWorkloadData(WorkloadType workload, int rowCount)
    {
        var rng = new Random(42 + (int)workload * 1000); // Deterministic seed for reproducible benchmarks

        switch (workload)
        {
            case WorkloadType.DenseNumeric:
                {
                    var columns = new[] { "TimestampMs", "SensorVoltage", "SensorCurrent", "SensorPower", "TemperatureC", "VibrationFrequency" };
                    var types = new IArrowType[] { Int64Type.Default, DoubleType.Default, DoubleType.Default, DoubleType.Default, DoubleType.Default, DoubleType.Default };
                    var schema = new DatasetSchema("DenseNumeric", columns, types);
                    var rows = new List<List<object?>>(rowCount);
                    long baseTime = 1704067200000;

                    for (int i = 0; i < rowCount; i++)
                    {
                        rows.Add(new List<object?>
                    {
                        baseTime + (i * 1000L),
                        Math.Round(rng.NextDouble() * 100.0, 4),
                        Math.Round(rng.NextDouble() * 50.0, 4),
                        Math.Round(rng.NextDouble() * 5000.0, 2),
                        Math.Round(rng.NextDouble() * 75.0, 3),
                        Math.Round(rng.NextDouble() * 1000.0, 2)
                    });
                    }
                    return new TableData(schema, columns, rows);
                }

            case WorkloadType.MixedTyped:
                {
                    var columns = new[] { "OrderId", "OrderDate", "CustomerName", "Region", "Quantity", "UnitPrice", "Discount", "IsShipped" };
                    var types = new IArrowType[] { Int32Type.Default, StringType.Default, StringType.Default, StringType.Default, Int32Type.Default, DoubleType.Default, DoubleType.Default, BooleanType.Default };
                    var schema = new DatasetSchema("MixedTyped", columns, types);
                    var rows = new List<List<object?>>(rowCount);
                    var regions = new[] { "North America", "EMEA", "APAC", "LATAM" };
                    var customers = new[] { "Acme Corp", "Globex", "Initech", "Umbrella", "Soylent", "Massive Dynamic", "Stark Ind" };

                    for (int i = 0; i < rowCount; i++)
                    {
                        rows.Add(new List<object?>
                    {
                        10000 + i,
                        $"2026-01-{(i % 28) + 1:D2}",
                        customers[i % customers.Length],
                        regions[i % regions.Length],
                        (i % 50) + 1,
                        Math.Round(19.99 + (rng.NextDouble() * 500.0), 2),
                        Math.Round(rng.NextDouble() * 0.3, 2),
                        i % 3 != 0
                    });
                    }
                    return new TableData(schema, columns, rows);
                }

            case WorkloadType.NullableSparse:
                {
                    var columns = new[] { "ObservationId", "StationName", "PrimaryReading", "SecondaryReading", "PressureHpa", "StatusNote" };
                    var types = new IArrowType[] { Int32Type.Default, StringType.Default, DoubleType.Default, DoubleType.Default, DoubleType.Default, StringType.Default };
                    var schema = new DatasetSchema("NullableSparse", columns, types);
                    var rows = new List<List<object?>>(rowCount);
                    var stations = new[] { "Station_Alpha", "Station_Beta", "Station_Gamma", "Station_Delta" };

                    for (int i = 0; i < rowCount; i++)
                    {
                        object? r1 = (i % 3 == 0) ? null : Math.Round(rng.NextDouble() * 100.0, 3);
                        object? r2 = (i % 2 == 0) ? null : Math.Round(rng.NextDouble() * 50.0, 3);
                        object? pressure = (i % 5 == 0) ? null : Math.Round(980.0 + (rng.NextDouble() * 50.0), 2);
                        object? note = (i % 4 == 0) ? null : (i % 8 == 0 ? "Warning: Variance High" : "Normal");

                        rows.Add(new List<object?>
                    {
                        1000 + i,
                        stations[i % stations.Length],
                        r1,
                        r2,
                        pressure,
                        note
                    });
                    }
                    return new TableData(schema, columns, rows);
                }

            case WorkloadType.TemporalEvents:
                {
                    var columns = new[] { "EventTimestamp", "Hostname", "EventType", "LatencyMs", "HttpStatusCode" };
                    var types = new IArrowType[] { StringType.Default, StringType.Default, StringType.Default, DoubleType.Default, Int32Type.Default };
                    var schema = new DatasetSchema("TemporalEvents", columns, types);
                    var rows = new List<List<object?>>(rowCount);
                    var hosts = new[] { "srv-app-01", "srv-app-02", "srv-app-03", "srv-db-01", "srv-gateway-01" };
                    var events = new[] { "HTTP_REQUEST", "DB_QUERY", "CACHE_HIT", "RPC_CALL", "METRIC_FLUSH" };
                    var statusCodes = new[] { 200, 200, 200, 201, 400, 404, 500 };

                    for (int i = 0; i < rowCount; i++)
                    {
                        rows.Add(new List<object?>
                    {
                        $"2026-02-15T{(i % 24):D2}:{(i % 60):D2}:{(i % 60):D2}.{((i * 17) % 1000):D3}Z",
                        hosts[i % hosts.Length],
                        events[i % events.Length],
                        Math.Round(2.5 + (rng.NextDouble() * 120.0), 3),
                        statusCodes[i % statusCodes.Length]
                    });
                    }
                    return new TableData(schema, columns, rows);
                }

            case WorkloadType.StringHeavy:
                {
                    var columns = new[] { "TicketId", "Department", "AssigneeRole", "SummaryDescription", "TagList" };
                    var types = new IArrowType[] { Int32Type.Default, StringType.Default, StringType.Default, StringType.Default, StringType.Default };
                    var schema = new DatasetSchema("StringHeavy", columns, types);
                    var rows = new List<List<object?>>(rowCount);
                    var depts = new[] { "Infrastructure", "Platform Security", "Data Engineering", "Customer Success", "Core Architecture" };
                    var roles = new[] { "Lead Architect", "Staff SRE", "Principal Data Engineer", "Support Escalation Specialist", "Security Auditor" };
                    var descriptions = new[]
                    {
                    "Detailed root cause analysis into memory pressure and intermittent GC pauses under high ingestion burst.",
                    "Automated migration and zero-downtime cutover plan for regional high-availability tenant cluster.",
                    "Verification and certification of mutual TLS transport handshakes across multi-datacenter nodes.",
                    "Reviewing compliance logs and policy audit violations for external partner API access.",
                    "Scheduled cluster resource rebalancing and container memory boundary tuning."
                };
                    var tags = new[] { "security,p1,audit,gateway", "infra,memory,gc,perf", "database,migration,ha,postgres", "api,auth,oidc,saml" };

                    for (int i = 0; i < rowCount; i++)
                    {
                        rows.Add(new List<object?>
                    {
                        50000 + i,
                        depts[i % depts.Length],
                        roles[i % roles.Length],
                        descriptions[i % descriptions.Length],
                        tags[i % tags.Length]
                    });
                    }
                    return new TableData(schema, columns, rows);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(workload));
        }
    }

    // ── Encoding & Decoding ─────────────────────────────────────────────

    public static byte[] EncodeJsonRowOriented(TableData data)
    {
        var model = new
        {
            columns = data.Columns,
            rows = data.Rows.Select(r => r.Select(c => c?.ToString()).ToList()).ToList()
        };
        return JsonSerializer.SerializeToUtf8Bytes(model, JsonOpts);
    }

    public static List<List<string?>> DecodeJsonRowOriented(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var rowsElem = root.GetProperty("rows");
        var rows = new List<List<string?>>(rowsElem.GetArrayLength());

        foreach (var r in rowsElem.EnumerateArray())
        {
            var row = new List<string?>();
            foreach (var cell in r.EnumerateArray())
            {
                row.Add(cell.ValueKind == JsonValueKind.Null ? null : cell.GetString());
            }
            rows.Add(row);
        }
        return rows;
    }

    public static byte[] EncodeJsonColumnar(TableData data)
    {
        var dict = new Dictionary<string, object?[]>(data.Columns.Count);
        for (int c = 0; c < data.Columns.Count; c++)
        {
            var colName = data.Columns[c];
            var colValues = new object?[data.Rows.Count];
            for (int r = 0; r < data.Rows.Count; r++)
            {
                colValues[r] = data.Rows[r][c];
            }
            dict[colName] = colValues;
        }
        return JsonSerializer.SerializeToUtf8Bytes(dict, JsonOpts);
    }

    public static Dictionary<string, List<object?>> DecodeJsonColumnar(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var result = new Dictionary<string, List<object?>>();

        foreach (var prop in root.EnumerateObject())
        {
            var list = new List<object?>(prop.Value.GetArrayLength());
            foreach (var item in prop.Value.EnumerateArray())
            {
                list.Add(item.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Number => item.TryGetInt64(out var l) ? l : item.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => item.GetString()
                });
            }
            result[prop.Name] = list;
        }
        return result;
    }

    public static byte[] EncodeArrowIpcStream(TableData data)
    {
        var fields = new List<Field>(data.Columns.Count);
        for (int i = 0; i < data.Columns.Count; i++)
        {
            fields.Add(new Field(data.Columns[i], data.Schema.ColumnTypes[i], nullable: true));
        }
        var schema = new Schema(fields, metadata: null);
        var arrays = new List<IArrowArray>(data.Columns.Count);

        for (int c = 0; c < data.Columns.Count; c++)
        {
            var arrowType = data.Schema.ColumnTypes[c];
            arrays.Add(BuildArrowArray(arrowType, data.Rows, c));
        }

        using var ms = new MemoryStream();
        using (var writer = new ArrowStreamWriter(ms, schema, leaveOpen: true))
        {
            writer.WriteStart();
            writer.WriteRecordBatch(new RecordBatch(schema, arrays, data.Rows.Count));
            writer.WriteEnd();
        }
        return ms.ToArray();
    }

    public static RecordBatch DecodeArrowIpcStream(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new ArrowStreamReader(ms);
        return reader.ReadNextRecordBatch()
            ?? throw new InvalidDataException("Arrow stream yielded no record batch");
    }

    private static IArrowArray BuildArrowArray(IArrowType type, IReadOnlyList<List<object?>> rows, int colIdx)
    {
        int count = rows.Count;
        switch (type.TypeId)
        {
            case ArrowTypeId.Int32:
                {
                    var b = new Int32Array.Builder();
                    b.Reserve(count);
                    for (int i = 0; i < count; i++)
                    {
                        var val = rows[i][colIdx];
                        if (val == null) b.AppendNull();
                        else b.Append(Convert.ToInt32(val));
                    }
                    return b.Build();
                }
            case ArrowTypeId.Int64:
                {
                    var b = new Int64Array.Builder();
                    b.Reserve(count);
                    for (int i = 0; i < count; i++)
                    {
                        var val = rows[i][colIdx];
                        if (val == null) b.AppendNull();
                        else b.Append(Convert.ToInt64(val));
                    }
                    return b.Build();
                }
            case ArrowTypeId.Double:
                {
                    var b = new DoubleArray.Builder();
                    b.Reserve(count);
                    for (int i = 0; i < count; i++)
                    {
                        var val = rows[i][colIdx];
                        if (val == null) b.AppendNull();
                        else b.Append(Convert.ToDouble(val));
                    }
                    return b.Build();
                }
            case ArrowTypeId.Boolean:
                {
                    var b = new BooleanArray.Builder();
                    b.Reserve(count);
                    for (int i = 0; i < count; i++)
                    {
                        var val = rows[i][colIdx];
                        if (val == null) b.AppendNull();
                        else b.Append(Convert.ToBoolean(val));
                    }
                    return b.Build();
                }
            default:
                {
                    var b = new StringArray.Builder();
                    b.Reserve(count);
                    for (int i = 0; i < count; i++)
                    {
                        var val = rows[i][colIdx];
                        if (val == null) b.AppendNull();
                        else b.Append(val.ToString());
                    }
                    return b.Build();
                }
        }
    }

    // ── Interaction Materialization & Verification ───────────────────────

    public static (int FilteredCount, string ChecksumSha256) QueryJsonRowOriented(List<List<string?>> rows)
    {
        // Materialize: Select rows where first numeric column > 0 (or row index % 2 == 0)
        var filtered = rows.Where((r, idx) => idx % 2 == 0).ToList();
        var sb = new StringBuilder();
        foreach (var r in filtered)
        {
            sb.Append(string.Join("|", r)).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return (filtered.Count, Convert.ToHexString(hash));
    }

    public static (int FilteredCount, string ChecksumSha256) QueryJsonColumnar(Dictionary<string, List<object?>> cols)
    {
        var firstCol = cols.Values.First();
        int rowCount = firstCol.Count;
        var filteredIndices = Enumerable.Range(0, rowCount).Where(idx => idx % 2 == 0).ToList();

        var sb = new StringBuilder();
        foreach (var idx in filteredIndices)
        {
            var rowVals = cols.Values.Select(col => col[idx]?.ToString() ?? "").ToList();
            sb.Append(string.Join("|", rowVals)).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return (filteredIndices.Count, Convert.ToHexString(hash));
    }

    public static (int FilteredCount, string ChecksumSha256) QueryArrowRecordBatch(RecordBatch batch)
    {
        int rowCount = batch.Length;
        var filteredIndices = Enumerable.Range(0, rowCount).Where(idx => idx % 2 == 0).ToList();

        var sb = new StringBuilder();
        foreach (var idx in filteredIndices)
        {
            var rowVals = new List<string>();
            for (int c = 0; c < batch.ColumnCount; c++)
            {
                var array = batch.Column(c);
                if (array.IsNull(idx))
                {
                    rowVals.Add("");
                }
                else
                {
                    switch (array)
                    {
                        case Int32Array i32: rowVals.Add(i32.GetValue(idx)?.ToString() ?? ""); break;
                        case Int64Array i64: rowVals.Add(i64.GetValue(idx)?.ToString() ?? ""); break;
                        case DoubleArray d: rowVals.Add(d.GetValue(idx)?.ToString() ?? ""); break;
                        case BooleanArray b: rowVals.Add(b.GetValue(idx)?.ToString() ?? ""); break;
                        case StringArray s: rowVals.Add(s.GetString(idx) ?? ""); break;
                        default: rowVals.Add(""); break;
                    }
                }
            }
            sb.Append(string.Join("|", rowVals)).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return (filteredIndices.Count, Convert.ToHexString(hash));
    }

    // ── Compression Helpers ───────────────────────────────────────────────

    public static byte[] CompressGzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    public static byte[] CompressBrotli(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var br = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            br.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    // ── Benchmark Execution ───────────────────────────────────────────────

    public static FormatMeasurement MeasureFormat(TableData data, PayloadFormat format, int samples = 5)
    {
        byte[] payloadBytes = format switch
        {
            PayloadFormat.JsonRowOriented => EncodeJsonRowOriented(data),
            PayloadFormat.JsonColumnar => EncodeJsonColumnar(data),
            PayloadFormat.ArrowIpcStream => EncodeArrowIpcStream(data),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        var gzBytes = CompressGzip(payloadBytes);
        var brBytes = CompressBrotli(payloadBytes);

        // Warmup encoding
        for (int i = 0; i < 2; i++)
        {
            _ = format switch
            {
                PayloadFormat.JsonRowOriented => EncodeJsonRowOriented(data),
                PayloadFormat.JsonColumnar => EncodeJsonColumnar(data),
                PayloadFormat.ArrowIpcStream => EncodeArrowIpcStream(data),
                _ => System.Array.Empty<byte>()
            };
        }

        // Measure encoding
        long memBeforeEnc = GC.GetAllocatedBytesForCurrentThread();
        var swEnc = Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
        {
            _ = format switch
            {
                PayloadFormat.JsonRowOriented => EncodeJsonRowOriented(data),
                PayloadFormat.JsonColumnar => EncodeJsonColumnar(data),
                PayloadFormat.ArrowIpcStream => EncodeArrowIpcStream(data),
                _ => System.Array.Empty<byte>()
            };
        }
        swEnc.Stop();
        long memAfterEnc = GC.GetAllocatedBytesForCurrentThread();
        double encodeMs = swEnc.Elapsed.TotalMilliseconds / samples;
        long encodeAlloc = Math.Max(0, (memAfterEnc - memBeforeEnc) / samples);

        // Warmup decoding
        for (int i = 0; i < 2; i++)
        {
            switch (format)
            {
                case PayloadFormat.JsonRowOriented: _ = DecodeJsonRowOriented(payloadBytes); break;
                case PayloadFormat.JsonColumnar: _ = DecodeJsonColumnar(payloadBytes); break;
                case PayloadFormat.ArrowIpcStream: _ = DecodeArrowIpcStream(payloadBytes); break;
            }
        }

        // Measure decoding
        long memBeforeDec = GC.GetAllocatedBytesForCurrentThread();
        var swDec = Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
        {
            switch (format)
            {
                case PayloadFormat.JsonRowOriented: _ = DecodeJsonRowOriented(payloadBytes); break;
                case PayloadFormat.JsonColumnar: _ = DecodeJsonColumnar(payloadBytes); break;
                case PayloadFormat.ArrowIpcStream: _ = DecodeArrowIpcStream(payloadBytes); break;
            }
        }
        swDec.Stop();
        long memAfterDec = GC.GetAllocatedBytesForCurrentThread();
        double decodeMs = swDec.Elapsed.TotalMilliseconds / samples;
        long decodeAlloc = Math.Max(0, (memAfterDec - memBeforeDec) / samples);

        // Measure post-parse interaction query
        var swQuery = Stopwatch.StartNew();
        int filteredCount = 0;
        string checksum = "";
        switch (format)
        {
            case PayloadFormat.JsonRowOriented:
                var rows = DecodeJsonRowOriented(payloadBytes);
                (filteredCount, checksum) = QueryJsonRowOriented(rows);
                break;
            case PayloadFormat.JsonColumnar:
                var cols = DecodeJsonColumnar(payloadBytes);
                (filteredCount, checksum) = QueryJsonColumnar(cols);
                break;
            case PayloadFormat.ArrowIpcStream:
                var batch = DecodeArrowIpcStream(payloadBytes);
                (filteredCount, checksum) = QueryArrowRecordBatch(batch);
                break;
        }
        swQuery.Stop();

        return new FormatMeasurement(
            Format: format,
            RawBytes: payloadBytes.Length,
            GzipBytes: gzBytes.Length,
            BrotliBytes: brBytes.Length,
            EncodeTimeMs: Math.Round(encodeMs, 3),
            EncodeAllocatedBytes: encodeAlloc,
            DecodeTimeMs: Math.Round(decodeMs, 3),
            DecodeAllocatedBytes: decodeAlloc,
            PostParseFilterTimeMs: Math.Round(swQuery.Elapsed.TotalMilliseconds, 3),
            FilteredRowCount: filteredCount,
            ChecksumSha256: checksum);
    }

    public static async Task<PayloadCrossoverBenchmarkReport> RunFullBenchmarkSuiteAsync(
        int[]? rowCounts = null,
        int samplesPerRun = 5)
    {
        var targetRowCounts = rowCounts ?? new[] { 500, 2500, 10000, 25000, 50000, 100000 };
        var workloads = Enum.GetValues<WorkloadType>();
        var results = new List<WorkloadBenchmarkResult>();

        foreach (var workload in workloads)
        {
            foreach (var count in targetRowCounts)
            {
                var data = GenerateWorkloadData(workload, count);
                var formatDict = new Dictionary<PayloadFormat, FormatMeasurement>();

                foreach (var format in Enum.GetValues<PayloadFormat>())
                {
                    var measurement = MeasureFormat(data, format, samplesPerRun);
                    formatDict[format] = measurement;
                }

                var winnerRaw = formatDict.OrderBy(f => f.Value.RawBytes).First().Key.ToString();
                var winnerGz = formatDict.OrderBy(f => f.Value.GzipBytes).First().Key.ToString();
                var winnerDec = formatDict.OrderBy(f => f.Value.DecodeTimeMs).First().Key.ToString();
                var winnerMem = formatDict.OrderBy(f => f.Value.DecodeAllocatedBytes).First().Key.ToString();

                results.Add(new WorkloadBenchmarkResult(
                    Workload: workload,
                    RowCount: count,
                    Formats: formatDict,
                    WinnerByRawSize: winnerRaw,
                    WinnerByGzipSize: winnerGz,
                    WinnerByDecodeTime: winnerDec,
                    WinnerByMemory: winnerMem));
            }
        }

        var crossoverSummaries = ComputeCrossoverSummaries(results);

        return new PayloadCrossoverBenchmarkReport(
            TimestampUtc: DateTime.UtcNow,
            GitBranch: "test/reporting-phase4-payload-crossover",
            RuntimeEnvironment: GetMachineMetadata(),
            Results: results,
            CrossoverSummaryByWorkload: crossoverSummaries);
    }

    private static Dictionary<string, string> ComputeCrossoverSummaries(List<WorkloadBenchmarkResult> results)
    {
        var summaries = new Dictionary<string, string>();
        var grouped = results.GroupBy(r => r.Workload);

        foreach (var g in grouped)
        {
            var rawCrossover = g.FirstOrDefault(r => r.Formats[PayloadFormat.ArrowIpcStream].RawBytes < r.Formats[PayloadFormat.JsonRowOriented].RawBytes)?.RowCount;
            var decCrossover = g.FirstOrDefault(r => r.Formats[PayloadFormat.ArrowIpcStream].DecodeTimeMs < r.Formats[PayloadFormat.JsonRowOriented].DecodeTimeMs)?.RowCount;
            var memCrossover = g.FirstOrDefault(r => r.Formats[PayloadFormat.ArrowIpcStream].DecodeAllocatedBytes < r.Formats[PayloadFormat.JsonRowOriented].DecodeAllocatedBytes)?.RowCount;

            summaries[g.Key.ToString()] = $"Raw Size Crossover: ~{rawCrossover?.ToString() ?? "N/A"} rows; Decode Latency Crossover: ~{decCrossover?.ToString() ?? "N/A"} rows; Managed Wrapper Allocation Crossover: ~{memCrossover?.ToString() ?? "N/A"} rows";
        }

        return summaries;
    }

    public static string FormatMarkdownReport(PayloadCrossoverBenchmarkReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 4 Visual Data Payload Crossover Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"> **Timestamp (UTC):** {report.TimestampUtc:yyyy-MM-dd HH:mm:ss} | **Branch:** `{report.GitBranch}`");
        sb.AppendLine($"> **OS:** {report.RuntimeEnvironment.OSDescription} ({report.RuntimeEnvironment.ProcessArchitecture}) | **Runtime:** {report.RuntimeEnvironment.FrameworkDescription} | **Cores:** {report.RuntimeEnvironment.ProcessorCount} | **Memory:** {report.RuntimeEnvironment.TotalAvailableMemoryMb} MB");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Executive Summary & Crossover Findings");
        sb.AppendLine();
        sb.AppendLine("This benchmark compares **JSON Row-Oriented** (standard ETL-SQL `VisualManifest.Rows`), **JSON Columnar**, and **Apache Arrow IPC Stream** representations across 5 representative visual workloads spanning **500 to 100,000 rows**.");
        sb.AppendLine();
        sb.AppendLine("### Crossover Ranges by Workload (Empirical Evidence)");
        sb.AppendLine();
        sb.AppendLine("| Workload | Raw Size Crossover | Gzip Compressed Winner | Decode Speed Crossover | Memory Allocation Winner |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: |");

        foreach (var (workload, summary) in report.CrossoverSummaryByWorkload)
        {
            var wResults = report.Results.Where(r => r.Workload.ToString() == workload).ToList();
            var rawCross = wResults.FirstOrDefault(r => r.Formats[PayloadFormat.ArrowIpcStream].RawBytes < r.Formats[PayloadFormat.JsonRowOriented].RawBytes)?.RowCount.ToString("N0") ?? ">100k";
            var decCross = wResults.FirstOrDefault(r => r.Formats[PayloadFormat.ArrowIpcStream].DecodeTimeMs < r.Formats[PayloadFormat.JsonRowOriented].DecodeTimeMs)?.RowCount.ToString("N0") ?? ">100k";
            var gzWinner = wResults.Last().WinnerByGzipSize;
            sb.AppendLine($"| **{workload}** | **{rawCross} rows** | `{gzWinner}` | **{decCross} rows** | See allocation caveat |");
        }

        sb.AppendLine();
        sb.AppendLine("The Arrow decode-allocation column measures managed allocations made while opening an IPC stream and reading a record-batch wrapper over the input byte buffer. Arrow retains column buffers rather than materializing row objects, so the very small values are expected, but they are **not** a measurement of total resident payload memory. Use them only as managed materialization-cost evidence; browser heap and resident-set measurements remain necessary before selecting a production transport.");
        sb.AppendLine();
        sb.AppendLine("These observations do not justify a permanent row-count switch. Compression changes the winner by workload—especially for repetitive strings—and interaction-query timings vary. A production decision must include browser decode/heap measurements and transport negotiation; the checked-in evidence is a reproducible comparison harness, not a shipping threshold.");
        sb.AppendLine();
        sb.AppendLine("The regression suite applies format-neutral, per-row budgets at the 10,000-row representative point: no measured format may exceed 200 raw bytes per row or 40 gzip-compressed bytes per row. Per-row budgets catch payload-shape regressions without turning one sampled row count into a permanent transport switch.");

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. Detailed Workload Measurements");
        sb.AppendLine();

        foreach (var group in report.Results.GroupBy(r => r.Workload))
        {
            sb.AppendLine($"### Workload: `{group.Key}`");
            sb.AppendLine();
            sb.AppendLine("| Rows | Format | Raw Size | Gzip Size | Brotli Size | Encode Time | Decode Time | Decode Memory | Filter Query | Checksum |");
            sb.AppendLine("| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

            foreach (var r in group)
            {
                foreach (var (fmt, m) in r.Formats)
                {
                    sb.AppendLine($"| {r.RowCount:N0} | `{fmt}` | {FormatBytes(m.RawBytes)} | {FormatBytes(m.GzipBytes)} | {FormatBytes(m.BrotliBytes)} | {m.EncodeTimeMs:F2} ms | **{m.DecodeTimeMs:F2} ms** | {FormatBytes(m.DecodeAllocatedBytes)} | {m.PostParseFilterTimeMs:F2} ms | `{m.ChecksumSha256[..8]}` |");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 3. How to Run the Benchmark Harness Deterministically");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("pwsh -File ./scripts/Measure-ReportingPayloadCrossover.ps1");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("To execute the fast non-timing correctness test suite in CI:");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter \"FullyQualifiedName~PayloadCrossoverTests\"");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
