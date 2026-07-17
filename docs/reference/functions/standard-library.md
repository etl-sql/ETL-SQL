# ETL-SQL Standard Library & Data Types Reference

This document is the authoritative dictionary for all built-in types, casting behaviors, and scalar/aggregate/window functions within the ETL-SQL engine.

---

## 1. Data Types

### 1.1 Numeric Types

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

### 1.2 Temporal Types

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

### 1.3 Character & String Types

| Type | Notes |
| :--- | :--- |
| `STRING` | Unicode text, arbitrary length |
| `VARCHAR(N)` | Unicode text, max N characters |
| `NVARCHAR(N)` | Synonym for `VARCHAR(N)` |
| `MARKDOWN` | Specialized string type that enables automatic Markdown rendering in reports |
| `CHAR(N)` | Fixed-length, N characters (padded with spaces) |
| `TEXT` | Alias for `STRING`; unbounded |

### 1.4 Logical Types

| Type | Values | Notes |
| :--- | :--- | :--- |
| `BIT` / `BOOLEAN` / `BOOL` | `TRUE`/`FALSE`, `1`/`0` | Interchangeable |

### 1.5 Binary Types

| Type | Notes |
| :--- | :--- |
| `VARBINARY` / `BINARY` | Raw byte sequences |
| `BLOB` / `IMAGE` | Alias for `VARBINARY`; typically from file connectors |

Casts to string yield Base64 or hex (e.g. `0x48656C6C6F`).

### 1.6 Structured Types

| Type | Notes |
| :--- | :--- |
| `JSON` | Valid JSON string; manipulated with `JSON_*` functions |
| `XML` | Valid XML string; manipulated with `XML*` functions |
| `VECTOR` | Numeric array (e.g. `CAST('[0.1, 0.2]' AS VECTOR)`) |

### 1.7 Collection & Specialized Types

| Type | Notes |
| :--- | :--- |
| `LIST` | Ordered collection. Construct with `(v1, v2, ...)` or `[v1, v2, ...]`. Used with `IN`, `FOREACH`, collection functions |
| `PATH` | File system path; normalizes separators cross-platform |
| `ENCRYPTED` | Semantic type for `ENC:`-prefixed secrets |
| `UNIQUEIDENTIFIER` / `UUID` / `GUID` | 128-bit globally unique identifier |
| `ANY` | Default when no type is declared; inferred from the assigned value |

---

## 2. Type Conversion

### 2.1 `CAST(expression AS type)`
Strict conversion. Raises `ExecutionException` if conversion fails.

### 2.2 `TRY_CAST(expression AS type)`
Safe conversion. Returns `NULL` if conversion fails. Ideal for dirty source data.

```sql
-- Numeric
SELECT CAST('42' AS INT)              AS i;   -- 42
SELECT CAST('# Title' AS MARKDOWN)    AS md;  -- Evaluated as Markdown in UI
SELECT CAST('123.456' AS DECIMAL(5,2)) AS d;  -- 123.46 (rounded)
SELECT CAST('100.00' AS MONEY)        AS m;
SELECT TRY_CAST('N/A' AS INT)         AS bad; -- NULL (no exception)

-- Temporal
SELECT CAST('2026-04-08' AS DATE)                        AS dt;
SELECT CAST('2026-04-08 15:30:00 -05:00' AS DATETIMEOFFSET) AS dto;
SELECT CAST(GETDATE() AS NVARCHAR)                       AS str;

-- Binary / Hex
SELECT CAST('SGVsbG8=' AS VARBINARY)   AS b;   -- 0x48656C6C6F ('Hello')
SELECT CAST(0x48656C6C6F AS STRING)    AS s;   -- 'Hello'

-- Logical
SELECT CAST(1 AS BIT)                  AS b1;  -- TRUE
SELECT CAST('FALSE' AS BOOLEAN)        AS b2;  -- FALSE

-- Specialized
SELECT CAST('C:\Temp\' AS PATH)        AS p;   -- Normalized path
SELECT CAST('550e8400-...' AS GUID)    AS uid;
SELECT CAST('{"id": 1}' AS JSON)       AS j;
SELECT CAST('[0.1, 0.2]' AS VECTOR)    AS v;
```

---

## Functions

Built-in functions are documented one page per function under [Functions](README.md), grouped by
category. The per-function pages are the source of truth for signatures, parameters, return types,
null behavior, and examples — this reference does not restate them.

- [Aggregate](aggregate/avg.md) - summarize values across grouped rows.
- [Bitwise](bitwise/bitand.md) - operate on the bits of integer values.
- [Collections](collections/add_to_list.md) - build and reshape list and array values.
- [Conversion](conversion/cast.md) - cast and convert between types.
- [Cryptography](cryptography/hashbytes.md) - hashes and checksums.
- [Date and time](datetime/getdate.md) - date/time construction and arithmetic.
- [Environment](general/env.md) - environment and system values.
- [Error handling](error/error_message.md) - inspect the active error in a TRY/CATCH block.
- [File and path](file-path/file_exists.md) - inspect the filesystem and manipulate paths.
- [Fuzzy matching](fuzzy-match/soundex.md) - similarity, phonetics, and edit distance.
- [Job state](job/get_job_state.md) - durable per-job state for orchestrated runs.
- [JSON and XML](json-xml/json_value.md) - parse and query JSON and XML.
- [Math](math/abs.md) - arithmetic, trigonometry, and rounding.
- [Null handling](null-handler/coalesce.md) - substitute, detect, and compare NULLs.
- [Random and GUID](random-guid/newid.md) - random values and unique identifiers.
- [Regular expressions](regex/regexp_matches.md) - match, extract, split, and replace by pattern.
- [String](string/concat.md) - case, search, formatting, encoding, and splitting.
- [Table-valued](table-valued/connection_property.md) - functions that return a result set.
- [Tags and lineage](tags/get_tags.md) - read column and dataset tags.
- [Window](window/row_number.md) - per-row output with partition and frame metrics.

## References

- [Functions](README.md)
- [Syntax Index](../../syntax-index.md)
