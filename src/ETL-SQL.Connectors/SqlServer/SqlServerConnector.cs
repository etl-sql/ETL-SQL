using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.Connectors.SqlServer
{
    /// <summary>
    /// Connector for Microsoft SQL Server databases using Microsoft.Data.SqlClient.
    /// Supports T-SQL syntax and discovery of tables, views, and procedures.
    /// </summary>
    public class SqlServerConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "MSSQL";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => new[] { "SQLSERVER" };
        
        /// <summary>Returns a description of the SQL Server version.</summary>
        public string GetVersion(string connectionString) => "Microsoft SQL Server (Mocked)";
        
        /// <summary>Returns SQL Server-specific SQL functions.</summary>
        public HashSet<string> GetSupportedFunctions() => SqlServerSyntax.GetSupportedFunctions();

        /// <summary>Returns SQL Server-specific SQL keywords.</summary>
        public HashSet<string> GetSupportedKeywords() => SqlServerSyntax.GetSupportedKeywords();

        /// <summary>Returns baseline keywords not supported in T-SQL pushdown queries.</summary>
        public HashSet<string> GetExcludedKeywords() => SqlServerSyntax.Exclusions;
        
        /// <summary>Returns supported options (none currently required for base connection).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new();

        /// <summary>Returns allowed option values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new();

        /// <summary>Returns a human-readable help string for the SQL Server connector.</summary>
        public string GetHelp() => "MSSQL Connector: Used for Microsoft SQL Server connections. Supports T-SQL syntax.";
        
        /// <summary>Creates a new SQL Server data source instance.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new SqlServerDataSource(connectionString, table);
        }

        /// <summary>Returns a list of logical tables from the connection source.</summary>
        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString)
        {
            var ds = new SqlServerDataSource(connectionString);
            return await ds.GetTablesAsync();
        }

        /// <summary>Retrieves the SQL Server version information.</summary>
        public async Task<string> GetVersionAsync(string connectionString)
        {
            var ds = new SqlServerDataSource(connectionString);
            return await ds.GetVersionAsync();
        }

        /// <summary>Returns a list of logical views from the connection source.</summary>
        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString)
        {
            var ds = new SqlServerDataSource(connectionString);
            return await ds.GetViewsAsync();
        }

        /// <summary>Returns a list of columns for the specified table.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new SqlServerDataSource(connectionString, tableName);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures from the connection source.</summary>
        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString)
        {
            var procs = new List<string>();
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }
    }
}

