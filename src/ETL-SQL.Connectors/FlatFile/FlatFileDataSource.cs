using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;
using System.IO.Compression;

namespace ETL_SQL.Connectors.FlatFile
{
    /// <summary>
    /// Data source implementation for reading and writing delimited text files (CSV, TSV, etc.).
    /// Supports advanced features like custom delimiters, row delimiters, encoding, text qualifiers, 
    /// compression, encryption, and footer count validation.
    /// </summary>
    public class FlatFileDataSource : IDataSource
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
        private readonly bool _encrypt;
        private readonly string _password;

        /// <summary>Gets the physical path to the flat file.</summary>
        public string Path => _filePath;
        /// <summary>Returns this instance as a typed table (no-op for FlatFile).</summary>
        public IDataSource WithTable(string tableName) => this;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlatFileDataSource"/> class.
        /// </summary>
        /// <param name="filePath">The path to the delimited file.</param>
        /// <param name="options">Optional configuration parameters.</param>
        public FlatFileDataSource(string filePath, Dictionary<string, string>? options = null)
        {
            _filePath = filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n');
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

                if (options.TryGetValue("ENCRYPT", out var encr))
                    _encrypt = encr.ToUpperInvariant() == "ON" || encr.ToUpperInvariant() == "TRUE";

                if (options.TryGetValue("PASSWORD", out var p))
                    _password = p;
                else
                    _password = "DefaultETLPass123!";
            }
            else
            {
                _password = "DefaultETLPass123!";
            }
        }

        /// <summary>Captures a snapshot (no-op for FlatFile).</summary>
        public object? Snapshot() => null;

        /// <summary>Restores from a snapshot (no-op for FlatFile).</summary>
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

        /// <summary>Reads data from the flat file in batches.</summary>
        /// <param name="batchSize">The number of rows per batch.</param>
        /// <returns>An async enumerable of DataTables.</returns>
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (!System.IO.File.Exists(_filePath))
                yield break;

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

            var headers = SplitLine(headerLine);
            var currentBatch = new DataTable();

            if (_hasHeader)
            {
                foreach (var h in headers) currentBatch.ColumnNames.Add(h.Trim());
            }
            else
            {
                for (int i = 0; i < headers.Length; i++) currentBatch.ColumnNames.Add($"Col{i + 1}");
                currentBatch.AddRow(CreateRow(headers, currentBatch.ColumnNames));
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
                    ProcessDataLine(dataLine, currentBatch, actualHeaders);
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
                TempFileHelper.SafeDelete(tempFile);
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

        private void ProcessDataLine(string line, DataTable batch, List<string> headers)
        {
            var values = SplitLine(line);
            
            if (_strictSchema && values.Length != batch.ColumnNames.Count)
            {
                Logger.WriteLine($"[WARNING] CSV Strict Schema Mismatch! Expected {batch.ColumnNames.Count} columns, found {values.Length}");
                if (values.Length < batch.ColumnNames.Count) return;
            }

            batch.AddRow(CreateRow(values, batch.ColumnNames));
        }

        private Row CreateRow(string[] values, IList<string> columnNames)
        {
            var row = new Row();
            for (int i = 0; i < columnNames.Count && i < values.Length; i++)
            {
                string val = _textQualifier == null ? values[i].Trim() : values[i];
                
                // Handle NULL_AS
                if (_nullAs != null && (val.Equals(_nullAs, StringComparison.OrdinalIgnoreCase) || (string.IsNullOrEmpty(val) && _nullAs == "")))
                {
                    row[columnNames[i]] = null;
                }
                else
                {
                    // Handle DATE_FORMAT
                    if (_dateFormat != null && DateTime.TryParseExact(val, _dateFormat, null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        row[columnNames[i]] = dt;
                    }
                    else
                    {
                        row[columnNames[i]] = val;
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
                    Logger.WriteLine($"[WARNING] CSV Count Mismatch! Expected: {expected}, Actual: {actualCount}");
                }
            }
        }

        /// <summary>Writes batches of data to the flat file.</summary>
        /// <param name="batches">An async enumerable of DataTables to write.</param>
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

        /// <summary>Asynchronously retrieves the column names from the file.</summary>
        /// <returns>A collection of column names.</returns>
        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();
            
            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encrypt)
            {
                tempFile = System.IO.Path.GetTempFileName();
                try { CryptoUtils.DecryptFile(_filePath, tempFile, _password); effectivePath = tempFile; }
                catch (Exception ex) { Logger.Verbose($"[FlatFileDataSource.GetColumnsAsync] Failed to decrypt '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
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
                catch (Exception ex) { Logger.Verbose($"[FlatFileDataSource.GetColumnsAsync] Failed to decompress '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
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
            catch (Exception ex) { Logger.Verbose($"[FlatFileDataSource.GetColumnsAsync] Failed to read headers from '{_filePath}': {ex.Message}"); return Enumerable.Empty<string>(); }
            finally
            {
                TempFileHelper.SafeDelete(tempFile);
            }
        }
        /// <summary>Truncates the file by writing an empty data set.</summary>
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

