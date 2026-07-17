# Data Types

ETL-SQL's built-in data types. For converting between them, see
[Data Type Conversion](functions/conversion/data-conversion.md).

## Numeric Types

| Type | Size | Range / Precision |
| :--- | :--- | :--- |
| `TINYINT` | 1 byte | 0 to 255 |
| `SMALLINT` | 2 bytes | -32,768 to 32,767 |
| `INT` / `INTEGER` | 4 bytes | -2,147,483,648 to 2,147,483,647 |
| `BIGINT` | 8 bytes | -9.22e18 to 9.22e18 |
| `DECIMAL(P,S)` / `NUMERIC(P,S)` | 16 bytes | Up to 28-29 significant digits. P = total digits; S = digits after decimal |
| `FLOAT` / `DOUBLE` | 8 bytes | Approximate (~15-17 significant digits) |
| `REAL` | 4 bytes | Approximate (~6-9 significant digits) |
| `MONEY` | 8 bytes | Fixed-precision currency (4 decimal places) |

## Temporal Types

| Type | Precision |
| :--- | :--- |
| `DATE` | Date only (year/month/day) |
| `TIME` | Time of day (hour/min/sec/subsecond) |
| `DATETIME` | Date and time |
| `DATETIME2` | High-precision date/time (100 nanoseconds) |
| `DATETIMEOFFSET` | `DATETIME2` with time zone offset |
| `TIMESTAMP` | Alias for `DATETIME` |

**System constants:** use anywhere a datetime is expected:
- `SYSDATE` / `CURRENT_TIMESTAMP` are bare identifiers (no parentheses).
- `GETDATE()` / `NOW()` are functions, so parentheses are required.

**Date arithmetic:**
```sql
DECLARE @tomorrow = SYSDATE + 1;        -- Tomorrow
DECLARE @lastWeek = GETDATE() - 7;      -- 7 days ago
DECLARE @days INT = date2 - date1;      -- Difference in days (decimal)
```

## Character & String Types

| Type | Notes |
| :--- | :--- |
| `STRING` | Unicode text, arbitrary length |
| `VARCHAR(N)` | Unicode text, max N characters |
| `NVARCHAR(N)` | Synonym for `VARCHAR(N)` |
| `MARKDOWN` | Specialized string type that enables automatic Markdown rendering in reports |
| `CHAR(N)` | Fixed-length, N characters (padded with spaces) |
| `TEXT` | Alias for `STRING`; unbounded |

## Logical Types

| Type | Values | Notes |
| :--- | :--- | :--- |
| `BIT` / `BOOLEAN` / `BOOL` | `TRUE`/`FALSE`, `1`/`0` | Interchangeable |

## Binary Types

| Type | Notes |
| :--- | :--- |
| `VARBINARY` / `BINARY` | Raw byte sequences |
| `BLOB` / `IMAGE` | Alias for `VARBINARY`; typically from file connectors |

Casts to string yield Base64 or hex (e.g. `0x48656C6C6F`).

## Structured Types

| Type | Notes |
| :--- | :--- |
| `JSON` | Valid JSON string; manipulated with `JSON_*` functions |
| `XML` | Valid XML string; manipulated with `XML*` functions |
| `VECTOR` | Numeric array (e.g. `CAST('[0.1, 0.2]' AS VECTOR)`) |

## Collection & Specialized Types

| Type | Notes |
| :--- | :--- |
| `LIST` | Ordered collection. Construct with `(v1, v2, ...)` or `[v1, v2, ...]`. Used with `IN`, `FOREACH`, collection functions |
| `PATH` | File system path; normalizes separators cross-platform |
| `ENCRYPTED` | Semantic type for `ENC:`-prefixed secrets |
| `UNIQUEIDENTIFIER` / `UUID` / `GUID` | 128-bit globally unique identifier |
| `ANY` | Default when no type is declared; inferred from the assigned value |


## References

- [Data Type Conversion](functions/conversion/data-conversion.md)
- [Functions](functions/README.md)
- [Syntax Index](../syntax-index.md)
