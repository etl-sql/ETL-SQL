using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>SQLite dialect — reproduces the store's original behavior exactly.</summary>
    public sealed class SqliteOrchestratorDialect : IOrchestratorStoreDialect
    {
        private readonly string _connectionString;

        public SqliteOrchestratorDialect(string connectionString) => _connectionString = connectionString;

        public DbConnection CreateConnection() => new SqliteConnection(_connectionString);

        // SQLite's COLLATE NOCASE is built in — nothing to create.
        public string CollationDdl => string.Empty;

        public string SchemaInitializationLockSql => string.Empty;

        public string SchemaInitializationUnlockSql => string.Empty;

        public string AutoIncrementPrimaryKey => "INTEGER PRIMARY KEY AUTOINCREMENT";

        public async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string table, CancellationToken ct = default)
        {
            // Case-insensitive so the additive column sweep works identically across providers
            // (PostgreSQL folds unquoted identifiers to lower case).
            var columns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            return columns;
        }

        public string InsertReturningId(string insertWithoutSemicolon, string idColumn) =>
            insertWithoutSemicolon + "; SELECT last_insert_rowid();";
    }
}
