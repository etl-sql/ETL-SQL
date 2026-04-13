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
    /// </summary>
    public class OracleConnector : IConnector
    {
        public string Name => "ORACLE";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        
        public async Task<string> GetVersionAsync(string connectionString, ILogger? logger = null)
        {
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT version FROM v$instance", conn);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Unknown Oracle Version";
        }
        
        public HashSet<string> GetSupportedFunctions() => OracleSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => OracleSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => OracleSyntax.Exclusions;
        
        public string GetHelp() => 
            "ORACLE Connector: Connects to Oracle Database instances.\n" +
            "Options:\n" +
            "  HOST/PORT/SERVICE_NAME: Connection via Easy Connect.\n" +
            "  TNS_NAME: Connection via TNS alias.\n" +
            "  USER/PASSWORD: Standard credentials.\n" +
            "  POOLING: Set to TRUE to enable connection pooling.\n" +
            "  MIN_POOL_SIZE / MAX_POOL_SIZE: Connection pooling bounds.\n" +
            "  CONNECTION_LIFETIME: Maximum seconds a connection remains in the pool.\n" +
            "  TABLE: Pre-selects a default table context.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HOST", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "SERVICE_NAME", Array.Empty<string>() },
            { "TNS_NAME", Array.Empty<string>() },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "POOLING", new[] { "TRUE", "FALSE" } },
            { "MIN_POOL_SIZE", Array.Empty<string>() },
            { "MAX_POOL_SIZE", Array.Empty<string>() },
            { "CONNECTION_LIFETIME", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new OracleDataSource(connectionString, table, options, logger);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null)
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
                (logger ?? NullLogger.Instance).Debug($"[OracleConnector.GetTablesAsync] Failed to retrieve tables: {ex.Message}");
            }
            return tables;
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null)
        {
            var views = new List<string>();
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT owner || '.' || view_name FROM all_views WHERE owner != 'SYS'", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) views.Add(reader.GetString(0));
            return views;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new OracleDataSource(connectionString, tableName, null, logger);
            return await ds.GetColumnsAsync();
        }

        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null)
        {
            var procs = new List<string>();
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new OracleCommand("SELECT object_name FROM all_procedures WHERE object_type = 'PROCEDURE' AND owner != 'SYS' AND owner != 'SYSTEM'", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            if (options != null && options.TryGetValue("TNS_NAME", out var tns)) return tns;
            
            try
            {
                var builder = new OracleConnectionStringBuilder(connectionString);
                return builder.DataSource;
            }
            catch { return null; }
        }
    }
}
