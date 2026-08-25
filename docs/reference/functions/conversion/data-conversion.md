# Data Type Conversion

Convert values between ETL-SQL [data types](../../data-types.md). Use [`CAST`](cast.md) for strict
conversion (raises on failure) and [`TRY_CAST`](try_cast.md) for safe conversion (returns `NULL` on
failure); see also [`CONVERT`](convert.md).

## Syntax

```sql
CAST(expression AS target_type)
TRY_CAST(expression AS target_type)
CONVERT(target_type, expression [, style])
```

## Description

- **`CAST(expression AS target_type)`** — Strict conversion. Raises `ExecutionException` if conversion fails.
- **`TRY_CAST(expression AS target_type)`** — Safe conversion. Returns `NULL` if conversion fails. Ideal for dirty source data.
- **`CONVERT(target_type, expression [, style])`** — T-SQL style type conversion.

## Returns

The converted value in the requested target data type, or `NULL` if using `TRY_CAST` on unconvertible input.

## Examples
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

## References

- [Data Types](../../data-types.md)
- [CAST](cast.md) · [TRY_CAST](try_cast.md) · [CONVERT](convert.md)
- [Functions](../README.md)
