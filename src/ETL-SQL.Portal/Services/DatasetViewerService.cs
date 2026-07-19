using System.Globalization;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Parquet;

namespace ETL_SQL.Portal.Services;

public sealed class DatasetPreviewCache : IDisposable
{
    private readonly object _gate = new();
    private readonly MemoryCache _cache;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly int _maxRows;
    private int _currentRows;

    public DatasetPreviewCache(PortalConfig config)
    {
        _maxRows = Math.Max(1, config.Dataset.PreviewCacheMaxRows);
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _maxRows
        });
    }

    public bool TryGetValue(
        string key,
        out (List<Dictionary<string, object?>> Rows, List<DatasetColumnDto> Columns) value)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out value))
            {
                if (_entries.TryGetValue(key, out var entry))
                    entry.LastAccessUtc = DateTime.UtcNow;
                return true;
            }

            return false;
        }
    }

    public void Set(
        string key,
        (List<Dictionary<string, object?>> Rows, List<DatasetColumnDto> Columns) value,
        int rowWeight)
    {
        var weight = Math.Max(1, rowWeight);
        lock (_gate)
        {
            EvictKey(key);
            if (weight > _maxRows)
                return;

            while (_currentRows + weight > _maxRows && _entries.Count > 0)
            {
                var oldest = _entries
                    .OrderBy(pair => pair.Value.LastAccessUtc)
                    .First()
                    .Key;
                EvictKey(oldest);
            }

            var entry = new CacheEntry(weight);
            _entries[key] = entry;
            _currentRows += weight;

            var options = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                .SetSize(weight)
                .RegisterPostEvictionCallback((evictedKey, _, _, state) =>
                {
                    if (evictedKey is string removedKey && state is CacheEntry removedEntry)
                        RemoveEntry(removedKey, removedEntry);
                }, entry);
            _cache.Set(key, value, options);
        }
    }

    public void Dispose() => _cache.Dispose();

    private void EvictKey(string key)
    {
        if (_entries.Remove(key, out var entry))
            _currentRows -= entry.Weight;
        _cache.Remove(key);
    }

    private void RemoveEntry(string key, CacheEntry entry)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                _currentRows -= entry.Weight;
            }
        }
    }

    private sealed class CacheEntry(int weight)
    {
        public int Weight { get; } = weight;
        public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
    }
}

