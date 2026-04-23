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
        public bool SupportsSqlPushdown => false;

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
                dt.ColumnNames.AddRange(new[] { "ID", "Name" });
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
             string processedSql = sql;
             if (parameters != null && parameters.Any())
             {
                 processedSql = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(sql);
                 var dt = new DataTable { ColumnNames = { "ParameterValue", "ProcessedSql" } };
                 foreach (var p in parameters) await dt.AddRowAsync(new Row { ["ParameterValue"] = p, ["ProcessedSql"] = processedSql });
                 yield return dt;
                 yield break;
             }
             
             var normSql = sql.Replace("[", "").Replace("]", "").Replace("\r", " ").Replace("\n", " ");
             DataTable? source = null;
             
             if (normSql.Contains("Users", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Users", out source);
             else if (normSql.Contains("Products", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Products", out source);
             else if (normSql.Contains("Sales", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Sales", out source);
             else if (normSql.Contains("Orders", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Sales", out source);
             else if (normSql.Contains("Employee", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Employee", out source);
             else if (normSql.Contains("AuditTrail", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("AuditTrail", out source);

             if (source != null)
             {
                 if (normSql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !normSql.Contains("*"))
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
                        
                        foreach (var row in source.Rows)
                        {
                            var newRow = new Row();
                            foreach (var colName in columnNames) 
                            {
                                var sourceColName = colName.Split('.').Last();
                                if (row.Columns.ContainsKey(sourceColName)) 
                                {
                                    newRow[colName] = row[sourceColName];
                                }
                                else if (row.Columns.ContainsKey(colName))
                                {
                                    newRow[colName] = row[colName];
                                }
                            }
                            await filtered.AddRowAsync(newRow);
                        }
                        yield return filtered;
                        yield break;
                     }
                 }
                 yield return source;
             }
             else
             {
                 yield return new DataTable { ColumnNames = { "ID", "Name" } };
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
