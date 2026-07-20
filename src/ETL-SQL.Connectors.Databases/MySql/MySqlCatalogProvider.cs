using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Data;
using MySqlConnector;

namespace ETL_SQL.Connectors.MySql
{
    /// <summary>
    /// Imports column metadata from the MySQL / MariaDB system catalog.
    /// Reads <c>information_schema.columns</c> and primary/foreign key constraints.
    /// </summary>
    public sealed class MySqlCatalogProvider : ICatalogMetadataProvider, IViewDefinitionProvider
    {
        private const string CatalogName = "MySql catalog";
        private readonly string _connectionString;

        public MySqlCatalogProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
            string schema, string tableName, CancellationToken ct = default)
            => ConnectorExceptionWrapper.RunAsync<IReadOnlyList<CatalogColumn>>(CatalogName, ShouldWrapProviderException, async () =>
            {
                var results = new List<CatalogColumn>();
                const string sql = @"
SELECT
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.IS_NULLABLE = 'YES'                           AS is_nullable,
    COALESCE(pk.is_primary_key, false)              AS is_primary_key,
    c.COLUMN_COMMENT                                AS description
FROM information_schema.columns c
LEFT JOIN (
    SELECT kcu.COLUMN_NAME, true AS is_primary_key
    FROM information_schema.table_constraints tc
    JOIN information_schema.key_column_usage kcu
        ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
        AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
        AND kcu.TABLE_NAME = tc.TABLE_NAME
    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
      AND tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @table
) pk ON pk.COLUMN_NAME = c.COLUMN_NAME
WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
ORDER BY c.ORDINAL_POSITION;";

                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", tableName);
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
                {
                    results.Add(new CatalogColumn(
                        ColumnName: rdr.GetString(0),
                        DataType: rdr.GetString(1),
                        IsNullable: rdr.GetBoolean(2),
                        IsPrimaryKey: rdr.GetBoolean(3),
                        Description: rdr.IsDBNull(4) ? null : rdr.GetString(4),
                        ExtraProperties: new Dictionary<string, string>()));
                }
                return results;
            });

        public Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(
            string schema, string tableName, CancellationToken ct = default)
            => ConnectorExceptionWrapper.RunAsync<IReadOnlyList<CatalogRelationship>>(CatalogName, ShouldWrapProviderException, async () =>
            {
                var results = new List<CatalogRelationship>();
                const string sql = @"
SELECT
    kcu.COLUMN_NAME                             AS fk_column,
    CONCAT(kcu.REFERENCED_TABLE_SCHEMA, '.', kcu.REFERENCED_TABLE_NAME)  AS referenced_table,
    kcu.REFERENCED_COLUMN_NAME                  AS referenced_column
FROM information_schema.key_column_usage kcu
WHERE kcu.TABLE_SCHEMA = @schema
  AND kcu.TABLE_NAME = @table
  AND kcu.REFERENCED_COLUMN_NAME IS NOT NULL;";

                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", tableName);
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
                {
                    results.Add(new CatalogRelationship(
                        ForeignKeyColumn: rdr.GetString(0),
                        ReferencedTable: rdr.GetString(1),
                        ReferencedColumn: rdr.GetString(2)));
                }
                return results;
            });

        public Task<string?> GetViewDefinitionAsync(string schema, string objectName, CancellationToken ct = default)
            => ConnectorExceptionWrapper.RunAsync<string?>(CatalogName, ShouldWrapProviderException, async () =>
            {
                const string sql = @"
SELECT VIEW_DEFINITION
FROM information_schema.VIEWS
WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name;";

                await using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@name", objectName);
                var result = await cmd.ExecuteScalarAsync(ct);
                return result is string def && !string.IsNullOrWhiteSpace(def) ? def : null;
            });

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is MySqlException or InvalidOperationException;
    }
}
