using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Postgres
{
    /// <summary>
    /// Imports column metadata from the PostgreSQL system catalog.
    /// Reads <c>information_schema.columns</c>, <c>pg_catalog.obj_description</c>, and
    /// primary-key / foreign-key constraints to populate <c>@db_*</c> lineage tags.
    /// </summary>
    public sealed class PostgresCatalogProvider : ICatalogMetadataProvider, IViewDefinitionProvider
    {
        private readonly string _connectionString;

        public PostgresCatalogProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
            string schema, string tableName, CancellationToken ct = default)
        {
            var results = new List<CatalogColumn>();
            const string sql = @"
SELECT
    c.column_name,
    c.data_type,
    c.is_nullable = 'YES'                           AS is_nullable,
    COALESCE(pk.is_primary_key, false)              AS is_primary_key,
    pg_catalog.col_description(
        (quote_ident(c.table_schema) || '.' || quote_ident(c.table_name))::regclass::oid,
        c.ordinal_position)                         AS description
FROM information_schema.columns c
LEFT JOIN (
    SELECT kcu.column_name, true AS is_primary_key
    FROM information_schema.table_constraints tc
    JOIN information_schema.key_column_usage kcu
        ON kcu.constraint_name = tc.constraint_name
        AND kcu.table_schema = tc.table_schema
        AND kcu.table_name = tc.table_name
    WHERE tc.constraint_type = 'PRIMARY KEY'
      AND tc.table_schema = @schema AND tc.table_name = @table
) pk ON pk.column_name = c.column_name
WHERE c.table_schema = @schema AND c.table_name = @table
ORDER BY c.ordinal_position;";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
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
        }

        public async Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(
            string schema, string tableName, CancellationToken ct = default)
        {
            var results = new List<CatalogRelationship>();
            const string sql = @"
SELECT
    kcu.column_name                             AS fk_column,
    ccu.table_schema || '.' || ccu.table_name  AS referenced_table,
    ccu.column_name                            AS referenced_column
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
    ON kcu.constraint_name = tc.constraint_name
    AND kcu.table_schema = tc.table_schema
JOIN information_schema.referential_constraints rc
    ON rc.constraint_name = tc.constraint_name
JOIN information_schema.constraint_column_usage ccu
    ON ccu.constraint_name = rc.unique_constraint_name
WHERE tc.constraint_type = 'FOREIGN KEY'
  AND tc.table_schema = @schema AND tc.table_name = @table;";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
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
        }

        public async Task<string?> GetViewDefinitionAsync(string schema, string objectName, CancellationToken ct = default)
        {
            const string sql = @"
SELECT pg_get_viewdef(c.oid, true)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('v', 'm')
  AND n.nspname = @schema AND c.relname = @name;";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@name", objectName);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is string def && !string.IsNullOrWhiteSpace(def) ? def : null;
        }
    }
}
