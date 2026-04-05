using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core;

namespace ETL_SQL.Connectors.MockDb
{
    public class MockSqlDataSource : IDatabaseSource
    {
        private readonly string _connectionString;
        private readonly string _dialect; 
        private readonly Dictionary<string, DataTable> _mockTables = new(StringComparer.OrdinalIgnoreCase);
        public string Path => "MOCK";
        public IDataSource WithTable(string tableName) 
        {
            _activeTable = tableName;
            return this;
        }
        private string? _activeTable;

        public string Dialect => _dialect;

        public MockSqlDataSource(string connectionString, string dialect)
        {
            _connectionString = connectionString;
            _dialect = dialect;
            InitializeMockData();
        }

        private void InitializeMockData()
        {
            var users = new DataTable();
            users.SetColumns(new[] { "UserID", "UserName", "Email" });
            users.AddRow(new Row { ["UserID"] = 1, ["UserName"] = "Alice", ["Email"] = "alice@example.com" });
            users.AddRow(new Row { ["UserID"] = 2, ["UserName"] = "Bob", ["Email"] = "bob@example.com" });
            _mockTables["Users"] = users;

            var products = new DataTable();
            products.SetColumns(new[] { "ProductID", "ProductName", "Price" });
            products.AddRow(new Row { ["ProductID"] = 101, ["ProductName"] = "Widget", ["Price"] = 19.99m });
            products.AddRow(new Row { ["ProductID"] = 102, ["ProductName"] = "Gadget", ["Price"] = 29.99m });
            _mockTables["Products"] = products;
            
            var orders = new DataTable();
            orders.SetColumns(new[] { "OrderID", "OrderDate", "TotalAmount" });
            orders.AddRow(new Row { ["OrderID"] = 1, ["OrderDate"] = DateTime.Now, ["TotalAmount"] = 150.0m });
            _mockTables["Orders"] = orders;

            var employees = new DataTable();
            employees.SetColumns(new[] { "ID", "Name", "column1", "column2", "Status", "Active", "first_name", "last_name" });
            employees.AddRow(new Row { ["ID"] = 1, ["Name"] = "Alice Boss", ["column1"] = "Test", ["column2"] = "Initial", ["Status"] = 0, ["Active"] = 1, ["first_name"] = "Alice", ["last_name"] = "Boss" });
            employees.AddRow(new Row { ["ID"] = 2, ["Name"] = "Bob Worker", ["column1"] = "Other", ["column2"] = "Changed", ["Status"] = 1, ["Active"] = 1, ["first_name"] = "Bob", ["last_name"] = "Worker" });
            _mockTables["Employee"] = employees;
            _mockTables["Employee_Log"] = employees.Clone();
            _mockTables["DemoDb.dbo.Employee"] = employees;
            _mockTables["DemoDb.dbo.Employee_Log"] = employees.Clone();

            var depts = new DataTable();
            depts.SetColumns(new[] { "column1", "column2", "column3" });
            depts.AddRow(new Row { ["column1"] = "Test", ["column2"] = "HR", ["column3"] = 100 });
            depts.AddRow(new Row { ["column1"] = "Other", ["column2"] = "IT", ["column3"] = 50 });
            _mockTables["departments"] = depts;
            _mockTables["hr.departments"] = depts;
        }

        public Task<string> GetVersionAsync() => Task.FromResult("Mock SQL Server 2022 v16.0");
        public HashSet<string> GetSupportedFunctions() => new(); 

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) 
        {
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            var enumerator = ReadBatches(1).GetAsyncEnumerator();
            if (await enumerator.MoveNextAsync())
            {
                return enumerator.Current.ColumnNames;
            }
            return new List<string> { "ID", "Name" };
        }
        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) 
        {
             string processedSql = sql;
             if (parameters != null && parameters.Any())
             {
                 processedSql = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(sql);
                 var dt = new DataTable { ColumnNames = { "ParameterValue", "ProcessedSql" } };
                 foreach (var p in parameters) dt.AddRow(new Row { ["ParameterValue"] = p, ["ProcessedSql"] = processedSql });
                 yield return dt;
                 yield break;
             }
             // More robust normalized SQL for simple mock filtering
             var normSql = sql.Replace("[", "").Replace("]", "").Replace("\r", " ").Replace("\n", " ");
             DataTable? source = null;
             
             if (normSql.Contains("Users", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Users", out source);
             else if (normSql.Contains("Products", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Products", out source);
             else if (normSql.Contains("Orders", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Orders", out source);
             else if (normSql.Contains("Employee", StringComparison.OrdinalIgnoreCase)) _mockTables.TryGetValue("Employee", out source);

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
                               // Take part after last space (if any, e.g. "u.Name as UserName" -> "UserName")
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
                                // Look for column name specifically, stripping any prefix if the row key is just the field name
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
                            filtered.AddRow(newRow);
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
        public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(_mockTables.Keys.Where(k => !k.Contains(".")).AsEnumerable());
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            if (_mockTables.TryGetValue(tableName, out var dt)) 
            {
                return Task.FromResult((IEnumerable<string>)dt.ColumnNames);
            }
            return Task.FromResult(Enumerable.Empty<string>());
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

