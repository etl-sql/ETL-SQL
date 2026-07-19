using System;
using System.Collections.Generic;
using System.Data; // For DataSet
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ExcelDataReader;
using MiniExcelLibs;

namespace ETL_SQL.Connectors.Excel
{
    public class ExcelDataSource : IDataSource
    {
        private readonly string _filePath;
        private readonly string? _sheetName;
        private readonly bool _hasHeader;
        private readonly bool _compress;
        private readonly EncryptionOptions _encryption;
        private readonly string? _range;
        private readonly Dictionary<string, string>? _options;
        private readonly IEnumerable<ColumnDefinition>? _templateSchema;
        private readonly bool _ignoreExtraColumns;
        private readonly bool _nullMissingColumns;
        private readonly bool _mapByHeaderName;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "EXCEL";
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public ExcelDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null, IEnumerable<ColumnDefinition>? templateSchema = null)
        {
            _context = context;
            _logger = context.Logger;
            _templateSchema = templateSchema;

            _options = options;
            _hasHeader = true; // Default

            if (options != null)
            {
                if (options.TryGetValue("SHEET", out var s)) _sheetName = s;
                if (options.TryGetValue("HEADER", out var h)) _hasHeader = h.ToUpperInvariant() == "ON";
                if (options.TryGetValue("RANGE", out var r)) _range = r;
                if (options.TryGetValue("COMPRESS", out var comp)) _compress = comp.ToUpperInvariant() == "ON";
                if (options.TryGetValue("IGNORE_EXTRA_COLUMNS", out var iec)) _ignoreExtraColumns = iec.ToUpperInvariant() == "ON" || iec.ToUpperInvariant() == "TRUE";
                if (options.TryGetValue("NULL_MISSING_COLUMNS", out var nmc)) _nullMissingColumns = nmc.ToUpperInvariant() == "ON" || nmc.ToUpperInvariant() == "TRUE";
                if (options.TryGetValue("MAP_BY_HEADER_NAME", out var mbh)) _mapByHeaderName = mbh.ToUpperInvariant() == "ON" || mbh.ToUpperInvariant() == "TRUE";
            }

            _encryption = new EncryptionOptions(options);

            var resolvedPath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));
            _filePath = FileConnectorPathHelper.CoerceFilePathExtension(resolvedPath, _encryption.Enabled, _compress);

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);

            // Register encoding provider for ExcelDataReader (needed for .net core)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        public IAsyncEnumerable<ETL_SQL.Data.DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<ETL_SQL.Data.DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize, cancellationToken), "Excel", ex => ex is not ExecutionException);

        private async IAsyncEnumerable<ETL_SQL.Data.DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeRead(_context, _filePath);
            if (!System.IO.File.Exists(_filePath)) yield break;

            var baseStream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".xlsx");
            using var stream = await GetSeekableStreamAsync(baseStream, effectiveCancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            using var reader = ExcelReaderFactory.CreateReader(stream);

            // Accepted exception (Rule 2): ExcelDataReader has no async read API.
            // The full sheet is loaded into a DataSet synchronously here. Re-evaluate if
            // ExcelDataReader ever ships an async overload or we switch libraries.
            var result = reader.AsDataSet(new ExcelDataSetConfiguration()
            {
                ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                {
                    UseHeaderRow = false
                }
            });
            effectiveCancellationToken.ThrowIfCancellationRequested();

            System.Data.DataTable? sheet = ResolveSheet(result);

            if (sheet == null) yield break;

            var range = ExcelRange.Parse(_range, sheet.Rows.Count, sheet.Columns.Count);

            int startRow = Math.Min(range.StartRow, sheet.Rows.Count - 1);
            int endRow = Math.Min(range.EndRow, sheet.Rows.Count - 1);
            int startCol = Math.Min(range.StartCol, sheet.Columns.Count - 1);
            int endCol = Math.Min(range.EndCol, sheet.Columns.Count - 1);

            if (startRow < 0 || startRow > endRow || startCol < 0 || startCol > endCol) yield break;

            var excelCols = new List<string>();
            int dataStartRow = startRow;

            if (_hasHeader && startRow < sheet.Rows.Count)
            {
                var headerRow = sheet.Rows[startRow];
                for (int c = startCol; c <= endCol; c++)
                {
                    excelCols.Add(headerRow[c]?.ToString()?.Trim() is string s && !string.IsNullOrEmpty(s) ? s : $"Column{c - startCol + 1}");
                }
                dataStartRow++;
            }
            else
            {
                for (int c = startCol; c <= endCol; c++)
                {
                    excelCols.Add($"Column{c - startCol + 1}");
                }
            }

            var actualHeaders = new List<string>();
            var sourceMapping = new List<int>();
            var sourceColumnCount = excelCols.Count;

            bool strictSchema = false;
            if (_options != null && _options.TryGetValue("STRICT_SCHEMA", out var ss))
            {
                strictSchema = ss.ToUpperInvariant() == "ON" || ss.ToUpperInvariant() == "TRUE";
            }

            if (_templateSchema != null && (_mapByHeaderName || _ignoreExtraColumns || _nullMissingColumns || strictSchema))
            {
                var expectedCols = _templateSchema.Select(c => c.ColumnName).ToList();

                if (_mapByHeaderName && _hasHeader)
                {
                    EnsureUniqueHeaders(excelCols, "Excel sheet");

                    var usedIndices = new HashSet<int>();
                    for (int i = 0; i < expectedCols.Count; i++)
                    {
                        var expectedName = expectedCols[i];
                        int idx = excelCols.FindIndex(h => h.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            actualHeaders.Add(expectedCols[i]);
                            sourceMapping.Add(idx);
                            usedIndices.Add(idx);
                        }
                        else
                        {
                            if (_nullMissingColumns)
                            {
                                actualHeaders.Add(expectedCols[i]);
                                sourceMapping.Add(-1);
                            }
                            else if (strictSchema)
                            {
                                throw new ExecutionException($"Required column '{expectedName}' is missing from the Excel sheet header.");
                            }
                        }
                    }

                    for (int j = 0; j < excelCols.Count; j++)
                    {
                        if (!usedIndices.Contains(j))
                        {
                            if (!_ignoreExtraColumns)
                            {
                                if (strictSchema)
                                {
                                    throw new ExecutionException($"Excel sheet contains extra column '{excelCols[j]}' that is not in the expected template schema.");
                                }
                                else
                                {
                                    actualHeaders.Add(excelCols[j]);
                                    sourceMapping.Add(j);
                                }
                            }
                        }
                    }
                }
                else
                {
                    int maxCols = Math.Max(expectedCols.Count, excelCols.Count);
                    for (int i = 0; i < maxCols; i++)
                    {
                        if (i < expectedCols.Count && i < excelCols.Count)
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
                            else if (strictSchema)
                            {
                                throw new ExecutionException($"Excel sheet contains fewer columns ({excelCols.Count}) than expected ({expectedCols.Count}).");
                            }
                        }
                        else
                        {
                            if (!_ignoreExtraColumns)
                            {
                                if (strictSchema)
                                {
                                    throw new ExecutionException($"Excel sheet contains more columns ({excelCols.Count}) than expected ({expectedCols.Count}).");
                                }
                                else
                                {
                                    actualHeaders.Add(excelCols[i]);
                                    sourceMapping.Add(i);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                actualHeaders = excelCols;
                sourceMapping = Enumerable.Range(0, excelCols.Count).ToList();
            }

            var etlBatch = new ETL_SQL.Data.DataTable();
            etlBatch.SetColumns(actualHeaders);
            var ignoredExtraColumnCount = Math.Max(0, sourceColumnCount - sourceMapping.Where(i => i >= 0).Distinct().Count());
            var nullFilledMissingColumnCount = sourceMapping.Count(i => i < 0);
            var emitResilienceDiagnostics = ignoredExtraColumnCount > 0 || nullFilledMissingColumnCount > 0;
            var affectedRowCount = 0;

            for (int r = dataStartRow; r <= endRow; r++)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                var row = sheet.Rows[r];
                var etlRow = etlBatch.NewRow();
                for (int i = 0; i < actualHeaders.Count; i++)
                {
                    string colName = actualHeaders[i];
                    int excelColIndex = i < sourceMapping.Count ? sourceMapping[i] : -1;
                    if (excelColIndex >= 0 && (startCol + excelColIndex) < row.ItemArray.Length)
                    {
                        int sheetColIdx = startCol + excelColIndex;
                        etlRow[colName] = row[sheetColIdx] == DBNull.Value ? null : row[sheetColIdx];
                    }
                    else
                    {
                        etlRow[colName] = null;
                    }
                }
                await etlBatch.AddRowAsync(etlRow);
                affectedRowCount++;

                if (etlBatch.Rows.Count >= batchSize)
                {
                    yield return etlBatch;
                    etlBatch = new ETL_SQL.Data.DataTable();
                    etlBatch.SetColumns(actualHeaders);
                }
            }

            if (etlBatch.Rows.Count > 0)
                yield return etlBatch;

            if (emitResilienceDiagnostics)
            {
                _logger.Info(
                    "EXCEL schema resilience applied: ignored extra columns={IgnoredExtraColumns}; null-filled missing columns={NullFilledMissingColumns}; affected rows={AffectedRows}.",
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

        public Task WriteBatches(IAsyncEnumerable<ETL_SQL.Data.DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(
            IAsyncEnumerable<ETL_SQL.Data.DataTable> batches,
            bool append,
            CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            ETL_SQL.Core.Common.FileConnectorPathHelper.AuthorizeWrite(_context, _filePath);

            var existingRows = new List<Dictionary<string, object?>>();
            var existingColumns = new List<string>();

            if (append && System.IO.File.Exists(_filePath))
            {
                try
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    var baseStream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".xlsx");
                    using (var stream = GetSeekableStream(baseStream))
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                        {
                            ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = false }
                        });
                        effectiveCancellationToken.ThrowIfCancellationRequested();

                        // Match the sheet we would have written to (the writer sanitizes the name);
                        // ResolveSheet tries the raw then sanitized name so append preserves rows.
                        System.Data.DataTable? sheet = ResolveSheet(result);

                        if (sheet != null)
                        {
                            var range = ExcelRange.Parse(_range, sheet.Rows.Count, sheet.Columns.Count);
                            int startRow = Math.Min(range.StartRow, sheet.Rows.Count - 1);
                            int endRow = Math.Min(range.EndRow, sheet.Rows.Count - 1);
                            int startCol = Math.Min(range.StartCol, sheet.Columns.Count - 1);
                            int endCol = Math.Min(range.EndCol, sheet.Columns.Count - 1);

                            if (startCol >= 0 && startCol <= endCol && startRow >= 0)
                            {
                                int dataStartRow = startRow;
                                if (_hasHeader && startRow < sheet.Rows.Count)
                                {
                                    var headerRow = sheet.Rows[startRow];
                                    for (int c = startCol; c <= endCol; c++)
                                    {
                                        existingColumns.Add(headerRow[c]?.ToString()?.Trim() is string s && !string.IsNullOrEmpty(s) ? s : $"Column{c - startCol + 1}");
                                    }
                                    dataStartRow++;
                                }
                                else
                                {
                                    for (int c = startCol; c <= endCol; c++)
                                    {
                                        existingColumns.Add($"Column{c - startCol + 1}");
                                    }
                                }

                                for (int r = dataStartRow; r <= endRow; r++)
                                {
                                    effectiveCancellationToken.ThrowIfCancellationRequested();
                                    var row = sheet.Rows[r];
                                    var rowDict = new Dictionary<string, object?>();
                                    for (int c = startCol; c <= endCol; c++)
                                    {
                                        string colName = existingColumns[c - startCol];
                                        rowDict[colName] = row[c] == DBNull.Value ? null : row[c];
                                    }
                                    existingRows.Add(rowDict);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("[ExcelDataSource.WriteBatches] Failed to read existing Excel file for append: {Message}", ex.Message);
                }
            }

            var newRows = new List<Dictionary<string, object?>>();
            var newColumns = new List<string>();

            await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
            {
                if (newColumns.Count == 0 && batch.ColumnNames.Count > 0)
                {
                    newColumns.AddRange(batch.ColumnNames);
                }

                foreach (var row in batch.Rows)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var col in batch.ColumnNames)
                    {
                        rowDict[col] = row[col];
                    }
                    newRows.Add(rowDict);
                }
            }

            var allRows = new List<Dictionary<string, object?>>(existingRows.Count + newRows.Count);
            allRows.AddRange(existingRows);
            allRows.AddRange(newRows);

            var columns = existingColumns.Count > 0 ? existingColumns : newColumns;
            if (columns.Count == 0 && allRows.Count > 0)
            {
                columns = allRows[0].Keys.ToList();
            }

            var materializedRows = new List<Dictionary<string, object?>>();
            foreach (var row in allRows)
            {
                effectiveCancellationToken.ThrowIfCancellationRequested();
                var mapped = new Dictionary<string, object?>(columns.Count);
                foreach (var col in columns)
                {
                    row.TryGetValue(col, out var raw);
                    mapped[col] = CoerceValue(raw);
                }
                materializedRows.Add(mapped);
            }

            string sheetName = string.IsNullOrWhiteSpace(_sheetName) ? "Sheet1" : _sheetName;
            sheetName = SanitizeSheetName(sheetName);

            var book = new Dictionary<string, object>();
            book[sheetName] = materializedRows;

            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    await MiniExcel.SaveAsAsync(stream, book, printHeader: _hasHeader, excelType: ExcelType.XLSX);
                    effectiveCancellationToken.ThrowIfCancellationRequested();
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
                if (System.IO.File.Exists(tempFile))
                {
                    try { System.IO.File.Delete(tempFile); } catch { /* best effort */ }
                }
            }
        }

        private static object? CoerceValue(object? raw)
        {
            if (raw is null || raw == DBNull.Value) return null;
            if (raw is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or DateOnly)
                return raw;
            return NeutralizeFormula(raw.ToString());
        }

        private static string? NeutralizeFormula(string? s) =>
            !string.IsNullOrEmpty(s) && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
                ? "'" + s
                : s;

        // Resolves the sheet this connector targets. When a sheet name is configured, try the raw
        // name first, then the sanitized form the writer would have produced (Excel forbids the
        // sanitized chars in real sheet names, so this only helps round-tripping our own output).
        // Deliberately no first-sheet fallback for a named lookup: that would mask typos on read and
        // merge an unrelated sheet on append. A null/empty name means "the first sheet".
        private System.Data.DataTable? ResolveSheet(System.Data.DataSet result)
        {
            if (!string.IsNullOrEmpty(_sheetName))
                return result.Tables[_sheetName] ?? result.Tables[SanitizeSheetName(_sheetName)];
            return result.Tables.Count > 0 ? result.Tables[0] : null;
        }

        private static string SanitizeSheetName(string? name)
        {
            var s = string.IsNullOrWhiteSpace(name) ? "Sheet" : name!;
            foreach (var c in new[] { '[', ']', ':', '*', '?', '/', '\\' })
                s = s.Replace(c, ' ');
            s = s.Trim();
            if (s.Length == 0) s = "Sheet";
            return s.Length > 31 ? s.Substring(0, 31) : s;
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            try
            {
                var baseStream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".xlsx");
                await using var stream = await GetSeekableStreamAsync(baseStream, effectiveCancellationToken);
                effectiveCancellationToken.ThrowIfCancellationRequested();
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                {
                    ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = false }
                });
                effectiveCancellationToken.ThrowIfCancellationRequested();

                System.Data.DataTable? sheet = ResolveSheet(result);

                if (sheet != null)
                {
                    var range = ExcelRange.Parse(_range, sheet.Rows.Count, sheet.Columns.Count);
                    int startCol = Math.Min(range.StartCol, sheet.Columns.Count - 1);
                    int endCol = Math.Min(range.EndCol, sheet.Columns.Count - 1);
                    int startRow = Math.Min(range.StartRow, sheet.Rows.Count - 1);

                    if (startCol < 0 || startCol > endCol || startRow < 0) return Enumerable.Empty<string>();

                    if (!_hasHeader)
                    {
                        return Enumerable.Range(1, Math.Max(0, endCol - startCol + 1)).Select(i => $"Column{i}");
                    }

                    var headerRow = sheet.Rows[startRow];
                    var names = new List<string>();
                    for (int c = startCol; c <= endCol; c++)
                    {
                        effectiveCancellationToken.ThrowIfCancellationRequested();
                        names.Add(headerRow[c]?.ToString()?.Trim() is string s && !string.IsNullOrEmpty(s) ? s : $"Column{c - startCol + 1}");
                    }
                    return names;
                }
            }
            catch (Exception ex) { _logger.Debug("[ExcelDataSource.GetColumnsAsync] Failed to read columns from '{FilePath}': {Message}", _filePath, ex.Message); }
            return Enumerable.Empty<string>();
        }

        public Task<IEnumerable<string>> GetTablesAsync() => GetTablesAsync(CancellationToken.None);

        public async Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();

            try
            {
                var baseStream = FileConnectorPathHelper.OpenReadStream(_filePath, _encryption, _compress, ".xlsx");
                await using var stream = await GetSeekableStreamAsync(baseStream, effectiveCancellationToken);
                effectiveCancellationToken.ThrowIfCancellationRequested();
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet();
                effectiveCancellationToken.ThrowIfCancellationRequested();
                return result.Tables.Cast<System.Data.DataTable>().Select(t => t.TableName).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
        }

        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
        {
            EffectiveCancellationToken(cancellationToken).ThrowIfCancellationRequested();
            return GetViewsAsync();
        }
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync(tableName, CancellationToken.None);
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken) =>
            string.IsNullOrEmpty(tableName) || string.Equals(tableName, _sheetName, StringComparison.OrdinalIgnoreCase)
                ? GetColumnsAsync(cancellationToken)
                : GetColumnsAsync(cancellationToken);

        public async ValueTask DisposeAsync() => await Task.CompletedTask;

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);

        private string PrepareReadPath(List<string> tempFiles)
        {
            var effectivePath = _filePath;

            if (_encryption.Enabled)
            {
                var decryptedTemp = System.IO.Path.GetTempFileName();
                tempFiles.Add(decryptedTemp);
                _encryption.DecryptFile(_filePath, decryptedTemp);
                effectivePath = decryptedTemp;
            }

            if (_compress &&
                (System.IO.Path.GetExtension(_filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                 || System.IO.Path.GetExtension(effectivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                 || _encryption.Enabled))
            {
                var extractedTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
                tempFiles.Add(extractedTemp);
                using var zip = System.IO.Compression.ZipFile.OpenRead(effectivePath);
                var entry = zip.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name));
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

        private class ExcelRange
        {
            public int StartRow { get; set; }
            public int StartCol { get; set; }
            public int EndRow { get; set; }
            public int EndCol { get; set; }

            public static ExcelRange Parse(string? rangeStr, int maxRows, int maxCols)
            {
                var result = new ExcelRange { StartRow = 0, StartCol = 0, EndRow = maxRows - 1, EndCol = maxCols - 1 };
                if (string.IsNullOrWhiteSpace(rangeStr)) return result;

                var parts = rangeStr.Split(':');
                var start = ParseCell(parts[0]);

                result.StartRow = start.row ?? 0;
                result.StartCol = start.col ?? 0;

                if (parts.Length > 1)
                {
                    var end = ParseCell(parts[1]);
                    result.EndRow = end.row ?? maxRows - 1;
                    result.EndCol = end.col ?? maxCols - 1;
                }

                return result;
            }

            private static (int? row, int? col) ParseCell(string cell)
            {
                int? row = null;
                int? col = null;

                string colStr = "";
                string rowStr = "";

                foreach (char c in cell.ToUpperInvariant())
                {
                    if (char.IsLetter(c)) colStr += c;
                    else if (char.IsDigit(c)) rowStr += c;
                }

                if (!string.IsNullOrEmpty(colStr))
                {
                    int cVal = 0;
                    for (int i = 0; i < colStr.Length; i++)
                    {
                        cVal = cVal * 26 + (colStr[i] - 'A' + 1);
                    }
                    col = cVal - 1;
                }

                if (!string.IsNullOrEmpty(rowStr))
                {
                    row = int.Parse(rowStr) - 1;
                }

                return (row, col);
            }
        }

        private async Task<Stream> GetSeekableStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream.CanSeek) return stream;
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            return new ChainedStream(ms, stream);
        }

        private Stream GetSeekableStream(Stream stream)
        {
            if (stream.CanSeek) return stream;
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return new ChainedStream(ms, stream);
        }
    }
}
