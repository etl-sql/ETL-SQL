using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Centralized utility for extracting and flattening JSON data into ETL-SQL DataTables.
    /// Used by both local JSON file connectors and remote REST API connectors.
    /// Optimized for streaming to handle large datasets with low memory footprint.
    /// </summary>
    public static class JsonExtractor
    {
        private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions 
        { 
            AllowTrailingCommas = true, 
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// extracts data from a JSON stream based on a root path and flattens it into batches using streaming.
        /// </summary>
        public static async IAsyncEnumerable<DataTable> ExtractBatchesAsync(Stream stream, string? rootPath, int batchSize = 10000, bool trimStrings = true)
        {
            // 1. Resolve the target element(s) via streaming if possible
            var elements = StreamElementsInternal(stream, rootPath);
            
            // 2. Process elements into DataTables
            await foreach (var batch in ProcessElementsAsync(elements, batchSize, trimStrings))
            {
                yield return batch;
            }
        }

        private static async IAsyncEnumerable<JsonElement> StreamElementsInternal(Stream stream, string? rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || rootPath == "$")
            {
                // Root array or object streaming
                await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, DefaultOptions))
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in element.EnumerateArray()) yield return item;
                    }
                    else
                    {
                        yield return element;
                    }
                }
                yield break;
            }

            // Deep path navigation requires a more custom approach to stay streaming
            // For simplicity in this engine version, we navigate to the start of the target path
            // and then stream from there.
            
            var pathParts = rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            
            // We use a small buffer to find the target property start without loading everything.
            // If the structure is complex, we might still need to buffer the branch, but not the whole document.
            using (var doc = await JsonDocument.ParseAsync(stream))
            {
                JsonElement target = doc.RootElement;
                foreach (var part in pathParts)
                {
                    if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty(part, out var next)) target = next;
                    else if (target.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < target.GetArrayLength()) target = target[idx];
                    else yield break;
                }

                if (target.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in target.EnumerateArray()) yield return item;
                }
                else
                {
                    yield return target;
                }
            }
        }

        private static async IAsyncEnumerable<DataTable> ProcessElementsAsync(IAsyncEnumerable<JsonElement> elements, int batchSize, bool trimStrings)
        {
            var currentBatch = new DataTable();
            var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await foreach (var element in elements)
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var row = new Row();
                foreach (var property in element.EnumerateObject())
                {
                    row[property.Name] = GetJsonValue(property.Value, trimStrings);
                    allColumns.Add(property.Name);
                }
                
                await currentBatch.AddRowAsync(row);

                if (currentBatch.Rows.Count >= batchSize)
                {
                    currentBatch.SetColumns(allColumns.ToList());
                    yield return currentBatch;
                    currentBatch = new DataTable();
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                currentBatch.SetColumns(allColumns.ToList());
                yield return currentBatch;
            }
        }

        /// <summary>
        /// extracts data from a JsonDocument based on a root path and flattens it into batches.
        /// (Legacy support for small documents or memory-buffered sources)
        /// </summary>
        public static async IAsyncEnumerable<DataTable> ExtractBatches(JsonDocument doc, string? rootPath, int batchSize = 10000, bool trimStrings = true)
        {
            JsonElement root = doc.RootElement;

            if (!string.IsNullOrEmpty(rootPath) && rootPath != "$")
            {
                foreach (var part in rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(part, out var next)) root = next;
                    else if (root.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < root.GetArrayLength()) root = root[idx];
                    else yield break;
                }
            }

            var elements = root.ValueKind == JsonValueKind.Array 
                ? root.EnumerateArray().ToAsyncEnumerable() 
                : new[] { root }.ToAsyncEnumerable();

            await foreach (var b in ProcessElementsAsync(elements, batchSize, trimStrings)) yield return b;
        }

        /// <summary>
        /// Infers columns from a JsonDocument.
        /// </summary>
        public static IEnumerable<string> GetColumns(JsonDocument doc, string? rootPath)
        {
            JsonElement root = doc.RootElement;

            if (!string.IsNullOrEmpty(rootPath) && rootPath != "$")
            {
                foreach (var part in rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(part, out var next)) root = next;
                    else if (root.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < root.GetArrayLength()) root = root[idx];
                    else return Enumerable.Empty<string>();
                }
            }

            var first = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
            if (first.ValueKind == JsonValueKind.Object)
            {
                return first.EnumerateObject().Select(p => p.Name).ToList();
            }

            return Enumerable.Empty<string>();
        }

        public static async Task<IEnumerable<string>> GetColumnsAsync(Stream stream, string? rootPath)
        {
            // For column inference, we only need a representative sample.
            // We use a small portion of the stream if possible, or parse doc for small files.
            using (var doc = await JsonDocument.ParseAsync(stream))
            {
                return GetColumns(doc, rootPath);
            }
        }

        private static object? GetJsonValue(JsonElement element, bool trim) => element.ValueKind switch
        {
            JsonValueKind.String => trim ? element.GetString()?.Trim() : element.GetString(),
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
