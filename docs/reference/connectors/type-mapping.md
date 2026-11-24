# Database Native-to-Engine Type Mapping

How native database types land in the ETL-SQL engine when read from each database connector, and how
engine values are formatted when written back. Every entry below is verified against the connector's
`*DataSource.cs` implementation and `TypeConverter.cs`.

## Engine Runtime Types

Inside the engine (`#temp` tables, expressions, variables, `CAST`, and `TRANSFORM`), types follow
these rules:

- **Integers store as `decimal`**: `TINYINT`, `SMALLINT`, `INT`, `INTEGER`, and `BIGINT` all truncate
  and store as CLR `decimal` (`System.Decimal`) when cast or coerced by engine expressions and
  columnar operators. Database readers initially emit driver-native integer types (`byte`, `short`,
  `int`, `long`), which the engine normalizes to `decimal` during arithmetic, comparisons, and casts.
- **Exact numeric**: `DECIMAL`, `NUMERIC`, and `MONEY` store as `decimal`.
- **Floating point**: `FLOAT` and `DOUBLE` store as `double`; `REAL` stores as `float`.
- **Boolean**: `BIT`, `BOOLEAN`, and `BOOL` store as `bool`.
- **Temporal**: `DATE` stores as `DateOnly`; `DATETIME` and `TIMESTAMP` store as `DateTime`;
  `DATETIMEOFFSET` stores as `DateTimeOffset`; `TIME` stores as `TimeSpan`.
- **Text and structured text**: `CHAR`, `NCHAR`, `VARCHAR`, `NVARCHAR`, `VARCHAR2`, `TEXT`, `NTEXT`,
  `STRING`, `JSON`, and `XML` store as `string`.
- **Binary and identifiers**: `BINARY`, `VARBINARY`, `BLOB`, and `LOB` store as `byte[]`;
  `UNIQUEIDENTIFIER`, `GUID`, and `UUID` store as `Guid`.

---

## SQL Server (`MSSQL`)

Reads pass directly through `Microsoft.Data.SqlClient` (`reader.GetValue(i)`).

| Native Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `tinyint`, `smallint`, `int`, `bigint` | `byte`, `short`, `int`, `long` → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `decimal`, `numeric`, `money`, `smallmoney` | `decimal` | Exact precision preserved |
| `float`, `real` | `double`, `float` | IEEE 754 |
| `bit` | `bool` | — |
| `datetimeoffset` | `DateTimeOffset` | Offset preserved end-to-end |
| `date`, `datetime`, `datetime2`, `smalldatetime` | `DateTime` | Engine `DateOnly` writes serialize as `yyyy-MM-dd` in `SqlBulkCopy` |
| `time` | `TimeSpan` | — |
| `uniqueidentifier` | `Guid` | — |
| `binary`, `varbinary`, `rowversion`, `timestamp` | `byte[]` | Use `CAST(rv AS BIGINT)` in SQL Server pushdown for numeric watermarks |
| `char`, `varchar`, `nchar`, `nvarchar`, `xml` | `string` | — |

---

## PostgreSQL (`POSTGRES`)

Reads run through `MapPostgresValue(reader, i)` over `NpgsqlDataReader.GetValue(i)`.

| Native Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `smallint`, `integer`, `bigint` | `short`, `int`, `long` → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `numeric`, `decimal`, `money` | `decimal` | Exact precision preserved |
| `real`, `double precision` | `float`, `double` | — |
| `boolean` | `bool` | — |
| `timestamp with time zone` (`timestamptz`) | `DateTimeOffset` (UTC) | `MapPostgresValue` normalizes to UTC; writes format `yyyy-MM-dd HH:mm:ss.ffffff+00` in `COPY` and bind `NpgsqlDbType.TimestampTz` |
| `timestamp without time zone` | `DateTime` | — |
| `date` | `DateOnly` | Npgsql 6+ default |
| `interval` | `TimeSpan` | — |
| `json`, `jsonb` | `string` | Returned as JSON text; query with `JSON_VALUE` or `TRANSFORM ... USING SPLIT` |
| Arrays (`integer[]`, `text[]`, etc.) | CLR typed array (`int[]`, `string[]`, ...) | Use `ARRAY_TO_STRING(col, ',')` or `TO_JSON(col)::text` in pushdown for scalar staging |
| `uuid` | `Guid` | — |
| `bytea` | `byte[]` | — |
| `text`, `varchar`, `char` | `string` | Text-mode `COPY` writes replace embedded `\t`, `\n`, `\r` with spaces |

