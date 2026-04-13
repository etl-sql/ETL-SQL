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
        
        public string GetHelp() => 
            "POSTGRES Connector: Used for PostgreSQL database connections.\n" +
            "Options:\n" +
            "  HOST: The target server.\n" +
            "  DATABASE: The target database.\n" +
            "  USER/PASSWORD: Standard credentials.\n" +
            "  PORT: Listening port (Default 5432).\n" +
            "  POOLING: Set to TRUE to enable connection pooling.\n" +
            "  MIN_POOL_SIZE / MAX_POOL_SIZE: Connection pooling bounds.\n" +
            "  CONNECTION_IDLE_LIFETIME: Maximum seconds an idle connection stays in the pool.\n" +
            "  SSL_MODE: Disable, Prefer, Require, etc.\n" +
            "  TRUST_SERVER_CERTIFICATE: Set to TRUE to skip cert validation.\n" +
            "  TABLE: Pre-selects a default table context.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HOST", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "POOLING", new[] { "TRUE", "FALSE" } },
            { "MIN_POOL_SIZE", Array.Empty<string>() },
            { "MAX_POOL_SIZE", Array.Empty<string>() },
            { "CONNECTION_IDLE_LIFETIME", Array.Empty<string>() },
            { "SSL_MODE", new[] { "Disable", "Prefer", "Require", "VerifyCA", "VerifyFull" } },
            { "TRUST_SERVER_CERTIFICATE", new[] { "TRUE", "FALSE" } },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

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

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                return builder.Host;
            }
            catch { return null; }
        }
    }
}