public class DatasetViewerService(PortalDbContext db, DatasetPreviewCache cache, PortalConfig config)
{
    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<DatasetRowsDto> QueryAsync(
        int id, int page, int pageSize, string? sort, string? dir,
        string? search, IEnumerable<DatasetColumnFilterDto> filters)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        var filteredList = Apply(rows, columns, search, filters);
        var page1 = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 1000);

        if (!string.IsNullOrWhiteSpace(sort) && columns.Any(c => c.Name.Equals(sort, StringComparison.OrdinalIgnoreCase)))
        {
            bool desc = "desc".Equals(dir, StringComparison.OrdinalIgnoreCase);
            filteredList = desc
                ? filteredList.OrderByDescending(r => r.GetValueOrDefault(sort))
                : filteredList.OrderBy(r => r.GetValueOrDefault(sort));

            var materialized = filteredList.ToList();
            return new DatasetRowsDto(
                columns,
                materialized.Skip((page1 - 1) * size).Take(size).ToList(),
                rows.Count,
                materialized.Count,
                page1,
                size);
        }

        var skip = (page1 - 1) * size;
        var paged = new List<Dictionary<string, object?>>(size);
        var filteredCount = 0L;
        foreach (var row in filteredList)
        {
            if (filteredCount >= skip && paged.Count < size)
                paged.Add(row);
            filteredCount++;
        }

        return new DatasetRowsDto(columns, paged, rows.Count, filteredCount, page1, size);
    }

    public async Task<(IEnumerable<Dictionary<string, object?>> rows, List<DatasetColumnDto> columns)> PrepareExportAsync(
        int id, string? sort, string? dir, string? search, IEnumerable<DatasetColumnFilterDto> filters)
    {
        var dataset = await LoadDatasetAsync(id);
        var columns = ParseColumnSchema(dataset.ColumnSchema);
        var rows = await LoadRowsAsync(dataset, columns, maxRows: null);
        var filtered = Apply(rows, columns, search, filters);

        if (!string.IsNullOrWhiteSpace(sort) && columns.Any(c => c.Name.Equals(sort, StringComparison.OrdinalIgnoreCase)))
        {
            bool desc = "desc".Equals(dir, StringComparison.OrdinalIgnoreCase);
            filtered = desc
                ? filtered.OrderByDescending(r => r.GetValueOrDefault(sort))
                : filtered.OrderBy(r => r.GetValueOrDefault(sort));
        }

        return (filtered, columns);
    }

    public async Task ExportCsvAsync(
        List<DatasetColumnDto> columns, IEnumerable<Dictionary<string, object?>> filtered, Stream output)
    {
        await using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);

        // Header
        await writer.WriteLineAsync(string.Join(",", columns.Select(c => CsvQuote(NeutralizeCsvFormula(c.Name)))));

        // Data
        foreach (var row in filtered)
            await writer.WriteLineAsync(string.Join(",", columns.Select(c => CsvQuote(CsvCell(row.GetValueOrDefault(c.Name))))));
    }

    public async Task ExportXlsxAsync(
        List<DatasetColumnDto> columns, IEnumerable<Dictionary<string, object?>> filtered, Stream output, string sheetName = "Data")
    {
        // Columns carry their SQL type, so XlsxWriter emits typed number/date cells.
        var cols = columns.Select(c => new XlsxWriter.Column(c.Name, c.Type)).ToList();
        await XlsxWriter.WriteAsync(output, cols, filtered, sheetName);
    }

    public async Task<IEnumerable<DatasetColumnStatsDto>> GetStatsAsync(
        int id, IEnumerable<DatasetColumnFilterDto> filters)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        var accumulators = columns
            .Select(column => new ColumnStatsAccumulator(column.Name))
            .ToArray();

        foreach (var row in Apply(rows, columns, null, filters))
        {
            foreach (var accumulator in accumulators)
                accumulator.Add(row.GetValueOrDefault(accumulator.Name));
        }

        return accumulators.Select(accumulator => accumulator.ToDto()).ToList();
    }

    public async Task<DatasetColumnValuesDto> GetColumnValuesAsync(
        int id, string colName, string? search, int limit)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        if (!columns.Any(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase)))
            return new DatasetColumnValuesDto([], 0);

        var size = Math.Max(1, limit);
        var seen = new HashSet<object?>();
        var values = new List<object?>(size);
        foreach (var row in rows)
        {
            var value = row.GetValueOrDefault(colName);
            if (!seen.Add(value))
                continue;

            if (values.Count < size
                && (string.IsNullOrWhiteSpace(search)
                    || value?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            {
                values.Add(value);
            }
        }
        return new DatasetColumnValuesDto(values, seen.Count);
    }

    // ── Cache + Parquet reader ────────────────────────────────────────────────

    private async Task<(List<Dictionary<string, object?>> rows, List<DatasetColumnDto> columns)> LoadCachedAsync(int id)
    {
        var dataset = await LoadDatasetAsync(id);
        var columns = ParseColumnSchema(dataset.ColumnSchema);
        string cacheKey = DatasetCacheKey(dataset);

        if (cache.TryGetValue(cacheKey, out (List<Dictionary<string, object?>> Rows, List<DatasetColumnDto> Columns) cached))
            return cached;

        var previewRows = await LoadRowsAsync(dataset, columns, config.MaxPreviewRows);
        var result = (previewRows, columns);
        cache.Set(cacheKey, result, previewRows.Count);
        return result;
    }

    private async Task<Dataset> LoadDatasetAsync(int id)
    {
        var dataset = await db.Datasets.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new InvalidOperationException($"Dataset {id} not found.");

        if (string.IsNullOrWhiteSpace(dataset.ParquetFilePath) || !File.Exists(dataset.ParquetFilePath))
            throw new InvalidOperationException($"Parquet file for dataset '{dataset.Name}' is not available.");

        return dataset;
    }

    private async Task<List<Dictionary<string, object?>>> LoadRowsAsync(
        Dataset dataset,
        List<DatasetColumnDto> columns,
        int? maxRows)
    {
        var atRestDecryptOptions = ResolveAtRestDecryptOptions(dataset);

        string effectivePath = dataset.ParquetFilePath;
        string? tempFile = null;

        if (atRestDecryptOptions != null)
        {
            try
            {
                tempFile = Path.GetTempFileName();
                var enc = new EncryptionOptions(atRestDecryptOptions);
                enc.DecryptFile(dataset.ParquetFilePath, tempFile);
                effectivePath = tempFile;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                if (tempFile != null) try { File.Delete(tempFile); } catch { /* best effort */ }
                throw new InvalidOperationException($"Failed to decrypt dataset '{dataset.Name}': {ex.Message}", ex);
            }
        }

        List<Dictionary<string, object?>> rows;
        try
        {
            rows = await ReadParquetAsync(effectivePath, columns, maxRows);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to read dataset '{dataset.Name}': {ex.Message}", ex);
        }
        finally
        {
            if (tempFile != null) try { File.Delete(tempFile); } catch { /* best effort */ }
        }

        return rows;
    }

    private static string DatasetCacheKey(Dataset dataset)
    {
        var identity = string.Join(
            "|",
            dataset.Id,
            dataset.Version,
            dataset.RowCount,
            dataset.UpdatedAt.Ticks,
            dataset.LastRefresh?.Ticks ?? 0,
            dataset.AtRestKeyVersion ?? string.Empty,
            dataset.ParquetFilePath);
        return $"dsv:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
    }

    // Returns the EncryptionOptions dictionary needed to decrypt the at-rest cache, or null when the
    // file is plaintext. A configured portal key encrypts every cache (regardless of the stored mode);
    // otherwise the stored mode is honored. A legacy Password/KeyFile record with no portal key carries a
    // credential we don't have at read time — surfaced as a clear, viewable error.
    private Dictionary<string, string>? ResolveAtRestDecryptOptions(Dataset dataset)
    {
        var atRestKey = ResolveConfiguredKey(dataset.AtRestKeyVersion);
        if (!string.IsNullOrWhiteSpace(atRestKey))
            return new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = atRestKey };

        return dataset.EncryptionMode switch
        {
            DatasetEncryptionMode.None => null,
            DatasetEncryptionMode.MachineBound => new Dictionary<string, string> { ["ENCRYPT"] = "MACHINE" },
            _ => throw new InvalidOperationException(
                $"Dataset '{dataset.Name}' was encrypted at rest with a {dataset.EncryptionMode} credential and no portal at-rest key is configured, so it cannot be viewed. Configure Portal:Dataset:AtRestKey or re-materialise the dataset.")
        };
    }

    private string? ResolveConfiguredKey(string? version)
    {
        if (string.IsNullOrWhiteSpace(config.Dataset.AtRestKey))
            return null;

        if (string.IsNullOrWhiteSpace(version)
            || version.Equals(config.Dataset.AtRestKeyVersion, StringComparison.OrdinalIgnoreCase))
        {
            return config.Dataset.AtRestKey;
        }

        return config.Dataset.PreviousAtRestKeys.TryGetValue(version, out var previousKey)
            ? previousKey
            : throw new InvalidOperationException(
                $"Dataset key version '{version}' is not configured. Restore that version's key or complete key rotation.");
    }

    private static async Task<List<Dictionary<string, object?>>> ReadParquetAsync(
        string filePath, List<DatasetColumnDto> columns, int? maxRows)
    {
        var rows = new List<Dictionary<string, object?>>();

        await using var stream = File.OpenRead(filePath);
        await using var reader = await ParquetReader.CreateAsync(stream);

        var dataFields = reader.Schema.GetDataFields();

        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using var rgReader = reader.OpenRowGroupReader(g);
            int rowCount = (int)rgReader.RowCount;
            var columnArrays = new object?[dataFields.Length][];

            for (int c = 0; c < dataFields.Length; c++)
            {
                var raw = await rgReader.ReadRawColumnDataBaseAsync(dataFields[c], default);
                var nullableValsProp = raw.GetType().GetProperty("NullableValues");
                columnArrays[c] = new object?[rowCount];
                if (nullableValsProp != null)
                {
                    var seq = (System.Collections.IEnumerable)nullableValsProp.GetValue(raw)!;
                    int idx = 0;
                    foreach (var v in seq) { if (idx >= rowCount) break; columnArrays[c][idx++] = v; }
                }
            }

            for (int r = 0; r < rowCount; r++)
            {
                if (maxRows is { } limit && rows.Count >= limit)
                    break;

                var row = new Dictionary<string, object?>(dataFields.Length, StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < dataFields.Length; c++)
                {
                    row[dataFields[c].Name] = columnArrays[c][r];
                }
                rows.Add(row);
            }

            if (maxRows is { } rowLimit && rows.Count >= rowLimit)
                break;
        }

        return rows;
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> Apply(
        List<Dictionary<string, object?>> rows,
        List<DatasetColumnDto> columns,
        string? search,
        IEnumerable<DatasetColumnFilterDto> filters)
    {
        IEnumerable<Dictionary<string, object?>> result = rows;

        if (!string.IsNullOrWhiteSpace(search))
            result = result.Where(r => r.Values.Any(v =>
                v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));

        foreach (var f in filters)
        {
            var col = f.Col;
            result = f.Op switch
            {
                "contains" => result.Where(r => r.GetValueOrDefault(col)?.ToString()?.Contains(f.Val ?? "", StringComparison.OrdinalIgnoreCase) == true),
                "starts_with" => result.Where(r => r.GetValueOrDefault(col)?.ToString()?.StartsWith(f.Val ?? "", StringComparison.OrdinalIgnoreCase) == true),
                "eq" => result.Where(r => string.Equals(r.GetValueOrDefault(col)?.ToString(), f.Val, StringComparison.OrdinalIgnoreCase)),
                "neq" => result.Where(r => !string.Equals(r.GetValueOrDefault(col)?.ToString(), f.Val, StringComparison.OrdinalIgnoreCase)),
                "gt" => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) > 0),
                "lt" => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) < 0),
                "gte" => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) >= 0),
                "lte" => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) <= 0),
                "between" => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) >= 0 && CompareNum(r.GetValueOrDefault(col), f.Val2) <= 0),
                "in" => result.Where(r => ParseJsonArray(f.Val).Contains(r.GetValueOrDefault(col)?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)),
                "is_null" => result.Where(r => r.GetValueOrDefault(col) is null),
                "not_null" => result.Where(r => r.GetValueOrDefault(col) is not null),
                _ => result
            };
        }

        return result;
    }

    private static int CompareNum(object? val, string? threshold)
    {
        if (val is null || threshold is null) return 0;
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
         && double.TryParse(threshold, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
            return a.CompareTo(b);
        return string.Compare(val.ToString(), threshold, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(json);
            return arr is null ? [] : [.. arr];
        }
        catch { return []; }
    }

    private static double? TryParseDouble(object? v)
    {
        if (v is null) return null;
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is decimal dm) return (double)dm;
        if (v is int i) return i;
        if (v is long l) return l;
        return double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    private sealed class ColumnStatsAccumulator(string name)
    {
        public string Name { get; } = name;
        private long _nullCount;
        private long _numericCount;
        private double _numericSum;
        private double? _numericMin;
        private double? _numericMax;
        private string? _stringMin;
        private string? _stringMax;

        public void Add(object? value)
        {
            if (value is null)
            {
                _nullCount++;
                return;
            }

            var numeric = TryParseDouble(value);
            if (numeric.HasValue)
            {
                var number = numeric.Value;
                _numericCount++;
                _numericSum += number;
                _numericMin = _numericMin.HasValue ? Math.Min(_numericMin.Value, number) : number;
                _numericMax = _numericMax.HasValue ? Math.Max(_numericMax.Value, number) : number;
                return;
            }

            if (value is string { Length: > 0 } text)
            {
                if (_stringMin is null || string.Compare(text, _stringMin, StringComparison.Ordinal) < 0)
                    _stringMin = text;
                if (_stringMax is null || string.Compare(text, _stringMax, StringComparison.Ordinal) > 0)
                    _stringMax = text;
            }
        }

        public DatasetColumnStatsDto ToDto()
        {
            if (_numericCount > 0)
                return new DatasetColumnStatsDto(
                    Name,
                    _nullCount,
                    _numericMin,
                    _numericMax,
                    _numericSum / _numericCount);

            if (_stringMin is not null)
                return new DatasetColumnStatsDto(Name, _nullCount, _stringMin, _stringMax, null);

            return new DatasetColumnStatsDto(Name, _nullCount, null, null, null);
        }
    }

    private static string CsvQuote(string? s)
    {
        if (s is null) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    // Render a cell for CSV, neutralizing formula injection on text values only (numbers/dates
    // keep their natural representation — a negative number must not become text).
    private static string CsvCell(object? v) =>
        v switch
        {
            null => "",
            string s => NeutralizeCsvFormula(s),
            _ => v.ToString() ?? ""
        };

    // Excel/Sheets interpret a cell beginning with =,+,-,@ (or a leading tab/CR) as a formula.
    // Prefix a single quote so spreadsheet apps render it as literal text — blocks CSV/formula
    // injection (e.g. =HYPERLINK/WEBSERVICE exfiltration) from stored dataset content.
    private static string NeutralizeCsvFormula(string s) =>
        s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + s
            : s;

    private static List<DatasetColumnDto> ParseColumnSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Select(e => new DatasetColumnDto(
                    e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    e.TryGetProperty("type", out var t) ? t.GetString() ?? "unknown" : "unknown"))
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();
        }
        catch { return []; }
    }
}
