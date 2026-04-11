using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using System.IO.Compression;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.FlatFile
{
    /// <summary>
    /// Data source implementation for reading and writing delimited text files (CSV, TSV, etc.).
    /// </summary>
    public class FlatFileDataSource : IDatabaseSource
    {
        private readonly string _filePath;
        private readonly bool _hasHeader;
        private readonly char _delimiter;
        private readonly Encoding _encoding;
        private readonly char? _textQualifier;
        private readonly int _startAtRows;
        private readonly int _endAtRows;
        private readonly string? _countAtEndPattern;
        private readonly string? _headerFile;
        private readonly string _rowDelimiter;
        private readonly char? _escapeChar;
        private readonly string? _nullAs;
        private readonly string? _dateFormat;
        private readonly bool _strictSchema;
        private readonly bool _compress;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly List<FixedWidthColumn>? _fixedColumns;
        private readonly bool _trim = true;
        private readonly ILogger _logger;

        private class FixedWidthColumn
        {
            public string Name { get; set; } = "";
            public int Start { get; set; }
            public int Length { get; set; }
        }

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "FLATFILE";

        public FlatFileDataSource(string filePath, Dictionary<string, string>? options = null, IEnumerable<ColumnDefinition>? templateSchema = null, ILogger? logger = null)
        {
            _filePath = filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n');
            _options = options;
            _logger = logger ?? NullLogger.Instance; // Fallback to global for backward compatibility during transition
            _hasHeader = true;
            _delimiter = ',';
            _encoding = Encoding.UTF8;
            _textQualifier = '"';
            _startAtRows = 0;
            _endAtRows = 0;
            _countAtEndPattern = null;
            _headerFile = null;
            _rowDelimiter = "\n";

            if (options != null)
            {
                if (options.TryGetValue("HEADER", out var h))
                {
                    string hv = h.ToUpperInvariant();
                    if (hv == "OFF" || hv == "FALSE") _hasHeader = false;
                    else if (hv == "ON" || hv == "TRUE") _hasHeader = true;
                    else _headerFile = h;
                }

                if (options.TryGetValue("DELIMITER", out var d))
                {
                    _delimiter = d.ToUpperInvariant() switch
                    {
                        "PIPE" => '|',
                        "TAB" => '\t',
                        "COMMA" => ',',
                        "SEMICOLON" => ';',
                        "COLON" => ':',
                        "TILDE" => '~',
                        _ => d.Length == 1 ? d[0] : ','
                    };
                }

                if (options.TryGetValue("ROW_DELIMITER", out var rd))
                {
                    _rowDelimiter = rd.ToUpperInvariant() switch
                    {
                        "LF" => "\n",
                        "CR" => "\r",
                        "CRLF" => "\r\n",
                        "TILDE" => "~",
                        "SEMICOLON" => ";",
                        "COLON" => ":",
                        "COMMA" => ",",
                        "TAB" => "\t",
                        "PIPE" => "|",
                        _ => rd
                    };
                }

                if (options.TryGetValue("ESCAPE_CHAR", out var esc))
                {
                    if (esc.Length == 1) _escapeChar = esc[0];
                }

                if (options.TryGetValue("NULL_AS", out var nullas))
                {
                    _nullAs = nullas.ToUpperInvariant() switch {
                        "EMPTY" => "",
                        "BACKSLASH_N" => "\\n",
                        "NULL" => "NULL",
                        _ => nullas
                    };
                }

                if (options.TryGetValue("DATE_FORMAT", out var df))
                {
                    _dateFormat = df;
                }

                if (options.TryGetValue("STRICT_SCHEMA", out var ss))
                {
                    _strictSchema = ss.ToUpperInvariant() == "ON" || ss.ToUpperInvariant() == "TRUE";
                }

                if (options.TryGetValue("ENCODING", out var enc))
                {
                    if (enc.ToUpperInvariant() == "ANSI")
                        _encoding = Encoding.Latin1;
                    else if (enc.ToUpperInvariant() == "UTF8")
                        _encoding = Encoding.UTF8;
                }

                if (options.TryGetValue("TEXT_QUALIFIER", out var tq))
                {
                    _textQualifier = tq.ToUpperInvariant() switch
                    {
                        "DOUBLEQUOTE" => '"',
                        "DOUBLEQUOTES" => '"',
                        "\"" => '"',
                        "SINGLEQUOTE" => '\'',
                        "SINGLEQUOTES" => '\'',
                        "\'" => '\'',
                        _ => tq.Length == 1 ? tq[0] : (char?)null
                    };
                }

                if (options.TryGetValue("START_AT", out var sa) && int.TryParse(sa, out var sav))
                    _startAtRows = sav;

                if (options.TryGetValue("END_AT", out var ea) && int.TryParse(ea, out var eav))
                    _endAtRows = eav;

                if (options.TryGetValue("COUNT_AT_END", out var cae))
                    _countAtEndPattern = cae;

                if (options.TryGetValue("COMPRESS", out var comp))
                    _compress = comp.ToUpperInvariant() == "ON" || comp.ToUpperInvariant() == "TRUE";

                if (options.TryGetValue("TRIM", out var tr))
                    _trim = tr.ToUpperInvariant() == "ON" || tr.ToUpperInvariant() == "TRUE";

                if (options.TryGetValue("FORMAT", out var fmt) && fmt.ToUpperInvariant() == "FIXED")
                {
                    if (templateSchema == null)
                        throw new ExecutionException("TEMPLATE table required for FORMAT='FIXED'.");

                    _fixedColumns = new List<FixedWidthColumn>();
                    int currentStart = 0;
                    foreach (var col in templateSchema)
                    {
                        int? width = GetWidthFromColumn(col);
                        if (!width.HasValue)
                            throw new ExecutionException($"Width not defined for fixed-width column '{col.ColumnName}'. Use VARCHAR(N) or /* @width: N */.");

                        _fixedColumns.Add(new FixedWidthColumn
                        {
                            Name = col.ColumnName,
                            Start = currentStart,
                            Length = width.Value
                        });
                        currentStart += width.Value;
                    }
                }
            }

            _encryption = new EncryptionOptions(options);
        }

        private int? GetWidthFromColumn(ColumnDefinition col)
        {
            if (col.Metadata.TryGetValue("width", out var wStr) && int.TryParse(wStr, out var w))
                return w;

            var match = Regex.Match(col.DataType, @"\((\d+)\)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var typeWidth))
                return typeWidth;

            return null;
        }

        private string[] SplitFixedWidthLine(string line)
        {
            if (_fixedColumns == null) return Array.Empty<string>();
            var result = new string[_fixedColumns.Count];
            for (int i = 0; i < _fixedColumns.Count; i++)
            {
                var col = _fixedColumns[i];
                if (col.Start >= line.Length)
                {
                    result[i] = "";
                    continue;
                }
                int len = Math.Min(col.Length, line.Length - col.Start);
                var val = line.Substring(col.Start, len);
                result[i] = _trim ? val.Trim() : val;
            }
            return result;
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        private string[] SplitLine(string line)
        {
            if (_textQualifier == null && !_escapeChar.HasValue)
                return line.Split(_delimiter);

            char q = _textQualifier ?? '"';
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            int i = 0;

            while (i < line.Length)
            {
                char c = line[i];

                if (_escapeChar.HasValue && c == _escapeChar.Value && i + 1 < line.Length)
                {
                    sb.Append(line[i + 1]);
                    i += 2;
                    continue;
                }

                if (char.IsWhiteSpace(c) && c != _delimiter && sb.Length == 0)
                {
                    i++;
                    continue;
                }

                if (_textQualifier.HasValue && c == q)
                {
                    i++;
                    while (i < line.Length)
                    {
                        char qc = line[i];
                        if (_escapeChar.HasValue && qc == _escapeChar.Value && i + 1 < line.Length)
                        {
                            sb.Append(line[i + 1]);
                            i += 2;
                            continue;
                        }

                        if (qc == q)
                        {
                            if (i + 1 < line.Length && line[i + 1] == q)
                            {
                                sb.Append(q);
                                i += 2;
                            }
                            else
                            {
                                i++;
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(qc);
                            i++;
                        }
                    }
                    while (i < line.Length && line[i] != _delimiter) i++;
                }
                else if (c == _delimiter)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private string FormatField(string value)
        {
            if (_textQualifier == null)
                return value;

            char q = _textQualifier.Value;
            bool needsQuoting = value.Contains(_delimiter) || value.Contains(q) || value.Contains('\n') || value.Contains('\r');
            
            if (!needsQuoting) return value;

            string escaped = value.Replace(q.ToString(), new string(q, 2));
            return $"{q}{escaped}{q}";
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (!System.IO.File.Exists(_filePath))
                yield break;

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
                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".csv");
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
                using var reader = new StreamReader(effectivePath, _encoding);
                
                for (int i = 0; i < _startAtRows; i++) await ReadRecordAsync(reader);

            string? headerLine = null;
            if (_headerFile != null)
            {
                if (System.IO.File.Exists(_headerFile))
                {
                    headerLine = (await System.IO.File.ReadAllTextAsync(_headerFile)).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }
                else
                {
                    headerLine = _headerFile;
                }
                
                if (_hasHeader) await ReadRecordAsync(reader);
            }
            else
            {
                headerLine = await ReadRecordAsync(reader);
            }

            if (string.IsNullOrWhiteSpace(headerLine))
                yield break;

            var headers = _fixedColumns != null ? SplitFixedWidthLine(headerLine) : SplitLine(headerLine);
            var currentBatch = new DataTable();

            if (_hasHeader)
            {
                currentBatch.SetColumns(headers.Select(h => h.Trim()));
            }
            else
            {
                var colNames = _fixedColumns != null 
                    ? _fixedColumns.Select(c => c.Name).ToList() 
                    : Enumerable.Range(1, headers.Length).Select(i => $"Col{i}").ToList();
                currentBatch.SetColumns(colNames);
                await currentBatch.AddRowAsync(CreateRow(headers, currentBatch));
            }

            var actualHeaders = new List<string>(currentBatch.ColumnNames);
            int totalRowsRead = 0;

            var lineQueue = new Queue<string>();
            string? line;
            while ((line = await ReadRecordAsync(reader)) != null)
            {
                lineQueue.Enqueue(line);
                if (lineQueue.Count > _endAtRows + (_countAtEndPattern != null ? 1 : 0))
                {
                    var dataLine = lineQueue.Dequeue();
                    await ProcessDataLine(dataLine, currentBatch, actualHeaders);
                    totalRowsRead++;

                    if (currentBatch.Rows.Count >= batchSize)
                    {
                        yield return currentBatch;
                        currentBatch = new DataTable();
                        currentBatch.SetColumns(actualHeaders);
                    }
                }
            }

            if (_countAtEndPattern != null && lineQueue.Count > _endAtRows)
            {
                var countLine = lineQueue.Dequeue();
                ValidateFooterCount(countLine, totalRowsRead);
            }

            if (currentBatch.Rows.Count > 0)
                yield return currentBatch;
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        private async Task<string?> ReadRecordAsync(StreamReader reader)
        {
            if (reader.Peek() == -1) return null;

            if (_rowDelimiter == "\n" || _rowDelimiter == "\r\n" || _rowDelimiter == "\r")
            {
                return await reader.ReadLineAsync();
            }

            var sb = new StringBuilder();
            int delimIdx = 0;
            
            while (reader.Peek() != -1)
            {
                int cInt = reader.Read();
                if (cInt == -1) break;
                char c = (char)cInt;
                
                sb.Append(c);
                
                if (c == _rowDelimiter[delimIdx])
                {
                    delimIdx++;
                    if (delimIdx == _rowDelimiter.Length)
                    {
                        sb.Length -= _rowDelimiter.Length;
                        return sb.ToString();
                    }
                }
                else
                {
                    delimIdx = 0;
                    if (c == _rowDelimiter[0]) delimIdx = 1;
                }
            }
            
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private async Task ProcessDataLine(string line, DataTable batch, List<string> actualHeaders)
        {
            var values = _fixedColumns != null ? SplitFixedWidthLine(line) : SplitLine(line);
            await batch.AddRowAsync(CreateRow(values, batch));
        }

        private Row CreateRow(string[] values, DataTable batch)
        {
            var row = batch.NewRow();
            var columnNames = batch.ColumnNames;
            for (int i = 0; i < columnNames.Count && i < values.Length; i++)
            {
                string val = _textQualifier == null ? values[i].Trim() : values[i];
                
                if (_nullAs != null && (val.Equals(_nullAs, StringComparison.OrdinalIgnoreCase) || (string.IsNullOrEmpty(val) && _nullAs == "")))
                {
                    row[i] = null;
                }
                else
                {
                    if (_dateFormat != null && DateTime.TryParseExact(val, _dateFormat, null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        row[i] = dt;
                    }
                    else
                    {
                        row[i] = val;
                    }
                }
            }
            return row;
        }

        private void ValidateFooterCount(string footerLine, int actualCount)
        {
            if (string.IsNullOrEmpty(_countAtEndPattern)) return;
            var pattern = _countAtEndPattern.Replace("COUNT", "(\\d+)");
            var match = Regex.Match(footerLine, pattern);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out var expected) && expected != actualCount)
                {
                    _logger.WriteLine($"[WARNING] CSV Count Mismatch! Expected: {expected}, Actual: {actualCount}", ConsoleColor.Yellow);
                }
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                using (var writer = new StreamWriter(tempFile, false, _encoding))
                {
                    bool headersWritten = false;
                    int totalRows = 0;

                    await foreach (var batch in batches)
                    {
                        if (!headersWritten && batch.ColumnNames.Count > 0)
                        {
                            if (!string.IsNullOrEmpty(_headerFile) && System.IO.File.Exists(_headerFile))
                            {
                                await writer.WriteLineAsync(await System.IO.File.ReadAllTextAsync(_headerFile).ConfigureAwait(false));
                            }
                            else if (_hasHeader)
                            {
                                var headerFields = batch.ColumnNames.Select(n => FormatField(n));
                                await writer.WriteLineAsync(string.Join(_delimiter.ToString(), headerFields)).ConfigureAwait(false);
                            }
                            headersWritten = true;
                        }

                        foreach (var row in batch.Rows)
                        {
                            var values = new List<string>();
                            foreach (var col in batch.ColumnNames)
                                values.Add(FormatField(row[col]?.ToString() ?? ""));
                            await writer.WriteLineAsync(string.Join(_delimiter.ToString(), values)).ConfigureAwait(false);
                            totalRows++;
                        }
                    }

                    if (!string.IsNullOrEmpty(_countAtEndPattern))
                    {
                        await writer.WriteLineAsync(_countAtEndPattern.Replace("COUNT", totalRows.ToString())).ConfigureAwait(false);
                    }
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
                catch (Exception ex) { _logger.Debug($"[FlatFileDataSource.GetColumnsAsync] Failed to decrypt '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            }
            else if (_compress && _filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() + ".csv");
                try
                {
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(_filePath))
                    {
                        var entry = zip.Entries.FirstOrDefault();
                        if (entry != null) { entry.ExtractToFile(tempFile, true); effectivePath = tempFile; }
                        else return Enumerable.Empty<string>();
                    }
                }
                catch (Exception ex) { _logger.Debug($"[FlatFileDataSource.GetColumnsAsync] Failed to decompress '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            }

            try
            {
                string? headerLine = null;
                if (_headerFile != null)
                {
                    if (System.IO.File.Exists(_headerFile))
                        headerLine = (await System.IO.File.ReadAllTextAsync(_headerFile)).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    else
                        headerLine = _headerFile;
                }
                else
                {
                    using var reader = new StreamReader(effectivePath, _encoding);
                    for (int i = 0; i < _startAtRows; i++) await reader.ReadLineAsync();
                    headerLine = await reader.ReadLineAsync();
                }

                if (string.IsNullOrWhiteSpace(headerLine)) return Enumerable.Empty<string>();
                var headers = SplitLine(headerLine);
                if (_hasHeader) return headers.Select(h => h.Trim());
                return headers.Select((h, i) => $"Col{i + 1}");
            }
            catch (Exception ex) { _logger.Debug($"[FlatFileDataSource.GetColumnsAsync] Failed to read headers from '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public async Task TruncateAsync()
        {
            if (System.IO.File.Exists(_filePath))
            {
                System.IO.File.WriteAllText(_filePath, string.Empty);
            }
            await Task.CompletedTask;
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
                throw new ExecutionException("FlatFile connector only supports 'SELECT * FROM FILE' as native SQL.");
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "FLATFILE";
        public bool SupportsSqlPushdown => false;
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();
    }
}
