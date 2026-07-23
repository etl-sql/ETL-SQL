using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.MockDb
{
    public class MockDbConnector : IConnector
    {
        private static readonly IMockDataSeeder SchemaSeeder = new MockDataSeeder();

        public string Name => "MOCKDB";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Mock SQL Server 2022 v16.0");

        public HashSet<string> GetSupportedFunctions() => MockDbSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => MockDbSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => MockDbSyntax.Exclusions;

        public Dictionary<string, string[]> GetSupportedOptions() => new();
        public Dictionary<string, string[]> GetOptionValues() => new();

        public string GetHelp() => "Mock DB Connector: Used for testing database interactions without a real server.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
            => new MockSqlDataSource(context, connectionString, "MockDB", options, new MockDataSeeder());

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult(SchemaSeeder.GetDeclaredSchema().Keys.Where(k => !k.Contains(".")).AsEnumerable());

        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) =>
            Task.FromResult(SchemaSeeder.GetDeclaredSchema().TryGetValue(tableName, out var columns)
                ? columns.Select(c => c.ColumnName)
                : Enumerable.Empty<string>());

        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
    }
}
