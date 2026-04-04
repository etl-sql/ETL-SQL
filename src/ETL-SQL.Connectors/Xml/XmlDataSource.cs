using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using System.IO.Compression;

namespace ETL_SQL.Connectors.Xml
{
    /// <summary>
    /// Data source implementation for reading and writing XML files.
    /// Supports XPath-style root selection, file compression (ZIP), and encryption.
    /// </summary>
    public class XmlDataSource : IDataSource
    {
        private readonly string _filePath;
        private readonly string? _rootPath;
        private readonly bool _compress;
        private readonly bool _encrypt;
        private readonly string _password;
        /// <summary>Gets the physical path to the XML file.</summary>
        public string Path => _filePath;
        
        /// <summary>Returns this instance as a typed table (no-op for XML).</summary>
        public IDataSource WithTable(string tableName) => this;

        /// <summary>
        /// Initializes a new instance of the <see cref="XmlDataSource"/> class.
        /// </summary>
        /// <param name="filePath">The path to the XML file.</param>
        /// <param name="options">Optional configuration params (ROOT_PATH, COMPRESS, etc.).</param>
        public XmlDataSource(string filePath, Dictionary<string, string>? options = null)
        {
            _filePath = filePath;
            if (options != null)
            {
                _rootPath = options.TryGetValue("ROOT_PATH", out var rp) ? rp : null;
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

        /// <summary>Reads data from the XML file in batches.</summary>
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
                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".xml");
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
                XDocument doc;
                using (var stream = System.IO.File.OpenRead(effectivePath))
                {
                    doc = await XDocument.LoadAsync(stream, LoadOptions.None, default);
                }

            IEnumerable<XElement> elements;
            if (string.IsNullOrEmpty(_rootPath))
            {
                elements = doc.Root?.Elements() ?? Enumerable.Empty<XElement>();
            }
            else
            {
                var current = doc.Root;
                var parts = _rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (current == null) break;
                    if (current.Name.LocalName.Equals(part, StringComparison.OrdinalIgnoreCase) && part == parts[0]) continue;
                    current = current.Element(part);
                }
                elements = current?.Elements() ?? Enumerable.Empty<XElement>();
            }

            var currentBatch = new DataTable();
            var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in elements)
            {
                var row = new Row();
                foreach (var attr in element.Attributes())
                {
                    row[attr.Name.LocalName] = attr.Value;
                    allColumns.Add(attr.Name.LocalName);
                }
                foreach (var sub in element.Elements())
                {
                    if (!sub.HasElements)
                    {
                        row[sub.Name.LocalName] = sub.Value;
                        allColumns.Add(sub.Name.LocalName);
                    }
                }

                currentBatch.AddRow(row);
                if (currentBatch.Rows.Count >= batchSize)
                {
                    EnsureColumns(currentBatch, allColumns);
                    yield return currentBatch;
                    currentBatch = new DataTable();
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                EnsureColumns(currentBatch, allColumns);
                yield return currentBatch;
            }
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile);
            }
        }

        private void EnsureColumns(DataTable dt, HashSet<string> cols)
        {
            foreach (var col in cols) if (!dt.ColumnNames.Contains(col)) dt.ColumnNames.Add(col);
        }

        /// <summary>Writes batches of data to the XML file.</summary>
        /// <param name="batches">An async enumerable of DataTables.</param>
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            bool alreadyXml = false;
            string? singleXml = null;

            await foreach (var b in batches)
            {
                if (b.ColumnNames.Count == 1 && b.ColumnNames[0] == "XML_F52E2B61")
                {
                    alreadyXml = true;
                    singleXml = b.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault()?.ToString();
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
                        foreach (var r in b.Rows)
                        {
                            var rowElem = new XElement("row");
                            foreach (var col in b.ColumnNames)
                            {
                                var parts = col.Split('.');
                                XElement current = rowElem;
                                for (int i = 0; i < parts.Length - 1; i++)
                                {
                                    var sub = current.Element(parts[i]);
                                    if (sub == null)
                                    {
                                        sub = new XElement(parts[i]);
                                        current.Add(sub);
                                    }
                                    current = sub;
                                }
                                current.Add(new XElement(parts.Last(), r[col]));
                            }
                            root.Add(rowElem);
                        }
                    }
                    await System.IO.File.WriteAllTextAsync(tempFile, root.ToString());
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

        /// <summary>Asynchronously retrieves the column names from the XML element structure.</summary>
        /// <returns>A collection of attribute and element names from the first repeating element.</returns>
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
            catch (Exception ex) { Logger.Verbose($"[XmlDataSource.GetColumnsAsync] Failed to read columns from '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
        }

        /// <summary>Captures a snapshot (no-op for XML).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for XML).</summary>
        public void Restore(object? snapshot) { }

        /// <summary>Truncates the XML file by clearing all data.</summary>
        public async Task TruncateAsync()
        {
            await WriteBatches(AsyncEnumerable.Empty<DataTable>());
        }
        /// <summary>Asynchronously disposes resources.</summary>
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}

