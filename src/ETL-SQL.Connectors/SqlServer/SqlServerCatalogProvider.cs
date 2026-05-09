using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.SqlServer
{
    /// <summary>
    /// Imports column metadata from the SQL Server system catalog.
    /// Reads <c>sys.columns</c>, <c>sys.extended_properties</c>, and primary-key constraints
    /// to populate <c>@db_*</c> lineage tags.
    /// </summary>
    public sealed class SqlServerCatalogProvider : ICatalogMetadataProvider
    {
        private readonly string _connectionString;

        public SqlServerCatalogProvider(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
            string schema, string tableName, CancellationToken ct = default)
        {
            var results = new List<CatalogColumn>();
            const string sql = @"
SELECT
    c.name                                          AS ColumnName,
    tp.name                                         AS DataType,
    c.is_nullable                                   AS IsNullable,
    CAST(ISNULL(pk.is_primary_key, 0) AS BIT)       AS IsPrimaryKey,
    CAST(ep.value AS NVARCHAR(MAX))                 AS Description
FROM sys.columns c
JOIN sys.types tp ON tp.user_type_id = c.user_type_id
JOIN sys.objects o ON o.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id, 1 AS is_primary_key
    FROM sys.index_columns ic
    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = c.object_id
    AND ep.minor_id = c.column_id
    AND ep.class = 1
    AND ep.name = 'MS_Description'
WHERE s.name = @schema AND o.name = @table
ORDER BY c.column_id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
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
    fkc_col.name        AS ForeignKeyColumn,
    rs.name + '.' + rt.name AS ReferencedTable,
    rc_col.name         AS ReferencedColumn
FROM sys.foreign_key_columns fkcc
JOIN sys.foreign_keys fk ON fk.object_id = fkcc.constraint_object_id
JOIN sys.columns fkc_col ON fkc_col.object_id = fkcc.parent_object_id AND fkc_col.column_id = fkcc.parent_column_id
JOIN sys.objects ft ON ft.object_id = fkcc.parent_object_id
JOIN sys.schemas fs ON fs.schema_id = ft.schema_id
JOIN sys.objects rt ON rt.object_id = fkcc.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns rc_col ON rc_col.object_id = fkcc.referenced_object_id AND rc_col.column_id = fkcc.referenced_column_id
WHERE fs.name = @schema AND ft.name = @table;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
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
    }
}
