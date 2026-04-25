using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Connectors.MockDb
{
    public class MockSqlDataSource : IDatabaseSource
    {
        private readonly string _connectionString;
        public string ConnectionString => _connectionString;
        private readonly string _dialect; 
        private readonly Dictionary<string, DataTable> _mockTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger _logger;
        private readonly IExecutionContext _context;
        public string Path => "MOCK";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "MOCKDB";
        public IDataSource WithTable(string tableName) 
        {
            _activeTable = tableName;
            return this;
        }
        private string? _activeTable;

        public string Dialect => _dialect;
        public bool SupportsSqlPushdown => true;

        private readonly IMockDataSeeder _seeder;
        private readonly Task _initTask;

        public MockSqlDataSource(IExecutionContext context, string connectionString, string dialect, IMockDataSeeder? seeder = null)
        {
            _context = context;
            _connectionString = connectionString;
            _dialect = dialect;
            _logger = context.Logger;
            _seeder = seeder ?? new MockDataSeeder();
            
            // Correctly capture and await the async seeding task
            _initTask = _seeder.SeedDataAsync(_mockTables, new Random(42));
        }


        private async Task EnsureInitialized()
        {
            if (!_initTask.IsCompleted) await _initTask;
        }

        public Task<string> GetVersionAsync() => Task.FromResult("Mock SQL Server 2022 v16.0");
        public HashSet<string> GetSupportedFunctions() => new(); 

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            await EnsureInitialized();
            if (!string.IsNullOrEmpty(_activeTable) && _mockTables.TryGetValue(_activeTable, out var table))
            {
                yield return table;
            }
            else if (_mockTables.Count > 0)
            {
                yield return _mockTables.Values.First();
            }
            else
            {
                var dt = new DataTable();
                dt.SetColumns(new[] { "ID", "Name" });
                yield return dt;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) 
        {
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            await EnsureInitialized();
            var enumerator = ReadBatches(1).GetAsyncEnumerator();
            if (await enumerator.MoveNextAsync())
            {
                return enumerator.Current.ColumnNames;
            }
            return new List<string> { "ID", "Name" };
        }
        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) 
        {
             await EnsureInitialized();
             var trimmedSql = sql.Trim();
             var normSql = trimmedSql.Replace("[", "").Replace("]", "").Replace("\r", " ").Replace("\n", " ");
             DataTable? source = null;
             foreach (var tableName in _mockTables.Keys)
             {
                 if (normSql.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                 {
                     source = _mockTables[tableName];
                     break;
                 }
             }

             if (source == null && parameters != null && parameters.Any())
             {
                 string processedSql = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(sql);
                 var dt = new DataTable();
                 dt.SetColumns(new[] { "ParameterValue", "ProcessedSql" });
                 foreach (var p in parameters) await dt.AddRowAsync(new Row { ["ParameterValue"] = p, ["ProcessedSql"] = processedSql });
                 yield return dt;
                 yield break;
             }

             if (source != null)
             {
                 if (normSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !normSql.Contains("*"))
                 {
                     var fromIdx = normSql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
                     if (fromIdx > 7)
                     {
                        var colsPart = normSql.Substring(7, fromIdx - 7);
                        var columnNames = colsPart.Split(',')
                           .Select(c => {
                               var trimmed = c.Trim();
                               var lastWord = trimmed.Split(' ').Last();
                               return lastWord.Split('.').Last();
                           })
                           .ToList();

                        var filtered = new DataTable(); 
                        filtered.SetColumns(columnNames);
                        var whereMatch = System.Text.RegularExpressions.Regex.Match(normSql, @"WHERE\s+(?<col>[\w\.]+)\s*=\s*(?<val>[^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        
                        foreach (var row in source.Rows)
                        {
                            bool match = true;
                            if (whereMatch.Success)
                            {
                                var colName = whereMatch.Groups["col"].Value.Split('.').Last();
                                var valStr = whereMatch.Groups["val"].Value.Trim().Trim('\'', '"');
                                
                                // Simplified Mock parameter replacement
                                if (valStr.StartsWith("?") || valStr.StartsWith("@"))
                                {
                                    // Handle ?1 or @pn from parameters
                                    int pIdx = -1;
                                    if (valStr.StartsWith("?"))
                                    {
                                        if (int.TryParse(valStr.Substring(1), out var pNum)) pIdx = pNum - 1;
                                    }
                                    else if (valStr.StartsWith("@p", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (int.TryParse(valStr.Substring(2), out var pNum)) pIdx = pNum;
                                    }

                                    var pList = parameters?.ToList();
                                    if (pList != null && pIdx >= 0 && pIdx < pList.Count)
                                    {
                                       valStr = pList[pIdx]?.ToString() ?? "";
                                    }
                                }

                                if (row[colName]?.ToString() != valStr) match = false;
                            }

                            if (match)
                            {
                                var newRow = new Row();
                                foreach (var colName in columnNames) 
                                {
                                    newRow[colName] = row[colName];
                                }
                                await filtered.AddRowAsync(newRow);
                                if (whereMatch.Success) break; 
                            }
                        }
                        yield return filtered;
                        yield break;
                     }
                 }
                 yield return source;
             }
             else
             {
                 var dt = new DataTable();
                 dt.SetColumns(new[] { "ID", "Name" });
                 yield return dt;
             }
        }
        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            await EnsureInitialized();
            return _mockTables.Keys.Where(k => !k.Contains(".")).AsEnumerable();
        }
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            await EnsureInitialized();
            if (_mockTables.TryGetValue(tableName, out var dt)) 
            {
                return dt.ColumnNames;
            }
            return Enumerable.Empty<string>();
        }
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public async Task TruncateAsync()
        {
            if (!string.IsNullOrEmpty(_activeTable) && _mockTables.TryGetValue(_activeTable, out var table))
            {
                table.Rows.Clear();
            }
            await Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
