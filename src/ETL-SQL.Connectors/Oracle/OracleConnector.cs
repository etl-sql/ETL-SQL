using System;
using System.Collections.Generic;
using Oracle.ManagedDataAccess.Client;
using ETL_SQL.Data;
using ETL_SQL.Common;
using System.Threading.Tasks;
using System.Linq;

namespace ETL_SQL.Connectors.Oracle
{
    /// <summary>
    /// Connector for Oracle Database using the Managed Data Access client.
    /// Supports deep metadata discovery (tables, views, procedures) and PL/SQL-style syntax.
    /// </summary>
    public class OracleConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "ORACLE";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        
        /// <summary>Retrieves the database version from v$instance.</summary>
        public async Task<string> GetVersionAsync(string connectionString)
        {
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT version FROM v$instance", conn);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown Oracle Version";
        }
        
        /// <summary>Returns Oracle-specific SQL functions.</summary>
        public HashSet<string> GetSupportedFunctions() => OracleSyntax.GetSupportedFunctions();

        /// <summary>Returns Oracle dialect keyword additions (baseline keywords are in LanguageMetadata).</summary>
        public HashSet<string> GetSupportedKeywords() => OracleSyntax.GetSupportedKeywords();

        /// <summary>Returns baseline keywords not supported in Oracle pushdown queries.</summary>
        public HashSet<string> GetExcludedKeywords() => OracleSyntax.Exclusions;
        
        /// <summary>Returns supported options (none currently required for base connection).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new();

        /// <summary>Returns allowed option values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new();

        /// <summary>Returns a human-readable help string for the Oracle connector.</summary>
        public string GetHelp() => "ORACLE Connector: Used for Oracle Database connections. Supports PL/SQL-style syntax.";
        
        /// <summary>Creates a new Oracle data source instance.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new OracleDataSource(connectionString, table);
        }

        /// <summary>Returns a list of logical tables from the connection source.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString)
        {
            var tables = new List<string>();
            try {
                using var conn = new OracleConnection(connectionString);
                await conn.OpenAsync();
                using var cmd = new OracleCommand("SELECT owner || '.' || table_name FROM all_tables WHERE owner != 'SYS'", conn);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            }
            catch (Exception ex)
            {
                Logger.Verbose($"[OracleConnector.GetTablesAsync] Failed to retrieve tables: {ex.Message}");
            }
            return tables;
        }

        /// <summary>Returns a list of logical views from the connection source.</summary>
        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString)
        {
            var views = new List<string>();
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT owner || '.' || view_name FROM all_views WHERE owner != 'SYS'", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) views.Add(reader.GetString(0));
            return views;
        }

        /// <summary>Returns a list of columns for the specified table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new OracleDataSource(connectionString, tableName);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures/functions from the connection source.</summary>
        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString)
        {
            var procs = new List<string>();
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT object_name FROM all_procedures WHERE object_type = 'PROCEDURE' AND owner != 'SYS' AND owner != 'SYSTEM'", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }
    }
}

