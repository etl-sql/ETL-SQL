using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Data;
using Microsoft.Data.Sqlite;

namespace ETL_SQL.Connectors.Sqlite
{
    /// <summary>
    /// Imports column metadata from SQLite's <c>PRAGMA table_info</c>, which already returns the
    /// declared type, NOT NULL flag and primary-key position alongside the name. Without this the
    /// metadata layer falls back to names with a type of <c>ANY</c>, so the schema and session
    /// explorers showed no types for SQLite sources.
    ///
    /// Foreign keys come from <c>PRAGMA foreign_key_list</c>.
    /// </summary>
    public sealed class SqliteCatalogProvider : ICatalogMetadataProvider
    {
        private const string CatalogName = "SQLite catalog";
        private readonly string _connectionString;

        public SqliteCatalogProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        // PRAGMA takes no parameters, so the table name is embedded. Double every embedded quote and
        // wrap in double quotes: the identifier is then a quoted literal and cannot terminate early.
        private static string QuoteIdentifier(string name) => name.Replace("\"", "\"\"");

        public Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
            string schema, string tableName, CancellationToken ct = default)
            => ConnectorExceptionWrapper.RunAsync<IReadOnlyList<CatalogColumn>>(CatalogName, ShouldWrapProviderException, async () =>
            {
                var results = new List<CatalogColumn>();
                if (string.IsNullOrWhiteSpace(tableName)) return results;

                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);

                await using var cmd = conn.CreateCommand();
                // Columns: cid, name, type, notnull, dflt_value, pk
                cmd.CommandText = $"PRAGMA table_info(\"{QuoteIdentifier(tableName)}\")";
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var name = reader.GetString(1);

                    // SQLite is dynamically typed and a column may be declared with no type at all.
                    // Report that honestly as ANY rather than inventing one — a wrong type is worse
                    // than an unknown one.
                    var declaredType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    if (string.IsNullOrWhiteSpace(declaredType)) declaredType = "ANY";

                    var notNull = !reader.IsDBNull(3) && reader.GetInt32(3) != 0;
                    // pk is the 1-based position within the primary key, so 0 means "not part of it".
                    var isPrimaryKey = !reader.IsDBNull(5) && reader.GetInt32(5) != 0;

                    results.Add(new CatalogColumn(
                        name,
                        declaredType.Trim(),
                        IsNullable: !notNull,
                        IsPrimaryKey: isPrimaryKey,
                        Description: null,
                        ExtraProperties: new Dictionary<string, string>()));
                }

                return results;
            });

        public Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(
            string schema, string tableName, CancellationToken ct = default)
            => ConnectorExceptionWrapper.RunAsync<IReadOnlyList<CatalogRelationship>>(CatalogName, ShouldWrapProviderException, async () =>
            {
                var results = new List<CatalogRelationship>();
                if (string.IsNullOrWhiteSpace(tableName)) return results;

                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);

                await using var cmd = conn.CreateCommand();
                // Columns: id, seq, table, from, to, on_update, on_delete, match
                cmd.CommandText = $"PRAGMA foreign_key_list(\"{QuoteIdentifier(tableName)}\")";
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var referencedTable = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var fromColumn = reader.IsDBNull(3) ? null : reader.GetString(3);
                    // "to" is null when the reference targets the other table's primary key
                    // implicitly; the column name is not recoverable from this pragma alone.
                    var toColumn = reader.IsDBNull(4) ? null : reader.GetString(4);

                    if (referencedTable is null || fromColumn is null || toColumn is null) continue;
                    results.Add(new CatalogRelationship(fromColumn, referencedTable, toColumn));
                }

                return results;
            });

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is SqliteException or InvalidOperationException;
    }
}
