using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Odbc
{
    /// <summary>
    /// Connector for the Universal ODBC Bridge.
    /// Enables connectivity to any data source with a system or user DSN, or a raw driver string.
    /// </summary>
    public class OdbcConnector : IConnector
    {
        public string Name => "ODBC";
        public IReadOnlyList<string> Aliases => new[] { "ODBC_BRIDGE" };

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            var ds = new OdbcDataSource(context, connectionString, null, null);
            return ds.GetVersionAsync();
        }

        public HashSet<string> GetSupportedFunctions() => OdbcSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => OdbcSyntax.GetSupportedKeywords();
        // Accepted exception (Rule 9): returns empty by design — ODBC wraps arbitrary third-party
        // drivers whose SQL dialects vary per DSN. There is no single set of excluded keywords that
        // applies to all targets, so the linter skips dialect-keyword checking for ODBC connections.
        public HashSet<string> GetExcludedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new OdbcDataSource(context, connectionString, table, options);
        }

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return ConnectionStringBuilder.Build("ODBC", properties);
        }

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "DSN", Array.Empty<string>() },
            { "DRIVER", Array.Empty<string>() },
            { "SERVER", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "UID", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");

        public async Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString)
        {
            // Security constraint: validate host before connecting
            var host = GetHost(connectionString);
            if (host != null) context.SecurityService.ValidateHost(host);

            var procs = new List<string>();
            try
            {
                using var conn = new OdbcConnection(connectionString);
                await Task.Run(() => conn.Open());
                using var schemaTable = conn.GetSchema("Procedures");
                foreach (System.Data.DataRow row in schemaTable.Rows)
                {
                    var proc = row["PROCEDURE_NAME"].ToString();
                    if (!string.IsNullOrEmpty(proc)) procs.Add(proc);
                }
            }
            catch { /* Suppress errors for schema discovery */ }
            return procs;
        }

        public string GetHelp()
        {
            return @"ODBC Bridge Connector
-----------------------
Enables connectivity to any data source via standard ODBC.

Property Mode:
  CREATE CONNECTION my_odbc AS ODBC(DSN='MyDSN', UID='user', PWD='pwd');
  
DSN-less Mode:
  CREATE CONNECTION my_sqllite AS ODBC(DRIVER='{SQLite3 ODBC Driver}', DATABASE='mydb.db');

Supported Options:
  DSN              - Pre-configured Data Source Name.
  DRIVER           - Name of driver in {}.
  SERVER           - Hostname or IP address.
  DATABASE         - Database name or file path.
  UID              - User profile name.
  PWD              - Login password.
  TIMEOUT_SECONDS  - Command/query execution timeout in seconds (default 30).
  TABLE            - Default table for reading.";
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => GetHostStatic(connectionString, options);

        public static string? GetHostStatic(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("SERVER", out var server)) return server;

            try
            {
                var builder = new OdbcConnectionStringBuilder(connectionString);
                if (builder.TryGetValue("SERVER", out var s)) return s.ToString();
                if (builder.TryGetValue("Host", out var h)) return h.ToString();
                if (builder.TryGetValue("DSN", out var dsn)) return dsn.ToString(); // DSN is the "host" of the config
                return null;
            }
            catch { return null; }
        }
    }
}
