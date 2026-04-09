using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.MockDb
{
    public class MockDbConnector : IConnector
    {
        public string Name => "MOCKDB";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        
        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("Mock SQL Server 2022 v16.0");
        
        public HashSet<string> GetSupportedFunctions() => MockDbSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => MockDbSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => MockDbSyntax.Exclusions;
        
        public Dictionary<string, string[]> GetSupportedOptions() => new();
        public Dictionary<string, string[]> GetOptionValues() => new();
        
        public string GetHelp() => "Mock DB Connector: Used for testing database interactions without a real server.";
        
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
            => new MockSqlDataSource(connectionString, "MockDB", logger);

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new MockSqlDataSource(connectionString, "MockDB", logger);
            return ds.GetTablesAsync();
        }
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new MockSqlDataSource(connectionString, "MockDB", logger);
            return await ds.GetColumnsAsync(tableName);
        }
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
    }
}
