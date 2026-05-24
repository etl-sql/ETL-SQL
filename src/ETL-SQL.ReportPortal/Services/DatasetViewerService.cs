using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Parquet;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public class DatasetViewerService(PortalDbContext db, IMemoryCache cache)
{
    private static readonly MemoryCacheEntryOptions CacheOptions =
        new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(5));

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<DatasetRowsDto> QueryAsync(
        int id, int page, int pageSize, string? sort, string? dir,
        string? search, IEnumerable<DatasetColumnFilterDto> filters)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        var filtered = Apply(rows, columns, search, filters);

        if (!string.IsNullOrWhiteSpace(sort) && columns.Any(c => c.Name.Equals(sort, StringComparison.OrdinalIgnoreCase)))
        {
            bool desc = "desc".Equals(dir, StringComparison.OrdinalIgnoreCase);
            filtered = desc
                ? filtered.OrderByDescending(r => r.GetValueOrDefault(sort)).ToList()
                : filtered.OrderBy(r => r.GetValueOrDefault(sort)).ToList();
        }

        long totalCount    = rows.Count;
        long filteredCount = filtered.Count;

        var page1 = Math.Max(1, page);
        var size  = Math.Clamp(pageSize, 1, 1000);
        var paged = filtered.Skip((page1 - 1) * size).Take(size).ToList();

        return new DatasetRowsDto(columns, paged, totalCount, filteredCount, page1, size);
    }

    public async Task ExportCsvAsync(
        int id, string? sort, string? dir, string? search,
        IEnumerable<DatasetColumnFilterDto> filters, Stream output)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        var filtered = Apply(rows, columns, search, filters);

        if (!string.IsNullOrWhiteSpace(sort) && columns.Any(c => c.Name.Equals(sort, StringComparison.OrdinalIgnoreCase)))
        {
            bool desc = "desc".Equals(dir, StringComparison.OrdinalIgnoreCase);
            filtered = desc
                ? filtered.OrderByDescending(r => r.GetValueOrDefault(sort)).ToList()
                : filtered.OrderBy(r => r.GetValueOrDefault(sort)).ToList();
        }

        await using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);

        // Header
        await writer.WriteLineAsync(string.Join(",", columns.Select(c => CsvQuote(c.Name))));

        // Data
        foreach (var row in filtered)
            await writer.WriteLineAsync(string.Join(",", columns.Select(c => CsvQuote(row.GetValueOrDefault(c.Name)?.ToString()))));
    }

    public async Task<IEnumerable<DatasetColumnStatsDto>> GetStatsAsync(
        int id, IEnumerable<DatasetColumnFilterDto> filters)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        var filtered = Apply(rows, columns, null, filters);

        return columns.Select(col =>
        {
            var values = filtered.Select(r => r.GetValueOrDefault(col.Name)).ToList();
            long nulls = values.Count(v => v is null);
            var nums   = values.OfType<object>()
                               .Select(v => TryParseDouble(v))
                               .Where(v => v.HasValue)
                               .Select(v => v!.Value)
                               .ToList();
            if (nums.Count > 0)
                return new DatasetColumnStatsDto(col.Name, nulls, nums.Min(), nums.Max(), nums.Average());

            var strings = values.OfType<string>().Where(s => s.Length > 0).OrderBy(s => s).ToList();
            if (strings.Count > 0)
                return new DatasetColumnStatsDto(col.Name, nulls, strings.First(), strings.Last(), null);

            return new DatasetColumnStatsDto(col.Name, nulls, null, null, null);
        }).ToList();
    }

    public async Task<DatasetColumnValuesDto> GetColumnValuesAsync(
        int id, string colName, string? search, int limit)
    {
        var (rows, columns) = await LoadCachedAsync(id);
        if (!columns.Any(c => c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase)))
            return new DatasetColumnValuesDto([], 0);

        var all = rows.Select(r => r.GetValueOrDefault(colName)).Distinct().ToList();
        long total = all.Count;

        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(v => v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true).ToList();

        var paged = all.Take(Math.Max(1, limit)).ToList();
        return new DatasetColumnValuesDto(paged, total);
    }

    // ── Cache + Parquet reader ────────────────────────────────────────────────

    private async Task<(List<Dictionary<string, object?>> rows, List<DatasetColumnDto> columns)> LoadCachedAsync(int id)
    {
        string cacheKey = $"dsv:{id}";

        if (cache.TryGetValue(cacheKey, out (List<Dictionary<string, object?>> rows, List<DatasetColumnDto> columns) cached))
            return cached;

        var dataset = await db.Datasets.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new InvalidOperationException($"Dataset {id} not found.");

        if (string.IsNullOrWhiteSpace(dataset.ParquetFilePath) || !File.Exists(dataset.ParquetFilePath))
            throw new InvalidOperationException($"Parquet file for dataset '{dataset.Name}' is not available.");

        var columns = ParseColumnSchema(dataset.ColumnSchema);

        if (dataset.EncryptionMode is not DatasetEncryptionMode.None and not DatasetEncryptionMode.MachineBound)
            throw new InvalidOperationException(
                $"Dataset '{dataset.Name}' uses {dataset.EncryptionMode} encryption, which is not supported for web viewing.");

        string effectivePath = dataset.ParquetFilePath;
        string? tempFile = null;

        if (dataset.EncryptionMode == DatasetEncryptionMode.MachineBound)
        {
            try
            {
                tempFile = Path.GetTempFileName();
                var enc = new EncryptionOptions(new Dictionary<string, string> { { "ENCRYPT", "MACHINE" } });
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
            rows = await ReadParquetAsync(effectivePath, columns);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to read dataset '{dataset.Name}': {ex.Message}", ex);
        }
        finally
        {
            if (tempFile != null) try { File.Delete(tempFile); } catch { /* best effort */ }
        }

        var result = (rows, columns);
        cache.Set(cacheKey, result, CacheOptions);
        return result;
    }

    private static async Task<List<Dictionary<string, object?>>> ReadParquetAsync(
        string filePath, List<DatasetColumnDto> columns)
    {
        var rows = new List<Dictionary<string, object?>>();

        await using var stream = File.OpenRead(filePath);
        using var reader = await ParquetReader.CreateAsync(stream);

        var dataFields = reader.Schema.GetDataFields();

        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using var rgReader  = reader.OpenRowGroupReader(g);
            int rowCount        = (int)rgReader.RowCount;
            var columnArrays    = new Array[dataFields.Length];

            for (int c = 0; c < dataFields.Length; c++)
                columnArrays[c] = (await rgReader.ReadColumnAsync(dataFields[c])).Data;

            for (int r = 0; r < rowCount; r++)
            {
                var row = new Dictionary<string, object?>(dataFields.Length, StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < dataFields.Length; c++)
                {
                    var val = columnArrays[c].GetValue(r);
                    row[dataFields[c].Name] = val;
                }
                rows.Add(row);
            }
        }

        return rows;
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> Apply(
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
                "contains"   => result.Where(r => r.GetValueOrDefault(col)?.ToString()?.Contains(f.Val ?? "", StringComparison.OrdinalIgnoreCase) == true),
                "starts_with"=> result.Where(r => r.GetValueOrDefault(col)?.ToString()?.StartsWith(f.Val ?? "", StringComparison.OrdinalIgnoreCase) == true),
                "eq"         => result.Where(r => string.Equals(r.GetValueOrDefault(col)?.ToString(), f.Val, StringComparison.OrdinalIgnoreCase)),
                "neq"        => result.Where(r => !string.Equals(r.GetValueOrDefault(col)?.ToString(), f.Val, StringComparison.OrdinalIgnoreCase)),
                "gt"         => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) > 0),
                "lt"         => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) < 0),
                "gte"        => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) >= 0),
                "lte"        => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) <= 0),
                "between"    => result.Where(r => CompareNum(r.GetValueOrDefault(col), f.Val) >= 0 && CompareNum(r.GetValueOrDefault(col), f.Val2) <= 0),
                "in"         => result.Where(r => ParseJsonArray(f.Val).Contains(r.GetValueOrDefault(col)?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)),
                "is_null"    => result.Where(r => r.GetValueOrDefault(col) is null),
                "not_null"   => result.Where(r => r.GetValueOrDefault(col) is not null),
                _            => result
            };
        }

        return result.ToList();
    }

    private static int CompareNum(object? val, string? threshold)
    {
        if (val is null || threshold is null) return 0;
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
         && double.TryParse(threshold,      NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
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
        if (v is double d)   return d;
        if (v is float  f)   return f;
        if (v is decimal dm) return (double)dm;
        if (v is int    i)   return i;
        if (v is long   l)   return l;
        return double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    private static string CsvQuote(string? s)
    {
        if (s is null) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

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
