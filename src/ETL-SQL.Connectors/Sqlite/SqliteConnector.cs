using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Connectors.Sqlite
{
    public class SqliteConnector : IConnector
    {
        public string Name => "SQLITE";
        public IReadOnlyList<string> Aliases => new[] { "SQLITE3" };
        public bool IsFileBased => true;

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            var ds = new SqliteDataSource(context, connectionString, null, null);
            return await ds.GetVersionAsync();
        }

        public HashSet<string> GetSupportedFunctions() => SqliteSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => SqliteSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => SqliteSyntax.Exclusions;

        public string GetHelp() =>
            "SQLITE Connector: Connects to local or in-memory SQLite databases.\n" +
            "Options:\n" +
            "  DATABASE / PATH: File path to the SQLite database (e.g., C:\\data\\sqlite.db) or ':memory:'.\n" +
            "  PASSWORD: Encryption password (requires host SQLCipher native library).\n" +
            "  TIMEOUT_SECONDS: Connection timeout in seconds.\n" +
            "  TABLE: Pre-selects a default table context.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "DATABASE", Array.Empty<string>() },
            { "PATH", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "TABLE", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? table = null;
            options?.TryGetValue("TABLE", out table);
            return new SqliteDataSource(context, connectionString, table, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            string path = properties.GetValueOrDefault("DATABASE", properties.GetValueOrDefault("PATH", ""));
            if (string.IsNullOrEmpty(path))
            {
                return "Data Source=:memory:";
            }

            var builder = new SqliteConnectionStringBuilder();
            builder.DataSource = path;

            if (properties.TryGetValue("PASSWORD", out var password))
            {
                builder.Password = password;
            }
            if (properties.TryGetValue("TIMEOUT_SECONDS", out var timeoutStr) && int.TryParse(timeoutStr, out var timeout))
            {
                builder.DefaultTimeout = timeout;
            }

            return builder.ConnectionString;
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => null;
    }
}
