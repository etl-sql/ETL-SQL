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
        // SQLite is SQL-capable even though its Data Source may be a file. SqliteDataSource resolves
        // the Data Source component itself so a full provider connection string is never mistaken
        // for a filesystem path by the generic file-connector handler.
        public bool IsFileBased => false;

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            await using var ds = new SqliteDataSource(context, connectionString, null, null);
            return await ds.GetVersionAsync();
        }

        public HashSet<string> GetSupportedFunctions() => SqliteSyntax.GetSupportedFunctions();
        public HashSet<string> GetSupportedKeywords() => SqliteSyntax.GetSupportedKeywords();
        public HashSet<string> GetExcludedKeywords() => SqliteSyntax.Exclusions;

        public string GetHelp() =>
            "SQLITE Connector: Connects to local or in-memory SQLite databases.\n" +
            "Options:\n" +
            "  DATABASE: File path to the SQLite database (e.g., C:\\data\\sqlite.db) or ':memory:'.\n" +
            "  TIMEOUT_SECONDS: Connection timeout in seconds.\n" +
            "  TABLE: Pre-selects a default table context.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "DATABASE", Array.Empty<string>() },
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
            string path = properties.GetValueOrDefault("DATABASE", "");
            if (string.IsNullOrEmpty(path))
            {
                return "Data Source=:memory:";
            }

            var builder = new SqliteConnectionStringBuilder();
            builder.DataSource = path;

            if (properties.TryGetValue("TIMEOUT_SECONDS", out var timeoutStr) && int.TryParse(timeoutStr, out var timeout))
            {
                builder.DefaultTimeout = timeout;
            }

            return builder.ConnectionString;
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null) => null;
    }
}
