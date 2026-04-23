using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using System.IO.Compression;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Xml
{
    /// <summary>
    /// Data source implementation for reading and writing XML files.
    /// Supports XPath-style root selection, file compression (ZIP), and encryption.
    /// </summary>
    public class XmlDataSource : IDatabaseSource
    {
        private readonly string _filePath;
        private readonly string? _rootPath;
        private readonly bool _compress;
        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly bool _trim = true;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "XML";

        public XmlDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null)
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
                _rootPath = options.TryGetValue("ROOT_PATH", out var rp) ? rp : null;
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

            string effectivePath = await GetEffectivePathAsync();
            bool isTemp = effectivePath != _filePath;

            try
            {
                XDocument doc;
                using (var stream = new StreamReader(effectivePath, _encoding))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.None, default);
                }

                var elements = GetElements(doc).ToList();
                if (elements.Count == 0) yield break;

                // Pass 1: Discover Schema
                var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in elements)
                {
                    foreach (var attr in element.Attributes()) allColumns.Add(attr.Name.LocalName);
                    foreach (var sub in element.Elements())
                    {
                        if (!sub.HasElements) allColumns.Add(sub.Name.LocalName);
                    }
                }

                var schema = new TableSchema();
                foreach (var col in allColumns.OrderBy(c => c))
                {
                    schema.AddColumn(col);
                }

                // Pass 2: Populate Batches
                var currentBatch = new DataTable();
                currentBatch.SetColumns(schema.ColumnNames);
                var activeSchema = currentBatch.Schema;

                foreach (var element in elements)
                {
                    var row = currentBatch.NewRow();
                    foreach (var attr in element.Attributes())
                    {
                        int idx = activeSchema.GetIndex(attr.Name.LocalName);
                        if (idx >= 0) row[idx] = attr.Value;
                    }
                    foreach (var sub in element.Elements())
                    {
                        if (!sub.HasElements)
                        {
                            int idx = activeSchema.GetIndex(sub.Name.LocalName);
                            if (idx >= 0) row[idx] = _trim ? sub.Value.Trim() : sub.Value;
                        }
                    }

                    await currentBatch.AddRowAsync(row);
                    if (currentBatch.Rows.Count >= batchSize)
                    {
                        yield return currentBatch;
                        currentBatch = new DataTable();
                        currentBatch.SetColumns(schema.ColumnNames);
                    }
                }

                if (currentBatch.Rows.Count > 0)
                {
                    yield return currentBatch;
                }
            }
            finally
            {
                if (isTemp) TempFileHelper.SafeDelete(effectivePath, _logger);
            }
        }

        private async Task<string> GetEffectivePathAsync()
        {
            if (_encryption.Enabled)
            {
                string tempFile = System.IO.Path.GetTempFileName();
                _encryption.DecryptFile(_filePath, tempFile);
                return tempFile;
            }
            if (_compress && _filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".xml");
                using (var zip = System.IO.Compression.ZipFile.OpenRead(_filePath))
                {
                    var entry = zip.Entries.FirstOrDefault();
                    if (entry != null)
                    {
                        entry.ExtractToFile(tempFile, true);
                        return tempFile;
                    }
                }
            }
            return _filePath;
        }

        private IEnumerable<XElement> GetElements(XDocument doc)
        {
            if (string.IsNullOrEmpty(_rootPath))
            {
                return doc.Root?.Elements() ?? Enumerable.Empty<XElement>();
            }
            
            var current = doc.Root;
            var parts = _rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (current == null) break;
                if (current.Name.LocalName.Equals(part, StringComparison.OrdinalIgnoreCase) && part == parts[0]) continue;
                current = current.Element(part);
            }
            return current?.Elements() ?? Enumerable.Empty<XElement>();
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            bool alreadyXml = false;
            string? singleXml = null;

            await foreach (var b in batches)
            {
                if (b.ColumnNames.Count == 1 && b.ColumnNames[0] == "XML_F52E2B61")
                {
                    alreadyXml = true;
                    singleXml = b.Rows.FirstOrDefault()?[0]?.ToString();
                    break;
                }
                else
                {
                    break; 
                }
            }

            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                if (alreadyXml && singleXml != null)
                {
                    await System.IO.File.WriteAllTextAsync(tempFile, singleXml);
                }
                else
                {
                    var rootName = _rootPath ?? "root";
                    var root = new XElement(rootName);
                    await foreach (var b in batches)
                    {
                        var columnNames = b.ColumnNames;
                        foreach (var r in b.Rows)
                        {
                            var rowElem = new XElement("row");
                            for (int i = 0; i < columnNames.Count; i++)
                            {
                                var col = columnNames[i];
                                var val = r[i];
                                if (val == null) continue;

                                var parts = col.Split('.');
                                XElement current = rowElem;
                                for (int j = 0; j < parts.Length - 1; j++)
                                {
                                    var sub = current.Element(parts[j]);
                                    if (sub == null)
                                    {
                                        sub = new XElement(parts[j]);
                                        current.Add(sub);
                                    }
                                    current = sub;
                                }
                                current.Add(new XElement(parts.Last(), val));
                            }
                            root.Add(rowElem);
                        }
                    }
                    await System.IO.File.WriteAllTextAsync(tempFile, root.ToString());
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
            try
            {
                var enumerator = ReadBatches(1).GetAsyncEnumerator();
                if (await enumerator.MoveNextAsync())
                {
                    return enumerator.Current.ColumnNames;
                }
                return Enumerable.Empty<string>();
            }
            catch (Exception ex) { _logger.Debug("[XmlDataSource.GetColumnsAsync] Failed to read columns from '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

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
            if (sql.Trim().ToUpperInvariant().StartsWith("SELECT * FROM ROOT"))
            {
                await foreach (var batch in ReadBatches()) yield return batch;
            }
            else
            {
                throw new ExecutionException("Xml connector only supports 'SELECT * FROM ROOT' as native SQL.");
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "XML";
        public bool SupportsSqlPushdown => false;
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "ROOT" });
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();
    }
}
