using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Centralized utility for extracting and flattening JSON data into ETL-SQL DataTables.
    /// Used by both local JSON file connectors and remote REST API connectors.
    /// </summary>
    public static class JsonExtractor
    {
        /// <summary>
        /// extracts data from a JsonDocument based on a root path and flattens it into batches.
        /// </summary>
        /// <param name="doc">The JSON document to parse.</param>
        /// <param name="rootPath">Dot-notation path to the data array (e.g. 'data.items').</param>
        /// <param name="batchSize">Maximum rows per batch.</param>
        /// <param name="trimStrings">Whether to trim string values.</param>
        /// <returns>An async enumerable of DataTables.</returns>
        public static async IAsyncEnumerable<DataTable> ExtractBatches(JsonDocument doc, string? rootPath, int batchSize = 10000, bool trimStrings = true)
        {
            JsonElement root = doc.RootElement;

            if (!string.IsNullOrEmpty(rootPath) && rootPath != "$")
            {
                foreach (var part in rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(part, out var next))
                    {
                        root = next;
                    }
                    else if (root.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < root.GetArrayLength())
                    {
                        root = root[idx];
                    }
                    else
                    {
                        yield break; // Path not found
                    }
                }
            }

            // Standardize on array even if it's a single object
            var elements = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root }.AsEnumerable();
            
            var currentBatch = new DataTable();
            var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in elements)
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

        private static object? GetJsonValue(JsonElement element, bool trim) => element.ValueKind switch
        {
            JsonValueKind.String => trim ? element.GetString()?.Trim() : element.GetString(),
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
