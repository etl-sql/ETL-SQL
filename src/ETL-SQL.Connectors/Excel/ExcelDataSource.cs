using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Data; // For DataSet
using ExcelDataReader;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;

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
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;

        public string Path => _filePath;
        public Dictionary<string, string>? Options => _options;
        public IDataSource WithTable(string tableName) => this;
        public string ConnectorType => "EXCEL";
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public ExcelDataSource(IExecutionContext context, string filePath, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _filePath = context.ResolvePath(filePath.Trim('\'', '\"', ' ', '\t', '\r', '\n'));

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_filePath);
            context.SecurityService.ValidateFileType(_filePath, context.AllowUnknownFileTypes);

            _options = options;
            _hasHeader = true; // Default

            if (options != null)
            {
                if (options.TryGetValue("SHEET", out var s)) _sheetName = s;
                if (options.TryGetValue("HEADER", out var h)) _hasHeader = h.ToUpperInvariant() == "ON";
                if (options.TryGetValue("RANGE", out var r)) _range = r;
                if (options.TryGetValue("COMPRESS", out var comp)) _compress = comp.ToUpperInvariant() == "ON";
            }
            
            _encryption = new EncryptionOptions(options);
            
            // Register encoding provider for ExcelDataReader (needed for .net core)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        public async IAsyncEnumerable<ETL_SQL.Data.DataTable> ReadBatches(int batchSize = 10000)
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

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                
                var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                {
                    ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                    {
                        UseHeaderRow = false
                    }
                });

                System.Data.DataTable? sheet = null;
                if (!string.IsNullOrEmpty(_sheetName)) sheet = result.Tables[_sheetName];
                else if (result.Tables.Count > 0) sheet = result.Tables[0];

                if (sheet == null) yield break;

                var range = ExcelRange.Parse(_range, sheet.Rows.Count, sheet.Columns.Count);
                
                int startRow = Math.Min(range.StartRow, sheet.Rows.Count - 1);
                int endRow = Math.Min(range.EndRow, sheet.Rows.Count - 1);
                int startCol = Math.Min(range.StartCol, sheet.Columns.Count - 1);
                int endCol = Math.Min(range.EndCol, sheet.Columns.Count - 1);

                if (startRow < 0 || startRow > endRow || startCol < 0 || startCol > endCol) yield break;

                var columnNames = new List<string>();
                int dataStartRow = startRow;
                
                if (_hasHeader && startRow < sheet.Rows.Count)
                {
                    var headerRow = sheet.Rows[startRow];
                    for (int c = startCol; c <= endCol; c++)
                    {
                        columnNames.Add(headerRow[c]?.ToString()?.Trim() is string s && !string.IsNullOrEmpty(s) ? s : $"Column{c - startCol + 1}");
                    }
                    dataStartRow++;
                }
                else
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        columnNames.Add($"Column{c - startCol + 1}");
                    }
                }

                var etlBatch = new ETL_SQL.Data.DataTable();
                foreach (var name in columnNames) etlBatch.ColumnNames.Add(name);

                for (int r = dataStartRow; r <= endRow; r++)
                {
                    var row = sheet.Rows[r];
                    var etlRow = new Row();
                    for (int c = startCol; c <= endCol; c++)
                    {
                        string colName = columnNames[c - startCol];
                        etlRow[colName] = row[c] == DBNull.Value ? null : row[c];
                    }
                    await etlBatch.AddRowAsync(etlRow);

                    if (etlBatch.Rows.Count >= batchSize)
                    {
                        yield return etlBatch;
                        etlBatch = new ETL_SQL.Data.DataTable();
                        foreach (var name in columnNames) etlBatch.ColumnNames.Add(name);
                    }
                }

                if (etlBatch.Rows.Count > 0) yield return etlBatch;
            }
            finally
            {
                TempFileHelper.SafeDelete(tempFile, _logger);
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<ETL_SQL.Data.DataTable> batches, bool append = false)
        {
            throw new NotSupportedException("Writing to Excel is not currently supported. Use CSV or FLATFILE for output.");
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
                catch (Exception ex) { _logger.Debug("[ExcelDataSource.GetColumnsAsync] Failed to decrypt '{FilePath}': {Message}", _filePath, ex.Message); return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                {
                    ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = false }
                });

                System.Data.DataTable? sheet = null;
                if (!string.IsNullOrEmpty(_sheetName)) sheet = result.Tables[_sheetName];
                else if (result.Tables.Count > 0) sheet = result.Tables[0];

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
                        names.Add(headerRow[c]?.ToString()?.Trim() is string s && !string.IsNullOrEmpty(s) ? s : $"Column{c - startCol + 1}");
                    }
                    return names;
                }
            }
            catch (Exception ex) { _logger.Debug("[ExcelDataSource.GetColumnsAsync] Failed to read columns from '{FilePath}': {Message}", _filePath, ex.Message); }
            finally { TempFileHelper.SafeDelete(tempFile, _logger); }
            return Enumerable.Empty<string>();
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            if (!System.IO.File.Exists(_filePath)) return Enumerable.Empty<string>();
            
            string effectivePath = _filePath;
            string? tempFile = null;

            if (_encryption.Enabled)
            {
                tempFile = System.IO.Path.GetTempFileName();
                try { _encryption.DecryptFile(_filePath, tempFile); effectivePath = tempFile; }
                catch { return Enumerable.Empty<string>(); }
            }

            try
            {
                using var stream = System.IO.File.OpenRead(effectivePath);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet();
                return result.Tables.Cast<System.Data.DataTable>().Select(t => t.TableName).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
            finally { TempFileHelper.SafeDelete(tempFile, _logger); }
        }

        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult<IEnumerable<string>>(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsAsync();

        public async ValueTask DisposeAsync() => await Task.CompletedTask;

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
    }
}
