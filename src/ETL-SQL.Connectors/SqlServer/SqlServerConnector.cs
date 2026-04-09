using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Connectors.MockDb;
using System.Threading.Tasks;

namespace ETL_SQL.Connectors.SqlServer
{
    /// <summary>
    /// Connector for Microsoft SQL Server databases using Microsoft.Data.SqlClient.
    /// </summary>
    public class SqlServerConnector : IConnector
    {
        public string Name => "MSSQL";
        public IReadOnlyList<string> Aliases => new[] { "SQLSERVER" };
        
        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new SqlServerDataSource(connectionString, null, null, logger);
            return ds.GetVersionAsync();
        }
        
        public HashSet<string> GetSupportedFunctions() => SqlServerSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => SqlServerSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => SqlServerSyntax.Exclusions;
        
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public string GetHelp() => 
            "MSSQL Connector: Connects to Microsoft SQL Server.\nOptions:\n  TABLE: Pre-selects a default table context.";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new SqlServerDataSource(connectionString, table, options, logger);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new SqlServerDataSource(connectionString, null, null, logger);
            return await ds.GetTablesAsync();
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new SqlServerDataSource(connectionString, null, null, logger);
            return await ds.GetViewsAsync();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new SqlServerDataSource(connectionString, tableName, null, logger);
            return await ds.GetColumnsAsync();
        }

        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null)
        {
            var procs = new List<string>();
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
