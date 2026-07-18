using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Data;
using Microsoft.Data.SqlClient;

namespace ETL_SQL.Connectors.SqlServer
{
    /// <summary>
    /// Connector for Microsoft SQL Server databases using Microsoft.Data.SqlClient.
    /// </summary>
    public class SqlServerConnector : IConnector, IConnectionDiagnosticAuthProbe
    {
        public string Name => "MSSQL";
        public IReadOnlyList<string> Aliases => new[] { "SQLSERVER" };

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            var ds = new SqlServerDataSource(context, connectionString, null, null);
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
            "  POOLING: Set to TRUE or FALSE to control provider pooling.\n" +
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
            { "POOLING", new[] { "TRUE", "FALSE" } },
            { "MIN_POOL_SIZE", Array.Empty<string>() },
            { "MAX_POOL_SIZE", Array.Empty<string>() },
            { "POOL_LIFETIME", Array.Empty<string>() },
            { "CONNECT_TIMEOUT", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new SqlServerDataSource(context, connectionString, table, options);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public async Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public async Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");

        public async Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            var procs = new List<string>();
            await using var cmd = new SqlCommand("SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) procs.Add(reader.GetString(0));
            return procs;
        }

        public string BuildConnectionString(Dictionary<string, string> properties) =>
            DatabaseConnectionStringBuilder.Build(Name, properties);

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => GetHostStatic(connectionString, options);

        public static string? GetHostStatic(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("SERVER", out var server)) return server;

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.DataSource;
            }
            catch { return null; }
        }

        public ETL_SQL.Data.ICatalogMetadataProvider? GetCatalogProvider(string connectionString)
            => new SqlServerCatalogProvider(connectionString);

        public async Task<IReadOnlyList<DiagnosticStep>> DiagnoseAuthenticationAsync(
            ConnectionDiagnosticAuthContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionString = BuildDiagnosticConnectionString(context.Target, context.Options);
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                return [new DiagnosticStep("AUTH", DiagnosticStatus.Ok, "MSSQL authentication succeeded.")];
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
            {
                return
                [
                    new DiagnosticStep("AUTH", DiagnosticStatus.Failed,
                        "MSSQL authentication failed.",
                        "Verify SERVER, DATABASE, USER/PASSWORD or TRUSTED_CONNECTION, account status, and SQL Server login policy.")
                ];
            }
        }

        private string BuildDiagnosticConnectionString(string target, IReadOnlyDictionary<string, string>? options)
        {
            if (options is { Count: > 0 } && (options.ContainsKey("SERVER") || options.ContainsKey("DATABASE") || options.ContainsKey("USER")))
                return BuildConnectionString(new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase));
            return target;
        }
    }
}
