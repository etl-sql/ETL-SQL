using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Postgres
{
    /// <summary>
    /// Connector for PostgreSQL databases using the Npgsql client.
    /// </summary>
    public class PostgresConnector : IConnector
    {
        public string Name => "POSTGRES";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        
        public async Task<string> GetVersionAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new PostgresDataSource(connectionString, null, null, logger);
            return await ds.GetVersionAsync();
        }
        
        public HashSet<string> GetSupportedFunctions() => PostgresSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => PostgresSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => PostgresSyntax.Exclusions;
        
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public string GetHelp() => "POSTGRES Connector: Used for PostgreSQL database connections.";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new PostgresDataSource(connectionString, table, options, logger);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new PostgresDataSource(connectionString, null, null, logger);
            return await ds.GetTablesAsync();
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new PostgresDataSource(connectionString, null, null, logger);
            return await ds.GetViewsAsync();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new PostgresDataSource(connectionString, tableName, null, logger);
            return await ds.GetColumnsAsync();
        }

        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null)
        {
            var procs = new List<string>();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT routine_name FROM information_schema.routines WHERE routine_schema = 'public' AND routine_type = 'PROCEDURE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
