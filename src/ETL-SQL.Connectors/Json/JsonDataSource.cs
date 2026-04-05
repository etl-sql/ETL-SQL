using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using System.IO.Compression;

namespace ETL_SQL.Connectors.Json
{
    /// <summary>
    /// Data source implementation for reading and writing JSON files.
    /// Supports JSONPath root selection, file compression (ZIP), and encryption.
    /// </summary>
    public class JsonDataSource : IDataSource
    {
        private readonly string _filePath;
        private readonly string? _rootPath;
        private readonly bool _compress;
        private readonly bool _encrypt;
        private readonly string _password;
        private readonly Dictionary<string, string>? _options;

        /// <summary>Gets the physical path to the JSON file.</summary>
        public string Path => _filePath;
        /// <summary>The options used to create this data source.</summary>
        public Dictionary<string, string>? Options => _options;
        
        /// <summary>Returns this instance as a typed table (no-op for JSON).</summary>
        public IDataSource WithTable(string tableName) => this;

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonDataSource"/> class.
        /// </summary>
        /// <param name="filePath">The path to the JSON file.</param>
        /// <param name="options">Optional configuration params (ROOT_PATH, COMPRESS, etc.).</param>
        public JsonDataSource(string filePath, Dictionary<string, string>? options = null)
        {
            _filePath = filePath;
            _options = options;
            if (options != null)
            {
                if (options.TryGetValue("ROOT_PATH", out var rp)) _rootPath = rp;
                if (options.TryGetValue("COMPRESS", out var comp)) _compress = comp.ToUpperInvariant() == "ON";
                if (options.TryGetValue("ENCRYPT", out var encr)) _encrypt = encr.ToUpperInvariant() == "ON";
                if (options.TryGetValue("PASSWORD", out var p)) _password = p;
                else _password = "DefaultETLPass123!";
            }
            else
            {
                _password = "DefaultETLPass123!";
            }
        }