---

## Oracle (`ORACLE`)

Reads run through `MapOracleValue(reader.GetValue(i))` over ODP.NET.

| Native Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `NUMBER` (`OracleDecimal`) | `decimal` | Explicit cast in `MapOracleValue`. Values with >28–29 significant digits overflow CLR `decimal`; cast with `TO_CHAR` or `CAST(... AS BINARY_DOUBLE)` in pushdown |
| `BINARY_FLOAT`, `BINARY_DOUBLE` | `float`, `double` | — |
| `DATE` (`OracleDate`) | `DateTime` | Includes time component |
| `TIMESTAMP` (`OracleTimeStamp`) | `DateTime` | — |
| `TIMESTAMP WITH TIME ZONE` (`OracleTimeStampTZ`) | `DateTimeOffset` | Original offset preserved; writes bind `OracleDbType.TimeStampTZ` |
| `TIMESTAMP WITH LOCAL TIME ZONE` (`OracleTimeStampLTZ`) | `DateTimeOffset` | Converted using local time-zone offset |
| `INTERVAL DAY TO SECOND` (`OracleIntervalDS`) | `TimeSpan` | — |
| `VARCHAR2`, `NVARCHAR2`, `CHAR`, `CLOB` | `string` | — |
| `RAW`, `BLOB` | `byte[]` | — |

On write, `DateOnly` values serialize as `yyyy-MM-dd` in `OracleBulkCopy`.

---

## MySQL / MariaDB (`MYSQL`)

Reads pass directly through `MySqlConnector` (`reader.GetValue(i)`).

| Native Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `TINYINT(1)` | `bool` | `MySqlConnector` default (`TreatTinyAsBoolean=true`) |
| `TINYINT`, `SMALLINT`, `INT`, `BIGINT` (signed/unsigned) | CLR integer → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `DECIMAL`, `NUMERIC` | `decimal` | — |
| `FLOAT`, `DOUBLE` | `float`, `double` | — |
| `DATE`, `DATETIME`, `TIMESTAMP` | `DateTime` | Engine `DateOnly` writes serialize as `yyyy-MM-dd` in `MySqlBulkCopy` |
| `TIME` | `TimeSpan` | — |
| `JSON`, `VARCHAR`, `CHAR`, `TEXT`, `ENUM`, `SET` | `string` | `JSON` lands as JSON text |
| `BINARY`, `VARBINARY`, `BLOB` | `byte[]` | — |

---

## Snowflake (`SNOWFLAKE`)

Reads pass directly through `Snowflake.Data` (`reader.GetValue(i)`).

| Native Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `NUMBER(p, 0)`, `INT`, `BIGINT` | `long` / `decimal` → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `NUMBER(p, s)`, `DECIMAL` | `decimal` | — |
| `FLOAT`, `DOUBLE` | `double` | — |
| `BOOLEAN` | `bool` | — |
| `VARIANT`, `OBJECT`, `ARRAY` | `string` | Returned as JSON text; parse with `JSON_VALUE` / `TRANSFORM ... USING SPLIT` or flatten in pushdown with `LATERAL FLATTEN` |
| `DATE`, `TIMESTAMP_NTZ` | `DateTime` | — |
| `TIMESTAMP_LTZ`, `TIMESTAMP_TZ` | `DateTimeOffset` | — |
| `TIME` | `TimeSpan` | — |
| `VARCHAR`, `STRING`, `TEXT` | `string` | — |
| `BINARY` | `byte[]` | — |

---

## BigQuery (`BIGQUERY`)

Reads pass directly through `Google.Cloud.BigQuery.V2` (`row[fieldName]`).

