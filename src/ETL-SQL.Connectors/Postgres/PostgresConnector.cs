using System;
using System.Collections.Generic;
using Npgsql;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Postgres
{
    /// <summary>
    /// Connector for PostgreSQL databases using the Npgsql client.
    /// </summary>
    public class PostgresConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "POSTGRES";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        
        /// <summary>Retrieves the database version information.</summary>
        public async Task<string> GetVersionAsync(string connectionString)
        {
            var ds = new PostgresDataSource(connectionString);
            return await ds.GetVersionAsync();
        }
        
        /// <summary>Returns PostgreSQL-specific SQL functions.</summary>
        public HashSet<string> GetSupportedFunctions() => PostgresSyntax.GetSupportedFunctions();

        /// <summary>Returns PostgreSQL-specific SQL keywords.</summary>
        public HashSet<string> GetSupportedKeywords() => PostgresSyntax.GetSupportedKeywords();

        /// <summary>Returns baseline keywords not supported in PostgreSQL pushdown queries.</summary>
        public HashSet<string> GetExcludedKeywords() => PostgresSyntax.Exclusions;
        
        /// <summary>Returns supported options (none currently required for base connection).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new();

        /// <summary>Returns allowed option values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new();

        /// <summary>Returns a human-readable help string for the Postgres connector.</summary>
        public string GetHelp() => "POSTGRES Connector: Used for PostgreSQL database connections.";
        
        /// <summary>Creates a new PostgreSQL data source instance.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new PostgresDataSource(connectionString, table);
        }

        /// <summary>Returns a list of logical tables from the connection source.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString)
        {
            var ds = new PostgresDataSource(connectionString);
            return await ds.GetTablesAsync();
        }

        /// <summary>Returns a list of logical views from the connection source.</summary>
        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString)
        {
            var ds = new PostgresDataSource(connectionString);
            return await ds.GetViewsAsync();
        }

        /// <summary>Returns a list of columns for the specified table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new PostgresDataSource(connectionString, tableName);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures/functions from the connection source.</summary>
        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString)
        {
            var procs = new List<string>();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT routine_name FROM information_schema.routines WHERE routine_schema = 'public' AND routine_type = 'PROCEDURE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }
    }
}

