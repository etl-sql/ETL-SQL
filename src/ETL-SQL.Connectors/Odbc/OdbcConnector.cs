using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.Odbc;
using ETL_SQL.Data;
using ETL_SQL.Common;

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

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new OdbcDataSource(connectionString, null, null, logger);
            return ds.GetVersionAsync();
        }

        public HashSet<string> GetSupportedFunctions() => OdbcSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => OdbcSyntax.GetSupportedKeywords();

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new OdbcDataSource(connectionString, table, options, logger);
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
            { "PORT", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "UID", Array.Empty<string>() },
            { "PWD", Array.Empty<string>() },
            { "CONNECT_TIMEOUT", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public async Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new OdbcDataSource(connectionString, null, null, logger);
            return await ds.GetTablesAsync();
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null)
        {
            var ds = new OdbcDataSource(connectionString, null, null, logger);
            return await ds.GetViewsAsync();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new OdbcDataSource(connectionString, tableName, null, logger);
            return await ds.GetColumnsAsync();
        }

        public async Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null)
        {
            var procs = new List<string>();
            try {
                using var conn = new OdbcConnection(connectionString);
                await Task.Run(() => conn.Open());
                using var schemaTable = conn.GetSchema("Procedures");
                foreach (System.Data.DataRow row in schemaTable.Rows)
                {
                    var proc = row["PROCEDURE_NAME"].ToString();
                    if (!string.IsNullOrEmpty(proc)) procs.Add(proc);
                }
            } catch { /* Suppress errors for schema discovery */ }
            return procs;
        }

        public string GetHelp()
        {
            return @"ODBC Bridge Connector
-----------------------
Enables connectivity to any data source via standard ODBC.

Property Mode:
  CREATE CONNECTION my_odbc ON ODBC() WITH(DSN='MyDSN', UID='user', PWD='pwd');
  
DSN-less Mode:
  CREATE CONNECTION my_sqllite ON ODBC() WITH(DRIVER='{SQLite3 ODBC Driver}', DATABASE='mydb.db');

Supported Options:
  DSN              - Pre-configured Data Source Name.
  DRIVER           - Name of driver in {}.
  SERVER           - Hostname or IP address.
  PORT             - Listening port.
  DATABASE         - Database name or file path.
  UID              - User profile name.
  PWD              - Login password.
  CONNECT_TIMEOUT  - Time (sec) to wait for connection.
  TABLE            - Default table for reading.";
        }
    }
}
