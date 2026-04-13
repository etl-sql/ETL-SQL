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
        
        public string GetHelp() => 
            "MSSQL Connector: Connects to Microsoft SQL Server.\n" +
            "Options:\n" +
            "  SERVER: The target server.\n" +
            "  DATABASE: The target database.\n" +
            "  USER/PASS: Standard credentials.\n" +
            "  TRUSTED_CONNECTION: Set to TRUE for Windows Auth.\n" +
            "  ENCRYPT / USE_SSL: Set to TRUE for encrypted transit.\n" +
            "  TRUST_SERVER_CERTIFICATE: Set to TRUE to skip cert validation.\n" +
            "  APPLICATION_INTENT: READONLY or READWRITE.\n" +
            "  MULTI_SUBNET_FAILOVER: Set to TRUE for high-availability clusters.\n" +
            "  MIN_POOL_SIZE / MAX_POOL_SIZE: Connection pooling bounds.\n" +
            "  POOL_LIFETIME: Maximum duration (seconds) a connection remains in the pool.\n" +
            "  CONNECT_TIMEOUT: Maximum seconds to wait for a connection.\n" +
            "  TABLE: Pre-selects a default table context.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "SERVER", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "TRUSTED_CONNECTION", new[] { "TRUE", "FALSE" } },
            { "ENCRYPT", new[] { "TRUE", "FALSE" } },
            { "USE_SSL", new[] { "TRUE", "FALSE" } },
            { "TRUST_SERVER_CERTIFICATE", new[] { "TRUE", "FALSE" } },
            { "APPLICATION_INTENT", new[] { "READONLY", "READWRITE" } },
            { "MULTI_SUBNET_FAILOVER", new[] { "TRUE", "FALSE" } },
            { "MIN_POOL_SIZE", Array.Empty<string>() },
            { "MAX_POOL_SIZE", Array.Empty<string>() },
            { "POOL_LIFETIME", Array.Empty<string>() },
            { "CONNECT_TIMEOUT", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

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

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("SERVER", out var server)) return server;
            
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.DataSource;
            }
            catch { return null; }
        }
    }
}
