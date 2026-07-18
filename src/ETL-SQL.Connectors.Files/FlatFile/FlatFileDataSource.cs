using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

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
        private readonly IEnumerable<ColumnDefinition>? _templateSchema;
        private readonly bool _ignoreExtraColumns;
        private readonly bool _nullMissingColumns;
        private readonly bool _mapByHeaderName;
        private readonly bool _compress;
        private readonly EncryptionOptions _encryption;
        private readonly Dictionary<string, string>? _options;
        private readonly List<FixedWidthColumn>? _fixedColumns;
        private readonly bool _trim = true;
        private readonly CultureInfo _culture = CultureInfo.InvariantCulture;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context; // Optional for backward compatibility, but required for security enforcement
        private bool _hasValidatedAccess = false;

        private class FixedWidthColumn
        {
            public string Name { get; set; } = "";
            public int Start { get; set; }
            /// <summary>Physical character width of the slot in the file.</summary>
            public int Length { get; set; }
            /// <summary>
            /// For integer types declared as INT(N), this is N — the number of significant
            /// digit characters. The physical <see cref="Length"/> is N+1 to accommodate a
            /// leading minus sign. Null for non-integer types.
            /// </summary>
            public int? DeclaredDigits { get; set; }
        }

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "FLATFILE";

        public FlatFileDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null, IEnumerable<ColumnDefinition>? templateSchema = null)
        {
            _context = context;
            _logger = context.Logger;
            _options = options;
            _templateSchema = templateSchema;
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
                    _nullAs = nullas.ToUpperInvariant() switch
                    {
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

                if (options.TryGetValue("IGNORE_EXTRA_COLUMNS", out var iec))
                {
                    _ignoreExtraColumns = iec.ToUpperInvariant() == "ON" || iec.ToUpperInvariant() == "TRUE";
                }

                if (options.TryGetValue("NULL_MISSING_COLUMNS", out var nmc))
                {
                    _nullMissingColumns = nmc.ToUpperInvariant() == "ON" || nmc.ToUpperInvariant() == "TRUE";
                }

                if (options.TryGetValue("MAP_BY_HEADER_NAME", out var mbh))
                {
                    _mapByHeaderName = mbh.ToUpperInvariant() == "ON" || mbh.ToUpperInvariant() == "TRUE";
                }

                if (options.TryGetValue("ENCODING", out var enc))
                {
                    _encoding = enc.ToUpperInvariant() switch
                    {
                        "ANSI" or "LATIN1" => Encoding.GetEncoding("ISO-8859-1"),
                        "UTF8" => Encoding.UTF8,
                        "UTF16" or "UNICODE" => Encoding.Unicode,
                        "UTF32" => Encoding.UTF32,
                        "ASCII" => Encoding.ASCII,
                        _ => Encoding.GetEncoding(enc)
                    };
                }

                // Culture is already initialized to InvariantCulture
                if (options.TryGetValue("CULTURE", out var cult))
                {
                    try { _culture = new CultureInfo(cult); }
                    catch { _logger.Debug("[FlatFileDataSource] Invalid culture '{Culture}', falling back to Invariant.", cult); }
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
                            throw new ExecutionException($"Width not defined for fixed-width column '{col.ColumnName}'. Use VARCHAR(N), CHAR(N), INT(N), or /* @width: N */.");

                        _fixedColumns.Add(new FixedWidthColumn
                        {
                            Name = col.ColumnName,
                            Start = currentStart,
                            Length = width.Value,
                            DeclaredDigits = GetIntegerDeclaredDigits(col)
                        });
                        currentStart += width.Value;
                    }
                }
            }

            _encryption = new EncryptionOptions(options);

            var resolvedPath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));
            _filePath = FileConnectorPathHelper.CoerceFilePathExtension(resolvedPath, _encryption.Enabled, _compress);

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);
        }

        /// <summary>
        /// Returns the physical character width for a column in a FORMAT=FIXED layout.
        /// Resolution order:
        ///   1. /* @width: N */ metadata tag — taken as-is (physical chars).
        ///   2. Integer types with precision — INT(N), BIGINT(N), SMALLINT(N), TINYINT(N),
        ///      INTEGER(N): returns N+1 (N digit characters + 1 sign character).
        ///   3. Any other type with parenthesised length — VARCHAR(N), CHAR(N), DECIMAL(p,s),
        ///      etc.: returns the first number in the parentheses as-is.
        /// </summary>
        private static int? GetWidthFromColumn(ColumnDefinition col)
        {
            // Priority 1: explicit @width tag
            if (col.Metadata.TryGetValue("width", out var wStr) && int.TryParse(wStr, out var w))
                return w;

            var match = Regex.Match(col.DataType, @"^(\w+)(?:\((\d+)(?:,\d+)?\))?$",
                RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var baseType = match.Groups[1].Value;
            int prec = 0;
            var hasPrec = match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out prec);

            // Priority 2: integer type with declared digit count — physical width = digits + 1 sign slot
            if (IsIntegerType(baseType) && hasPrec)
                return prec + 1;

            // Priority 3: any other sized type — use the length as-is
            if (hasPrec)
                return prec;

            return null;
        }

        /// <summary>
        /// If the column is an integer type declared with a precision (e.g. INT(5)), returns that
        /// precision (the digit count). Returns null for all other types.
        /// </summary>
        private static int? GetIntegerDeclaredDigits(ColumnDefinition col)
        {
            var match = Regex.Match(col.DataType, @"^(\w+)\((\d+)\)$", RegexOptions.IgnoreCase);
            if (match.Success && IsIntegerType(match.Groups[1].Value)
                && int.TryParse(match.Groups[2].Value, out var digits))
                return digits;
            return null;
        }

        private static readonly HashSet<string> _integerTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT"
        };

        private static bool IsIntegerType(string typeName) => _integerTypeNames.Contains(typeName);

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

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeRead(_context, _filePath);
            if (!System.IO.File.Exists(_filePath))
                yield break;

            ValidateFileAccess();

            using var stream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".csv");
            using var reader = new StreamReader(stream, _encoding);
            var recordReader = new RecordReader(reader, _rowDelimiter);

            for (int i = 0; i < _startAtRows; i++)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                await recordReader.ReadRecordAsync(effectiveCancellationToken);
            }

            string? headerLine = null;
            if (_headerFile != null)
            {
                if (System.IO.File.Exists(_headerFile))
                {
                    headerLine = (await System.IO.File.ReadAllTextAsync(_headerFile, effectiveCancellationToken)).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }
                else
                {
                    headerLine = _headerFile;
                }

                if (_hasHeader) await recordReader.ReadRecordAsync(effectiveCancellationToken);
            }
            else
            {
                headerLine = await recordReader.ReadRecordAsync(effectiveCancellationToken);
            }

            if (string.IsNullOrWhiteSpace(headerLine))
                yield break;

            var headers = _fixedColumns != null ? SplitFixedWidthLine(headerLine) : SplitLine(headerLine);
            var actualHeaders = new List<string>();
            var sourceMapping = new List<int>();
            var sourceColumnCount = headers.Length;

            if (_templateSchema != null && (_mapByHeaderName || _ignoreExtraColumns || _nullMissingColumns || _strictSchema))
            {
                var expectedCols = _templateSchema.Select(c => c.ColumnName).ToList();
                var csvCols = _hasHeader
                    ? headers.Select(h => h.Trim()).ToList()
                    : Enumerable.Range(1, headers.Length).Select(i => $"Col{i}").ToList();
                sourceColumnCount = csvCols.Count;

                if (_mapByHeaderName && _hasHeader)
                {
                    EnsureUniqueHeaders(csvCols, "Flat file");

                    var usedCsvIndices = new HashSet<int>();
                    for (int i = 0; i < expectedCols.Count; i++)
                    {
                        var expectedName = expectedCols[i];
                        int csvIndex = csvCols.FindIndex(h => h.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
                        if (csvIndex >= 0)
                        {
                            actualHeaders.Add(expectedCols[i]);
                            sourceMapping.Add(csvIndex);
                            usedCsvIndices.Add(csvIndex);
                        }
                        else
                        {
                            if (_nullMissingColumns)
                            {
                                actualHeaders.Add(expectedCols[i]);
                                sourceMapping.Add(-1);
                            }
                            else if (_strictSchema)
                            {
                                throw new ExecutionException($"Required column '{expectedName}' is missing from the flat file header.");
                            }
                        }
                    }

                    for (int j = 0; j < csvCols.Count; j++)
                    {
                        if (!usedCsvIndices.Contains(j))
                        {
                            if (!_ignoreExtraColumns)
                            {
                                if (_strictSchema)
                                {
                                    throw new ExecutionException($"Flat file contains extra column '{csvCols[j]}' that is not in the expected template schema.");
                                }
                                else
                                {
                                    actualHeaders.Add(csvCols[j]);
                                    sourceMapping.Add(j);
                                }
                            }
                        }
                    }
                }
                else
                {
                    int maxCols = Math.Max(expectedCols.Count, csvCols.Count);
                    for (int i = 0; i < maxCols; i++)
                    {
                        if (i < expectedCols.Count && i < csvCols.Count)
                        {
                            actualHeaders.Add(expectedCols[i]);
                            sourceMapping.Add(i);
                        }
                        else if (i < expectedCols.Count)
                        {
                            if (_nullMissingColumns)
                            {
                                actualHeaders.Add(expectedCols[i]);
                                sourceMapping.Add(-1);
                            }
                            else if (_strictSchema)
                            {
                                throw new ExecutionException($"Flat file contains fewer columns ({csvCols.Count}) than expected ({expectedCols.Count}).");
                            }
                        }
                        else
                        {
                            if (!_ignoreExtraColumns)
                            {
                                if (_strictSchema)
                                {
                                    throw new ExecutionException($"Flat file contains more columns ({csvCols.Count}) than expected ({expectedCols.Count}).");
                                }
                                else
                                {
                                    actualHeaders.Add(csvCols[i]);
                                    sourceMapping.Add(i);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (_hasHeader)
                {
                    actualHeaders = headers.Select(h => h.Trim()).ToList();
                }
                else
                {
                    actualHeaders = _fixedColumns != null
                        ? _fixedColumns.Select(c => c.Name).ToList()
                        : Enumerable.Range(1, headers.Length).Select(i => $"Col{i}").ToList();
                }
                sourceMapping = Enumerable.Range(0, actualHeaders.Count).ToList();
            }

            var currentBatch = new DataTable();
            currentBatch.SetColumns(actualHeaders);
            var ignoredExtraColumnCount = Math.Max(0, sourceColumnCount - sourceMapping.Where(i => i >= 0).Distinct().Count());
            var nullFilledMissingColumnCount = sourceMapping.Count(i => i < 0);
            var emitResilienceDiagnostics = ignoredExtraColumnCount > 0 || nullFilledMissingColumnCount > 0;
            var affectedRowCount = 0;

            if (!_hasHeader)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                await currentBatch.AddRowAsync(CreateRow(headers, currentBatch, sourceMapping));
                affectedRowCount++;
            }

            int totalRowsRead = 0;

            var lineQueue = new Queue<string>();
            string? line;
            while ((line = await recordReader.ReadRecordAsync(effectiveCancellationToken)) != null)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                lineQueue.Enqueue(line);
                if (lineQueue.Count > _endAtRows + (_countAtEndPattern != null ? 1 : 0))
                {
                    var dataLine = lineQueue.Dequeue();
                    await ProcessDataLine(dataLine, currentBatch, actualHeaders, sourceMapping, effectiveCancellationToken);
                    totalRowsRead++;
                    affectedRowCount++;

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

            if (emitResilienceDiagnostics)
            {
                _logger.Info(
                    "FLATFILE schema resilience applied: ignored extra columns={IgnoredExtraColumns}; null-filled missing columns={NullFilledMissingColumns}; affected rows={AffectedRows}.",
                    ignoredExtraColumnCount,
                    nullFilledMissingColumnCount,
                    affectedRowCount);
            }
        }

        private static void EnsureUniqueHeaders(IReadOnlyList<string> headers, string sourceDescription)
        {
            var duplicate = headers
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
            {
                throw new ExecutionException($"{sourceDescription} header contains duplicate column '{duplicate.Key}'. MAP_BY_HEADER_NAME requires unique source headers.");
            }
        }

        private sealed class RecordReader
        {
            private readonly StreamReader _reader;
            private readonly string _rowDelimiter;
            private readonly char[] _buffer = new char[16 * 1024];
            private readonly StringBuilder _record = new();
            private int _position;
            private int _length;

            public RecordReader(StreamReader reader, string rowDelimiter)
            {
                _reader = reader;
                _rowDelimiter = rowDelimiter;
            }

            public async Task<string?> ReadRecordAsync(CancellationToken cancellationToken = default)
            {
                if (_rowDelimiter == "\n" || _rowDelimiter == "\r\n" || _rowDelimiter == "\r")
                    return await _reader.ReadLineAsync(cancellationToken);

                _record.Clear();
                int delimIdx = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_position >= _length)
                    {
                        _length = await _reader.ReadAsync(_buffer.AsMemory(0, _buffer.Length), cancellationToken);
                        _position = 0;
                        if (_length == 0) return _record.Length > 0 ? _record.ToString() : null;
                    }

                    while (_position < _length)
                    {
                        char c = _buffer[_position++];
                        _record.Append(c);

                        if (c == _rowDelimiter[delimIdx])
                        {
                            delimIdx++;
                            if (delimIdx == _rowDelimiter.Length)
                            {
                                _record.Length -= _rowDelimiter.Length;
                                return _record.ToString();
                            }
                        }
                        else
                        {
                            delimIdx = c == _rowDelimiter[0] ? 1 : 0;
                        }
                    }
                }
            }
        }

        private async Task ProcessDataLine(
            string line,
            DataTable batch,
            List<string> actualHeaders,
            List<int> sourceMapping,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = _fixedColumns != null ? SplitFixedWidthLine(line) : SplitLine(line);
            await batch.AddRowAsync(CreateRow(values, batch, sourceMapping));
        }

        private Row CreateRow(string[] values, DataTable batch, List<int> sourceMapping)
        {
            var row = batch.NewRow();
            var columnNames = batch.ColumnNames;
            for (int i = 0; i < columnNames.Count; i++)
            {
                int srcIdx = i < sourceMapping.Count ? sourceMapping[i] : i;
                if (srcIdx < 0 || srcIdx >= values.Length)
                {
                    row[i] = null;
                }
                else
                {
                    string val = _trim ? values[srcIdx].Trim() : values[srcIdx];

                    if (_nullAs != null && (val.Equals(_nullAs, StringComparison.OrdinalIgnoreCase) || (string.IsNullOrEmpty(val) && _nullAs == "")))
                    {
                        row[i] = null;
                    }
                    else
                    {
                        if (_dateFormat != null && DateTime.TryParseExact(val, _dateFormat, _culture, DateTimeStyles.None, out var dt))
                        {
                            row[i] = dt;
                        }
                        else
                        {
                            row[i] = val;
                        }
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            // Security Hardening: local write guardrail + enterprise policy re-check at the boundary.
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);

            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                if (append && System.IO.File.Exists(_filePath) && !_compress && !_encryption.Enabled)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    System.IO.File.Copy(_filePath, tempFile, true);
                }

                using (var writer = new StreamWriter(tempFile, append, _encoding))
                {
                    bool headersWritten = append && System.IO.File.Exists(_filePath) && new System.IO.FileInfo(_filePath).Length > 0;
                    int totalRows = 0;

                    await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        if (!headersWritten && batch.ColumnNames.Count > 0)
                        {
                            if (!string.IsNullOrEmpty(_headerFile) && System.IO.File.Exists(_headerFile))
                            {
                                await writer.WriteLineAsync(
                                    await System.IO.File.ReadAllTextAsync(_headerFile, effectiveCancellationToken).ConfigureAwait(false))
                                    .ConfigureAwait(false);
                            }
                            else if (_hasHeader)
                            {
                                string headerLine;
                                if (_fixedColumns != null)
                                {
                                    var sb = new System.Text.StringBuilder();
                                    foreach (var col in _fixedColumns)
                                    {
                                        var name = col.Name;
                                        sb.Append(name.Length >= col.Length
                                            ? name[..col.Length]
                                            : name.PadRight(col.Length));
                                    }
                                    headerLine = sb.ToString();
                                }
                                else
                                {
                                    headerLine = string.Join(_delimiter.ToString(), batch.ColumnNames.Select(n => FormatField(n)));
                                }
                                await writer.WriteLineAsync(headerLine.AsMemory(), effectiveCancellationToken).ConfigureAwait(false);
                            }
                            headersWritten = true;
                        }

                        var truncateEnabled = IsTruncateStringEnabled();
                        var skipErrorEnabled = IsSkipErrorEnabled();
                        var errors = new List<string>();
                        int rowNum = 0;

                        foreach (var row in batch.Rows)
                        {
                            effectiveCancellationToken.ThrowIfCancellationRequested();
                            rowNum++;
                            string line;
                            if (_fixedColumns != null)
                            {
                                // Fixed-width: pad/truncate each field to its declared column width, no delimiter
                                var sb = new System.Text.StringBuilder();
                                foreach (var col in _fixedColumns)
                                {
                                    var raw = row[col.Name]?.ToString() ?? "";

                                    // For integer columns declared as INT(N): overflow is defined by
                                    // digit count (sign excluded) vs DeclaredDigits, giving a
                                    // symmetric range of -(10^N - 1) to (10^N - 1).
                                    // For all other column types: compare raw string length to the
                                    // physical slot width as before.
                                    bool isOverflow = col.DeclaredDigits.HasValue
                                        ? (raw.TrimStart('-').Length > col.DeclaredDigits.Value)
                                        : (raw.Length > col.Length);

                                    if (isOverflow)
                                    {
                                        if (truncateEnabled)
                                        {
                                            // Truncate to the physical slot (preserves sign if present)
                                            raw = raw.Substring(0, col.Length);
                                        }
                                        else
                                        {
                                            string errMsg;
                                            if (col.DeclaredDigits.HasValue)
                                            {
                                                long maxVal = (long)Math.Pow(10, col.DeclaredDigits.Value) - 1;
                                                errMsg = $"Row {rowNum}: Column '{col.Name}' value '{raw}' exceeds the declared INT({col.DeclaredDigits.Value}) field width " +
                                                         $"(max {col.DeclaredDigits.Value} digits, range -{maxVal} to {maxVal})";
                                            }
                                            else
                                            {
                                                errMsg = $"Row {rowNum}: Column '{col.Name}' is trying to insert a string with length {raw.Length} into a {col.Length} character column (Value: '{raw}')";
                                            }
                                            if (skipErrorEnabled)
                                            {
                                                raw = ""; // skip that column (pad with spaces)
                                            }
                                            else
                                            {
                                                errors.Add(errMsg);
                                            }
                                        }
                                    }
                                    sb.Append(raw.Length >= col.Length
                                        ? raw[..col.Length]
                                        : raw.PadRight(col.Length));
                                }
                                line = sb.ToString();
                            }
                            else
                            {
                                var values = new List<string>();
                                foreach (var col in batch.ColumnNames)
                                    values.Add(FormatField(row[col]?.ToString() ?? ""));
                                line = string.Join(_delimiter.ToString(), values);
                            }
                            await writer.WriteLineAsync(line.AsMemory(), effectiveCancellationToken).ConfigureAwait(false);
                            totalRows++;
                        }

                        if (errors.Count > 0)
                        {
                            throw new ExecutionException("Fixed-width file write validation failed:\n" + string.Join("\n", errors));
                        }
                    }

                    if (!string.IsNullOrEmpty(_countAtEndPattern))
                    {
                        await writer.WriteLineAsync(
                            _countAtEndPattern.Replace("COUNT", totalRows.ToString()).AsMemory(),
                            effectiveCancellationToken).ConfigureAwait(false);
                    }
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
                    System.IO.File.Move(tempFile, _filePath, true);
                }

                if (zippedTemp != null && System.IO.File.Exists(zippedTemp))
                {
                    try { System.IO.File.Delete(zippedTemp); } catch { /* best effort */ }
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile);
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            ValidateFileAccess();

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
                    using var stream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".csv");
                    using var reader = new StreamReader(stream, _encoding);
                    for (int i = 0; i < _startAtRows; i++) await reader.ReadLineAsync();
                    headerLine = await reader.ReadLineAsync();
                }

                if (string.IsNullOrWhiteSpace(headerLine)) return Enumerable.Empty<string>();
                var headers = SplitLine(headerLine);
                if (_hasHeader) return headers.Select(h => h.Trim());
                return headers.Select((h, i) => $"Col{i + 1}");
            }
            catch (Exception ex) { _logger.Debug("[FlatFileDataSource.GetColumnsAsync] Failed to read headers from '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
        }

        private void ValidateFileAccess()
        {
            if (_hasValidatedAccess) return;
            _hasValidatedAccess = true;
            if (_context != null)
            {
                CryptoUtils.ValidateFileAccess(_filePath, _options, _context);
            }
        }

        private string PrepareReadPath(List<string> tempFiles, string extension)
        {
            ValidateFileAccess();
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
            // Security Hardening: Block truncating script files
            _context?.SecurityService.ValidateWriteAccess(_filePath);

            if (System.IO.File.Exists(_filePath))
            {
                System.IO.File.Delete(_filePath);
            }
            await Task.CompletedTask;
        }

        private bool IsTruncateStringEnabled()
        {
            if (_options != null && _options.TryGetValue("TRUNCATE_STRING", out var tsStr))
            {
                return tsStr.Equals("ON", StringComparison.OrdinalIgnoreCase) || tsStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            }
            return _context?.TruncateString ?? false;
        }

        private bool IsSkipErrorEnabled()
        {
            if (_options != null && _options.TryGetValue("SKIP_ERROR", out var seStr))
            {
                return seStr.Equals("ON", StringComparison.OrdinalIgnoreCase) || seStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            }
            return _context?.SkipError ?? false;
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
                _logger.Debug("[FlatFile] ExecuteRawSql received unknown SQL: {Sql}. Returning empty result as native pushdown is not supported.", sql);
                yield return new DataTable { ColumnNames = { "Status" }, Rows = { new Row { ["Status"] = "NOT_SUPPORTED" } } };
            }
        }

        public string ConnectionString => _filePath;
        public string Dialect => "FLATFILE";
        public bool SupportsSqlPushdown => false;
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
