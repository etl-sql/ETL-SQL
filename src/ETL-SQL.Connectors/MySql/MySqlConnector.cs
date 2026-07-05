using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Data;
using MySqlConnector;

namespace ETL_SQL.Connectors.MySql
{
    /// <summary>
    /// Connector for MySQL and MariaDB databases using the MySqlConnector client.
    /// </summary>
    public class MySqlConnector : IConnector
    {
        public string Name => "MYSQL";
        public IReadOnlyList<string> Aliases => new[] { "MARIADB" };

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            var ds = new MySqlDataSource(context, connectionString, null, null);
            return await ds.GetVersionAsync();
        }

        public HashSet<string> GetSupportedFunctions() => MySqlSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => MySqlSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => MySqlSyntax.Exclusions;

        public string GetHelp() =>
            "MYSQL/MARIADB Connector: Used for MySQL and MariaDB database connections.\n" +
            "Options:\n" +
            "  HOST/SERVER: The target server.\n" +
            "  DATABASE: The target database.\n" +
            "  USER/UID: Standard credential user name.\n" +
            "  PASSWORD/PWD: Standard credential password.\n" +
            "  PORT: Listening port (Default 3306).\n" +
            "  POOLING: Set to TRUE to enable connection pooling.\n" +
            "  MIN_POOL_SIZE / MAX_POOL_SIZE: Connection pooling bounds.\n" +
            "  CONNECTION_IDLE_TIMEOUT: Seconds before idle pooled connections are removed.\n" +
            "  CONNECTION_LIFETIME: Maximum age in seconds for pooled connections.\n" +
            "  SSL_MODE: None, Preferred, Required, VerifyCA, VerifyFull.\n" +
            "  ALLOW_PUBLIC_KEY_RETRIEVAL: Set to TRUE to allow public key retrieval.\n" +
            "  ALLOW_USER_VARIABLES: Set to TRUE to allow user-defined variables in queries.\n" +
            "  TIMEOUT_SECONDS: Command timeout in seconds.\n" +
            "  TABLE: Pre-selects a default table context.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HOST", Array.Empty<string>() },
            { "SERVER", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "USER", Array.Empty<string>() },
            { "UID", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "POOLING", new[] { "TRUE", "FALSE" } },
            { "MIN_POOL_SIZE", Array.Empty<string>() },
            { "MAX_POOL_SIZE", Array.Empty<string>() },
            { "CONNECTION_IDLE_TIMEOUT", Array.Empty<string>() },
            { "CONNECTION_LIFETIME", Array.Empty<string>() },
            { "SSL_MODE", new[] { "None", "Preferred", "Required", "VerifyCA", "VerifyFull" } },
            { "ALLOW_PUBLIC_KEY_RETRIEVAL", new[] { "TRUE", "FALSE" } },
            { "ALLOW_USER_VARIABLES", new[] { "TRUE", "FALSE" } },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new MySqlDataSource(context, connectionString, table, options);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public async Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public async Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");

        public async Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);

            var dbBuilder = new MySqlConnectionStringBuilder(connectionString);
            var schema = dbBuilder.Database;

            try
            {
                var procs = new List<string>();
                await using var conn = new MySqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT ROUTINE_NAME FROM information_schema.routines WHERE ROUTINE_SCHEMA = @schema AND ROUTINE_TYPE = 'PROCEDURE'", conn);
                cmd.Parameters.AddWithValue("@schema", schema);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
                return procs;
            }
            catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
            {
                throw ConnectorExceptionWrapper.Wrap("MySql", ex);
            }
        }

        public string BuildConnectionString(Dictionary<string, string> properties) =>
            ConnectionStringBuilder.Build(Name, properties);

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => GetHostStatic(connectionString, options);

        public static string? GetHostStatic(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            if (options != null && options.TryGetValue("SERVER", out var server)) return server;

            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                return builder.Server;
            }
            catch { return null; }
        }

        public ICatalogMetadataProvider? GetCatalogProvider(string connectionString)
            => new MySqlCatalogProvider(connectionString);
    }
}