        /// <summary>Reads data from the JSON file in batches.</summary>
        /// <param name="batchSize">The number of rows per batch.</param>
        /// <returns>An async enumerable of DataTables.</returns>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (!System.IO.File.Exists(_filePath)) yield break;

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encrypt)
            {
                tempFile = System.IO.Path.GetTempFileName();
                CryptoUtils.DecryptFile(_filePath, tempFile, _password);
                effectivePath = tempFile;
            }
            else if (_compress && _filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
                using (var zip = System.IO.Compression.ZipFile.OpenRead(_filePath))
                {
                    var entry = zip.Entries.FirstOrDefault();
                    if (entry != null)
                    {
                        entry.ExtractToFile(tempFile, true);
                        effectivePath = tempFile;
                    }
                }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                JsonDocument doc;
                try { doc = await JsonDocument.ParseAsync(stream); }
                catch (Exception ex) { Logger.Verbose($"[JsonDataSource.ReadBatches] Failed to parse JSON '{effectivePath}': {ex.Message}"); yield break; }

                using (doc)
                {
                    JsonElement root = doc.RootElement;
                    if (_rootPath != null)
                    {
                        foreach (var part in _rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(part, out var next)) root = next;
                            else if (root.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < root.GetArrayLength()) root = root[idx];
                            else yield break;
                        }
                    }

                    if (root.ValueKind != JsonValueKind.Array && root.ValueKind != JsonValueKind.Object) yield break;

                    var elements = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root }.AsEnumerable();
                    var currentBatch = new DataTable();
                    var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var element in elements)
                    {
                        if (element.ValueKind != JsonValueKind.Object) continue;

                        // Start with a dynamic row since we don't know the schema yet
                        var row = new Row();
                        foreach (var property in element.EnumerateObject())
                        {
                            row[property.Name] = GetJsonValue(property.Value);
                            allColumns.Add(property.Name);
                        }
                        currentBatch.AddRow(row);

                        if (currentBatch.Rows.Count >= batchSize)
                        {
                            currentBatch.SetColumns(allColumns);
                            yield return currentBatch;
                            currentBatch = new DataTable();
                        }
                    }

                    if (currentBatch.Rows.Count > 0)
                    {
                        currentBatch.SetColumns(allColumns);
                        yield return currentBatch;
                    }
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile);
            }
        }

        private object? GetJsonValue(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };

        /// <summary>Writes batches of data to the JSON file.</summary>
        /// <param name="batches">An async enumerable of DataTables.</param>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            var allRows = new List<IDictionary<string, object?>>();
            bool alreadyJson = false;
            string? singleJson = null;

            await foreach (var b in batches)
            {
                if (b.ColumnNames.Count == 1 && b.ColumnNames[0] == "JSON_F52E2B61")
                {
                    alreadyJson = true;
                    singleJson = b.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault()?.ToString();
                    break;
                }

                foreach (var r in b.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var col in b.ColumnNames) dict[col] = r[col];
                    allRows.Add(dict);
                }
            }

            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                if (alreadyJson && singleJson != null)
                {
                    await System.IO.File.WriteAllTextAsync(tempFile, singleJson);
                }
                else
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    await System.IO.File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(allRows, options));
                }

                if (_encrypt)
                {
                    CryptoUtils.EncryptFile(tempFile, _filePath, _password);
                }
                else if (_compress)
                {
                    string zipPath = _filePath;
                    if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zipPath += ".zip";
                    if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
                    using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        zip.CreateEntryFromFile(tempFile, System.IO.Path.GetFileName(_filePath));
                    }
                }
                else
                {
                    if (System.IO.File.Exists(_filePath)) System.IO.File.Delete(_filePath);
                    System.IO.File.Move(tempFile, _filePath);
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile);
            }
        }

        /// <summary>Asynchronously retrieves the column names from the JSON object structure.</summary>
        /// <returns>A collection of property names from the first object in the array.</returns>
        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encrypt)
            {
                tempFile = System.IO.Path.GetTempFileName();
                try { CryptoUtils.DecryptFile(_filePath, tempFile, _password); effectivePath = tempFile; }
                catch (Exception ex) { Logger.Verbose($"[JsonDataSource.GetColumnsAsync] Failed to decrypt '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            }
            else if (_compress && _filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
                try
                {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(_filePath))
                    {
                        var entry = zip.Entries.FirstOrDefault();
                        if (entry != null) { entry.ExtractToFile(tempFile, true); effectivePath = tempFile; }
                        else return Enumerable.Empty<string>();
                    }
                }
                catch (Exception ex) { Logger.Verbose($"[JsonDataSource.GetColumnsAsync] Failed to decompress '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var doc = JsonDocument.Parse(stream);
                JsonElement root = doc.RootElement;
                if (_rootPath != null)
                {
                    foreach (var part in _rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(part, out var next)) root = next;
                        else if (root.ValueKind == JsonValueKind.Array && int.TryParse(part, out var idx) && idx >= 0 && idx < root.GetArrayLength()) root = root[idx];
                        else return Enumerable.Empty<string>();
                    }
                }
                var first = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
                if (first.ValueKind == JsonValueKind.Object) return first.EnumerateObject().Select(p => p.Name).ToList();
                return Enumerable.Empty<string>();
            }
            catch (Exception ex) { Logger.Verbose($"[JsonDataSource.GetColumnsAsync] Failed to read columns from '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            finally
            {
                TempFileHelper.SafeDelete(tempFile);
            }
        }

        /// <summary>Captures a snapshot (no-op for JSON).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for JSON).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Truncates the JSON file by clearing all data.</summary>
        public async Task TruncateAsync()
        {
            if (_rootPath != null)
            {
                // If a root path is specified, we should probably only clear that specific part,
                // but for now, we'll follow the same logic as others: clearing the whole source or throwing.
                // However, WriteBatches with empty list will clear the file.
                await WriteBatches(AsyncEnumerable.Empty<DataTable>());
            }
            else
            {
                await WriteBatches(AsyncEnumerable.Empty<DataTable>());
            }
        }
        /// <summary>Asynchronously disposes resources.</summary>
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}

