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
        
        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) context.SecurityService.ValidateHost(host);

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
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) 
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new OracleDataSource(context, connectionString, table, options);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public async Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public async Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");

        public async Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) context.SecurityService.ValidateHost(host);

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

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => GetHostStatic(connectionString, options);

        public static string? GetHostStatic(string connectionString, Dictionary<string, string>? options = null)
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
