using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using System.IO.Compression;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Json
{
    /// <summary>
    /// Data source implementation for reading and writing JSON files.
    /// Supports JSONPath root selection, file compression (ZIP), and encryption.
    /// </summary>
    public class JsonDataSource : IDatabaseSource
    {
        private readonly string _filePath;
        private readonly string? _rootPath;
        private readonly bool _compress;
        private readonly Encoding? _encoding;
        private readonly bool _trim = true;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext _context;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "JSON";
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public JsonDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _filePath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));
            
            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);

            _options = options;
            if (options != null)
            {
                if (options.TryGetValue("ROOT_PATH", out var rp)) _rootPath = rp;
                if (options.TryGetValue("COMPRESS", out var comp)) _compress = comp.ToUpperInvariant() == "ON";
                if (options.TryGetValue("ENCODING", out var enc))
                {
                    _encoding = enc.ToUpperInvariant() switch
                    {
                        "ANSI" or "LATIN1" => Encoding.GetEncoding("ISO-8859-1"),
                        "UTF8" => Encoding.UTF8,
                        "UTF16" or "UNICODE" => Encoding.Unicode,
                        _ => Encoding.GetEncoding(enc)
                    };
                }
                if (options.TryGetValue("TRIM", out var tr)) _trim = tr.ToUpperInvariant() == "ON" || tr.ToUpperInvariant() == "TRUE";
            }
            
            _encryption = new EncryptionOptions(options);
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (!System.IO.File.Exists(_filePath)) yield break;

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                _encryption.DecryptFile(_filePath, tempFile);
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
                await foreach (var batch in JsonExtractor.ExtractBatchesAsync(stream, _rootPath, batchSize, _trim))
                {
                    yield return batch;
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

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

                if (_encryption.Enabled)
                {
                    _encryption.EncryptFile(tempFile, _filePath);
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
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                try { _encryption.DecryptFile(_filePath, tempFile); effectivePath = tempFile; }
                catch (Exception ex) { _logger.Debug("[JsonDataSource.GetColumnsAsync] Failed to decrypt '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
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
                catch (Exception ex) { _logger.Debug("[JsonDataSource.GetColumnsAsync] Failed to decompress '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                return await JsonExtractor.GetColumnsAsync(stream, _rootPath);
            }
            catch (Exception ex) { _logger.Debug("[JsonDataSource.GetColumnsAsync] Failed to read columns from '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public async Task TruncateAsync()
        {
            await WriteBatches(AsyncEnumerable.Empty<DataTable>());
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        public async Task<string> GetVersionAsync() => await Task.FromResult("1.0.0");
        public HashSet<string> GetSupportedFunctions() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            if (sql.Trim().ToUpperInvariant().StartsWith("SELECT * FROM FILE"))
            {
                await foreach (var batch in ReadBatches()) yield return batch;
            }
            else
            {
                throw new ExecutionException("Json connector only supports 'SELECT * FROM FILE' as native SQL.");
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "JSON";
        public bool SupportsSqlPushdown => false;
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();
    }
}
