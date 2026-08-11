using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

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
        private readonly bool _transactional = false;

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
                if (options.TryGetValue("TRANSACTIONAL", out var tx)) _transactional = tx.ToUpperInvariant() == "ON" || tx.ToUpperInvariant() == "TRUE";
            }

            _encryption = new EncryptionOptions(options);

            var resolvedPath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));
            _filePath = FileConnectorPathHelper.CoerceFilePathExtension(resolvedPath, _encryption.Enabled, _compress);

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeRead(_context, _filePath);
            if (!System.IO.File.Exists(_filePath)) yield break;

            using var stream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".json");
            await foreach (var batch in JsonExtractor.ExtractBatchesAsync(stream, _rootPath, batchSize, _trim)
                .WithCancellation(effectiveCancellationToken))
            {
                yield return batch;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);
            var allRows = new List<IDictionary<string, object?>>();
            bool alreadyJson = false;
            string? singleJson = null;

            await foreach (var b in batches.WithCancellation(effectiveCancellationToken))
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

            string tempFile = ETL_SQL.Core.Common.FileConnectorPathHelper.GetStagingFilePath(_filePath, _transactional);
            try
            {
                if (alreadyJson && singleJson != null)
                {
                    await System.IO.File.WriteAllTextAsync(tempFile, singleJson, effectiveCancellationToken);
                }
                else
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    await System.IO.File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(allRows, options), effectiveCancellationToken);
                }

                string fileToEncrypt = tempFile;
                string? zippedTemp = null;

                if (_compress)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    zippedTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".zip");
                    using (var zip = System.IO.Compression.ZipFile.Open(zippedTemp, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        string entryName = System.IO.Path.GetFileName(_filePath);
                        if (entryName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            entryName = entryName.Substring(0, entryName.Length - 4);
                        else if (entryName.EndsWith(".pgp", StringComparison.OrdinalIgnoreCase))
                            entryName = entryName.Substring(0, entryName.Length - 4);

                        zip.CreateEntryFromFile(tempFile, entryName);
                    }
                    fileToEncrypt = zippedTemp;
                }

                if (_encryption.Enabled)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    _encryption.EncryptFile(fileToEncrypt, _filePath);
                }
                else if (_compress)
                {
                    var dir = System.IO.Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                    if (System.IO.File.Exists(_filePath)) System.IO.File.Delete(_filePath);
                    System.IO.File.Move(fileToEncrypt, _filePath, true);
                }
                else
                {
                    var dir = System.IO.Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                    if (System.IO.File.Exists(_filePath)) System.IO.File.Delete(_filePath);
                    System.IO.File.Move(tempFile, _filePath);
                }

                if (zippedTemp != null && System.IO.File.Exists(zippedTemp))
                {
                    try { System.IO.File.Delete(zippedTemp); } catch { /* best effort */ }
                }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            try
            {
                using var stream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".json");
                return await JsonExtractor.GetColumnsAsync(stream, _rootPath, effectiveCancellationToken);
            }
            catch (Exception ex) { _logger.Debug("[JsonDataSource.GetColumnsAsync] Failed to read columns from '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
        }

        private string PrepareReadPath(List<string> tempFiles, string extension)
        {
            var effectivePath = _filePath;

            if (_encryption.Enabled)
            {
                var decryptedTemp = System.IO.Path.GetTempFileName();
                tempFiles.Add(decryptedTemp);
                _encryption.DecryptFile(_filePath, decryptedTemp);
                effectivePath = decryptedTemp;
            }

            if (_compress && (_filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                              || effectivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                              || _encryption.Enabled))
            {
                var extractedTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + extension);
                tempFiles.Add(extractedTemp);
                using var zip = System.IO.Compression.ZipFile.OpenRead(effectivePath);
                var entry = zip.Entries.FirstOrDefault();
                if (entry != null)
                {
                    entry.ExtractToFile(extractedTemp, true);
                    effectivePath = extractedTemp;
                }
            }

            return effectivePath;
        }

        private void DeleteTempFiles(IEnumerable<string> tempFiles)
        {
            foreach (var tempFile in tempFiles.Reverse())
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

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (sql.Trim().ToUpperInvariant().StartsWith("SELECT * FROM FILE"))
            {
                await foreach (var batch in ReadBatches(10000, cancellationToken).WithCancellation(cancellationToken))
                    yield return batch;
            }
            else
            {
                _logger.Debug("[JSON] ExecuteRawSql received unknown SQL: {Sql}. Returning empty result as native pushdown is not supported.", sql);
                yield return new DataTable { ColumnNames = { "Status" }, Rows = { new Row { ["Status"] = "NOT_SUPPORTED" } } };
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "JSON";
        public bool SupportsSqlPushdown => false;
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
        public Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            EffectiveCancellationToken(cancellationToken).ThrowIfCancellationRequested();
            return GetTablesAsync();
        }
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
        {
            EffectiveCancellationToken(cancellationToken).ThrowIfCancellationRequested();
            return GetViewsAsync();
        }
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync(tableName, CancellationToken.None);
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken) =>
            string.Equals(tableName, "FILE", StringComparison.OrdinalIgnoreCase)
                ? GetColumnsAsync(cancellationToken)
                : Task.FromResult(Enumerable.Empty<string>());

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : _context.CancellationToken;
    }
}