| Native Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `INT64` | `long` → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `FLOAT64` | `double` | — |
| `NUMERIC`, `BIGNUMERIC` | `BigQueryNumeric`, `BigQueryBigNumeric` | Cast to `STRING` or `FLOAT64` in pushdown when using engine arithmetic |
| `BOOL` | `bool` | — |
| `STRING` | `string` | — |
| `BYTES` | `byte[]` | — |
| `DATE`, `DATETIME`, `TIMESTAMP` | `DateTime` | `TIMESTAMP` values are UTC |
| `TIME` | `TimeSpan` | — |
| `STRUCT` / `RECORD` | `Dictionary<string, object>` | Use `TO_JSON_STRING(col)` in SQL pushdown to land portable JSON text |
| `ARRAY<T>` | CLR array (`T[]`) | Use `UNNEST(col)` or `TO_JSON_STRING(col)` in pushdown for flat staging |

On write (`InsertRowsAsync`), engine `decimal` values serialize as invariant numeric strings because
the BigQuery REST streaming SDK does not accept CLR `decimal` directly.

---

## SQLite (`SQLITE`)

Reads pass directly through `Microsoft.Data.Sqlite` (`reader.GetValue(i)`). SQLite types values by
their stored **storage class**, not declared column affinity.

| Storage Class | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `INTEGER` | `long` → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `REAL` | `double` | — |
| `TEXT` | `string` | Dates, decimals, and GUIDs stored as ISO text arrive as `string` until cast |
| `BLOB` | `byte[]` | — |
| `NULL` | `null` | — |

On write, `DateOnly` values serialize as `yyyy-MM-dd`.

---

## ODBC (`ODBC`)

Reads pass directly through `System.Data.Odbc` (`reader.GetValue(i)`), so exact CLR types depend on
the installed vendor driver. Standard SQL types map to `int`/`long`, `decimal`, `double`, `bool`,
`DateTime`, `string`, and `byte[]`. For semi-structured warehouse columns (Databricks `STRUCT`/`ARRAY`,
Redshift `SUPER`, Trino `ROW`/`JSON`), cast to `VARCHAR` or JSON text inside SQL pushdown for
predictable staging.

---

## MongoDB (`MONGODB`)

Reads run through `ConvertBsonValue(BsonValue)`.

| BSON Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `BsonDocument`, `BsonArray` | `string` (JSON) | Serialized via `val.ToJson()` |
| `Int32`, `Int64` | `int`, `long` → `decimal` | Mapped via `BsonTypeMapper.MapToDotNetValue`; normalized to `decimal` on engine cast/arithmetic |
| `Double`, `Decimal128` | `double`, `decimal` | — |
| `Boolean`, `String`, `DateTime` | `bool`, `string`, `DateTime` | — |
| `ObjectId`, other BSON types | `ObjectId` or `string` | Fallback calls `val.ToString()` |

On write (`ConvertToBsonValue`), strings wrapped in `{...}` or `[...]` are parsed back into
`BsonDocument` / `BsonArray`.

---

## Neo4j (`NEO4J`)

Reads from `NODE_<LABEL>` / `EDGE_<TYPE>` return driver property values directly; `EXECUTE ... BEGIN ... END`
passes values through `ConvertNeo4jValue`.

| Cypher / Driver Type | Engine CLR Type | Notes |
| :--- | :--- | :--- |
| `Integer` | `long` → `decimal` | Normalized to `decimal` on engine cast/arithmetic |
| `Float` | `double` | — |
| `Boolean`, `String`, `Bytes` | `bool`, `string`, `byte[]` | — |
| `INode`, `IRelationship`, maps, lists (in `EXECUTE`) | `string` (JSON) | Serialized via `JsonSerializer.Serialize` |
| System columns (`_id`, `_labels`, `_from_id`, `_to_id`, `_from_label`, `_to_label`) | `string` | `_labels` is comma-separated |

On write, `DBNull.Value` writes as `NULL`, dates/times and GUIDs write as strings, and nested maps
or rows write as JSON text.

## References

- [Database Connectors](databases/README.md)
- [Connectors](README.md)
- [Data Types](../functions/README.md)
