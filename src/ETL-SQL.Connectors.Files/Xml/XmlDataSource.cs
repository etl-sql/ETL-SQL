using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

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

            Func<Stream> opener = () => FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".xml");

            try
            {
                // Pass 1 — stream through once to discover column names (no data retained)
                var columnNames = await DiscoverColumnsAsync(opener, effectiveCancellationToken);
                if (columnNames.Count == 0) yield break;

                var schema = new TableSchema();
                foreach (var col in columnNames) schema.AddColumn(col);

                // Pass 2 — stream through again, yielding one batch at a time
                var currentBatch = new DataTable();
                currentBatch.SetColumns(schema.ColumnNames);
                var activeSchema = currentBatch.Schema;

                await foreach (var record in StreamRecordsAsync(opener, effectiveCancellationToken)
                    .WithCancellation(effectiveCancellationToken))
                {
                    var row = currentBatch.NewRow();
                    foreach (var (name, value) in record.Attributes)
                    {
                        int idx = activeSchema.GetIndex(name);
                        if (idx >= 0) row[idx] = value;
                    }
                    foreach (var (name, value) in record.Children)
                    {
                        int idx = activeSchema.GetIndex(name);
                        if (idx >= 0) row[idx] = _trim ? value.Trim() : value;
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
                    yield return currentBatch;
            }
            finally
            {
            }
        }

        // ── Streaming helpers ─────────────────────────────────────────────────

        private readonly record struct XmlRecord(
            List<(string Name, string Value)> Attributes,
            List<(string Name, string Value)> Children);

        private async Task<List<string>> DiscoverColumnsAsync(
            Func<Stream> streamOpener,
            CancellationToken cancellationToken = default)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var record in StreamRecordsAsync(streamOpener, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                foreach (var (name, _) in record.Attributes) columns.Add(name);
                foreach (var (name, _) in record.Children) columns.Add(name);
            }
            return columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Streams one <see cref="XmlRecord"/> per repeating element without loading
        /// the full document into memory. Both attribute values and direct leaf-child
        /// text values are captured; nested elements are skipped (same behaviour as
        /// the previous XDocument implementation).
        /// </summary>
        private async IAsyncEnumerable<XmlRecord> StreamRecordsAsync(
            Func<Stream> streamOpener,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var settings = new XmlReaderSettings { Async = true };
            using var fileStream = streamOpener();
            using var textReader = new StreamReader(fileStream, _encoding);
            using var reader = XmlReader.Create(textReader, settings);

            int containerDepth = await NavigateToContainerAsync(reader, cancellationToken);
            if (containerDepth < 0) yield break;

            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Back past the container — done
                if (reader.Depth <= containerDepth) break;

                if (reader.NodeType != XmlNodeType.Element || reader.Depth != containerDepth + 1)
                    continue;

                var attrs = new List<(string, string)>();
                for (int i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    attrs.Add((reader.LocalName, reader.Value));
                }
                reader.MoveToElement();

                var children = new List<(string, string)>();
                if (!reader.IsEmptyElement)
                {
                    // XmlSubtreeReader resets Depth to 0 at the record element, so direct child
                    // elements are at sub-depth 1 and their text content is at sub-depth 2.
                    using var sub = reader.ReadSubtree();
                    await sub.ReadAsync(); // consume the record element itself (sub-depth 0)

                    string? childName = null;
                    bool childHasElements = false;
                    var childText = new System.Text.StringBuilder();

                    while (await sub.ReadAsync())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        switch (sub.NodeType)
                        {
                            case XmlNodeType.Element when sub.Depth == 1:
                                FlushChild(children, childName, childHasElements, childText);
                                childName = sub.LocalName;
                                childHasElements = false;
                                childText.Clear();
                                break;
                            case XmlNodeType.Element when sub.Depth > 1:
                                childHasElements = true;
                                break;
                            case XmlNodeType.Text or XmlNodeType.CDATA when sub.Depth == 2:
                                childText.Append(sub.Value);
                                break;
                            case XmlNodeType.EndElement when sub.Depth == 1:
                                FlushChild(children, childName, childHasElements, childText);
                                childName = null;
                                childHasElements = false;
                                childText.Clear();
                                break;
                        }
                    }
                }

                yield return new XmlRecord(attrs, children);
            }
        }

        private static void FlushChild(
            List<(string, string)> children, string? name, bool hasElements, System.Text.StringBuilder text)
        {
            if (name != null && !hasElements)
                children.Add((name, text.ToString()));
        }

        /// <summary>
        /// Advances <paramref name="reader"/> to the container element whose direct
        /// children are the repeating record elements, honoring <see cref="_rootPath"/>.
        /// Returns the depth of that container, or -1 if navigation fails.
        /// </summary>
        private async Task<int> NavigateToContainerAsync(XmlReader reader, CancellationToken cancellationToken = default)
        {
            // Advance to the document root element
            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element) break;
            }
            if (reader.EOF) return -1;

            if (string.IsNullOrEmpty(_rootPath))
                return reader.Depth; // records are direct children of the document root

            var parts = _rootPath.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int startIdx = 0;

            // If the first segment names the root element, skip it — we're already there
            if (parts.Length > 0 && reader.LocalName.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                startIdx = 1;

            for (int i = startIdx; i < parts.Length; i++)
            {
                var target = parts[i];
                bool found = false;
                while (await reader.ReadAsync())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.LocalName.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return -1;
            }

            return reader.Depth;
        }

        private string GetEffectivePath(List<string> tempFiles)
        {
            var effectivePath = _filePath;

            if (_encryption.Enabled)
            {
                string decryptedTemp = System.IO.Path.GetTempFileName();
                tempFiles.Add(decryptedTemp);
                _encryption.DecryptFile(_filePath, decryptedTemp);
                effectivePath = decryptedTemp;
            }

            if (_compress && (_filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                              || effectivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                              || _encryption.Enabled))
            {
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".xml");
                tempFiles.Add(tempFile);
                using (var zip = System.IO.Compression.ZipFile.OpenRead(effectivePath))
                {
                    var entry = zip.Entries.FirstOrDefault();
                    if (entry != null)
                    {
                        entry.ExtractToFile(tempFile, true);
                        effectivePath = tempFile;
                    }
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);
            bool alreadyXml = false;
            string? singleXml = null;

            await foreach (var b in batches.WithCancellation(effectiveCancellationToken))
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
                    await System.IO.File.WriteAllTextAsync(tempFile, singleXml, effectiveCancellationToken);
                }
                else
                {
                    XElement root;
                    XElement deepRoot;

                    if (string.IsNullOrEmpty(_rootPath))
                    {
                        root = new XElement("root");
                        deepRoot = root;
                    }
                    else
                    {
                        var pathParts = _rootPath.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
                        root = new XElement(pathParts[0]);
                        deepRoot = root;
                        for (int k = 1; k < pathParts.Length; k++)
                        {
                            var sub = new XElement(pathParts[k]);
                            deepRoot.Add(sub);
                            deepRoot = sub;
                        }
                    }

                    await foreach (var b in batches.WithCancellation(effectiveCancellationToken))
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
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
                            deepRoot.Add(rowElem);
                        }
                    }
                    await System.IO.File.WriteAllTextAsync(tempFile, root.ToString(), effectiveCancellationToken);
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
                await using var enumerator = ReadBatches(1, effectiveCancellationToken).GetAsyncEnumerator(effectiveCancellationToken);
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

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (sql.Trim().ToUpperInvariant().StartsWith("SELECT * FROM ROOT"))
            {
                await foreach (var batch in ReadBatches(10000, cancellationToken).WithCancellation(cancellationToken))
                    yield return batch;
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
            string.Equals(tableName, "ROOT", StringComparison.OrdinalIgnoreCase)
                ? GetColumnsAsync(cancellationToken)
                : Task.FromResult(Enumerable.Empty<string>());

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
