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
| `BIGINT` | 8 bytes | -9.22 × 10¹⁸ to 9.22 × 10¹⁸ |
| `DECIMAL(P,S)` / `NUMERIC(P,S)` | 16 bytes | Up to 28–29 significant digits. P = total digits; S = digits after decimal |
| `FLOAT` / `DOUBLE` | 8 bytes | Approximate (~15–17 significant digits) |
| `REAL` | 4 bytes | Approximate (~6–9 significant digits) |
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

**System constants** — use anywhere a datetime is expected:
- `SYSDATE` / `CURRENT_TIMESTAMP` — bare identifiers (no parentheses)
- `GETDATE()` / `NOW()` — functions (parentheses **required**)

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
| `ANY` | Default when no type is declared — inferred from the assigned value |

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

## 3. String Functions

### 3.1 Case & Whitespace

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `UPPER` | `UPPER(str)` | All-uppercase string |
| `LOWER` | `LOWER(str)` | All-lowercase string |
| `INITCAP` | `INITCAP(str)` | First letter of each word capitalized |
| `TRIM` | `TRIM([BOTH\|LEADING\|TRAILING] [chars FROM] str)` | Whitespace (or specified chars) removed |
| `LTRIM` | `LTRIM(str)` | Leading whitespace removed |
| `RTRIM` | `RTRIM(str)` | Trailing whitespace removed |
| `REVERSE` | `REVERSE(str)` | Characters in reverse order |

### 3.2 Substrings & Search

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `SUBSTRING` / `SUBSTR` | `SUBSTRING(str, start, length)` | Portion of a string (1-indexed) |
| `LEFT` | `LEFT(str, n)` | Leftmost N characters |
| `RIGHT` | `RIGHT(str, n)` | Rightmost N characters |
| `POSITION` / `INSTR` | `POSITION(sub IN str)` | 1-based index of substring, or 0 |
| `CHARINDEX` | `CHARINDEX(sub, str [, start])` | 1-based index, with optional start offset |
| `PATINDEX` | `PATINDEX(pattern, str)` | 1-based position of wildcard pattern |
| `OVERLAY` | `OVERLAY(str PLACING new FROM pos [FOR len])` | Replaces portion of string |

### 3.3 Concatenation & Splitting

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `CONCAT` | `CONCAT(s1, s2, ...)` | All arguments joined |
| `CONCAT_WS` | `CONCAT_WS(sep, s1, s2, ...)` | Arguments joined with separator; nulls skipped |
| `STRING_AGG` | `STRING_AGG(col, sep) [WITHIN GROUP (ORDER BY col)]` | Aggregate — rows joined into one string |
| `STRING_SPLIT` | `STRING_SPLIT(str, sep)` | Table-valued: rows of substrings |
| `SPLIT_PART` | `SPLIT_PART(str, delim, n)` | Nth segment after splitting by delimiter |

### 3.4 Formatting & Padding

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `LPAD` | `LPAD(str, len [, pad_str])` | Left-pads string to target length |
| `RPAD` | `RPAD(str, len [, pad_str])` | Right-pads string to target length |
| `REPEAT` | `REPEAT(str, n)` | Repeats string N times (alias for `REPLICATE`) |
| `FORMAT` | `FORMAT(val, fmt)` | Value formatted by .NET format string (e.g. `'yyyy-MM-dd'`, `'N2'`) |
| `STR` | `STR(float [, len [, dec]])` | Numeric value as a right-padded string |
| `REPLICATE` | `REPLICATE(str, n)` | String repeated N times |
| `SPACE` | `SPACE(n)` | N space characters |
| `QUOTENAME` | `QUOTENAME(str [, char])` | Delimited identifier (default: `[str]`) |
| `TO_STR` | `TO_STR(val)` | Any value converted to string |

### 3.5 Character Encoding

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `ASCII` / `UNICODE` | `ASCII(str)` | Numeric code of the first character |
| `CHAR` | `CHAR(n)` | Character for the given ASCII/Unicode code |
| `DATALENGTH` | `DATALENGTH(val)` | Byte count of the value |

### 3.6 Translation & Escaping

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `TRANSLATE` | `TRANSLATE(str, from, to)` | Characters in `from` replaced by corresponding chars in `to` |
| `REPLACE` | `REPLACE(str, search, new)` | All occurrences of `search` replaced |
| `STRING_ESCAPE` | `STRING_ESCAPE(text, type)` | Special chars escaped (e.g. `'json'`) |
| `LEN` / `LENGTH` | `LEN(str)` | Character count of a string; item count of a list |
| `CHARACTER_LENGTH` | `CHARACTER_LENGTH(str)` | ANSI-style character count |
| `OCTET_LENGTH` | `OCTET_LENGTH(str)` | ANSI-style byte count (UTF-8) |

### 3.7 Regex (`PCRE`)

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `REGEXP_LIKE` | `REGEXP_LIKE(str, pattern [, flags])` | `1` if matched, `0` otherwise |
| `REGEXP_SUBSTR` | `REGEXP_SUBSTR(str, pattern [, pos, occ, flags])` | Matched substring |
| `REGEXP_REPLACE` | `REGEXP_REPLACE(str, pattern, new [, pos, occ, flags])` | String with replacements |
| `REGEXP_INSTR` | `REGEXP_INSTR(str, pat [, pos, occ, option, flags])` | 1-based position of match |
| `REGEXP_COUNT` | `REGEXP_COUNT(str, pattern [, pos, flags])` | Number of matches found |
| `REGEXP_MATCHES` | `REGEXP_MATCHES(str, pattern)` | Table of all matches |
| `REGEXP_SPLIT_TO_TABLE` | `REGEXP_SPLIT_TO_TABLE(str, pattern)` | Table of segments split by pattern |

*Example:*
```sql
-- Extract a phone number and redact all digits
SELECT
    REGEXP_SUBSTR(Notes, '\(\d{3}\) \d{3}-\d{4}') AS Phone,
    REGEXP_REPLACE(Notes, '\d', '*')               AS Redacted
FROM #comments;
```

---

## 4. Date & Time Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `GETDATE` / `NOW` | `GETDATE()` | Current system date and time |
| `DATEADD` | `DATEADD(part, n, date)` | Date with N intervals added. Parts: `YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND` |
| `DATEDIFF` | `DATEDIFF(part, start, end)` | Count of part boundaries crossed between two dates |
| `DATEPART` | `DATEPART(part, date)` | Integer value of the date part (e.g. month → 4) |
| `DATE_PART` | `DATE_PART(part, date)` | Integer value of the date part (alias for `DATEPART`) |
| `DATENAME` | `DATENAME(part, date)` | String name of the date part (e.g. month → `'April'`) |
| `EXTRACT` | `EXTRACT(field FROM source)` | ANSI-style part extraction (`YEAR`, `MONTH`, `DAY`, `DOW`, `DOY`, `HOUR`, `MINUTE`, `SECOND`, `EPOCH`, `QUARTER`, `WEEK`, `ISODOW`, `DECADE`, `CENTURY`, `MILLENNIUM`) |
| `YEAR` | `YEAR(date)` | Integer year |
| `MONTH` | `MONTH(date)` | Integer month (1–12) |
| `DAY` | `DAY(date)` | Integer day of month |
| `EOMONTH` | `EOMONTH(date [, months])` | Last day of the month; optionally offset by N months |
| `ISDATE` | `ISDATE(expr)` | `1` if parseable as date, `0` otherwise |
| `DATETIMEFROMPARTS` | `DATETIMEFROMPARTS(y, m, d, h, mi, s, ms)` | Constructs `DATETIME` from component parts |
| `TIMEFROMPARTS` | `TIMEFROMPARTS(h, mi, s, frac, prec)` | Constructs `TIME` from parts |
| `TRUNC` / `TO_DATE` | `TRUNC(datetime)` | Truncates the time portion; returns date |
| `DATE_TRUNC` | `DATE_TRUNC(part, date)` | Truncates date to specified boundary (Postgres-compatible parameter order) |
| `TO_TIMESTAMP` | `TO_TIMESTAMP(seconds)` | Converts Unix epoch seconds (with fractional seconds) to a `DATETIME` |
| `AT TIME ZONE` | `expr AT TIME ZONE 'tz_id'` | Converts expression to the specified timezone |

**Common Windows timezone IDs:** `UTC`, `Eastern Standard Time`, `Central Standard Time`, `Mountain Standard Time`, `Pacific Standard Time`, `GMT Standard Time`, `W. Europe Standard Time`, `Tokyo Standard Time`

*Examples:*
```sql
-- Date arithmetic
SELECT DATEADD(DAY, -7, GETDATE()) AS LastWeek;
SELECT DATEDIFF(MONTH, '2026-01-01', GETDATE()) AS MonthsElapsed;
SELECT DATEPART(QUARTER, GETDATE()) AS CurrentQuarter;
SELECT YEAR(OrderDate), MONTH(OrderDate), DAY(OrderDate) FROM #orders;

-- Build a date from parts
SELECT DATETIMEFROMPARTS(2026, 4, 1, 8, 0, 0, 0) AS StartOfMonth;

-- Timezone conversion
DECLARE @nyTime = GETDATE() AT TIME ZONE 'Eastern Standard Time';
SELECT OrderDate AT TIME ZONE 'UTC' AS UtcDate FROM #orders;
```

---

## 5. Math & Scalar Functions

### 5.1 Arithmetic

| Function | Returns |
| :--- | :--- |
| `ABS(n)` | Absolute value |
| `CEILING(n)` | Smallest integer ≥ n |
| `FLOOR(n)` | Largest integer ≤ n |
| `ROUND(n, d)` | n rounded to d decimal places |
| `SIGN(n)` | `1` (positive), `-1` (negative), `0` (zero) |
| `MOD(n, d)` / `n % d` | Remainder of n ÷ d |
| `POWER(base, exp)` | base^exp |
| `SQRT(n)` | Square root |
| `EXP(n)` | e^n |
| `LOG(n)` / `LN(n)` | Natural logarithm (base e) |
| `LOG10(n)` | Base-10 logarithm |
| `RAND([seed])` | Pseudo-random float in [0, 1] |
| `LEAST(v1, v2, ...)` | Smallest of all arguments |
| `GREATEST(v1, v2, ...)` | Largest of all arguments |

### 5.2 Trigonometry (Input/Output in Radians)

| Function | Returns |
| :--- | :--- |
| `PI()` | Returns the mathematical constant $\pi$ |
| `DEGREES(r)` | Converts radians to degrees |
| `RADIANS(d)` | Converts degrees to radians |
| `COT(r)` | Cotangent |
| `SIN(r)` | Sine |
| `COS(r)` | Cosine |
| `TAN(r)` | Tangent |
| `ASIN(r)` | Arcsine (result in radians) |
| `ACOS(r)` | Arccosine (result in radians) |
| `ATAN(r)` | Arctangent (result in radians) |
| `ATAN2(y, x)` | Quadrant-aware arctangent of point (x,y) |

*Converting degrees to radians:* `degrees * (3.14159265 / 180.0)`

```sql
DECLARE @deg = 45.0;
DECLARE @rad = @deg * (3.14159265 / 180.0);
SELECT SIN(@rad), COS(@rad), TAN(@rad);
SELECT ATAN2(1.0, 1.0) AS Angle45;   -- ~0.785 radians (π/4)
```

### 5.3 Bitwise Functions

| Function | Signature | Returns / Return Type |
| :--- | :--- | :--- |
| `BITAND` | `BITAND(a, b)` | `BIGINT` — bitwise AND of two integers |
| `BITOR` | `BITOR(a, b)` | `BIGINT` — bitwise OR of two integers |
| `BITXOR` | `BITXOR(a, b)` | `BIGINT` — bitwise XOR of two integers |
| `BITNOT` | `BITNOT(a)` | `BIGINT` — bitwise NOT of an integer |
| `BITSHIFTLEFT` | `BITSHIFTLEFT(a, n)` | `BIGINT` — bitwise left shift of `a` by `n` bits |
| `BITSHIFTRIGHT` | `BITSHIFTRIGHT(a, n)` | `BIGINT` — bitwise right shift of `a` by `n` bits |
| `BIT_COUNT` | `BIT_COUNT(a)` | `BIGINT` — popcount (count of set bits) in the integer |

*Example:*
```sql
-- Shift values and count active bits
SELECT 
    BITSHIFTLEFT(1, 4)  AS ShiftedLeft,  -- Returns 16 (1 << 4)
    BITSHIFTRIGHT(32, 2) AS ShiftedRight, -- Returns 8 (32 >> 2)
    BIT_COUNT(7)        AS SetBitsCount;  -- Returns 3 (binary 111)
```

---

## 6. Statistical Aggregates

For paired functions (`CORR`, `COVAR_*`), rows where either input is `NULL` are excluded from the calculation.

| Function | Returns |
| :--- | :--- |
| `SUM(col)` | Total sum |
| `AVG(col)` | Arithmetic mean |
| `COUNT([DISTINCT] col)` | Non-null count (or distinct count) |
| `APPROX_COUNT_DISTINCT(col)` | HyperLogLog-based approximate count of distinct non-null values |
| `MIN(col)` / `MAX(col)` | Minimum / maximum value |
| `VAR(col)` / `VAR_SAMP(col)` | Sample variance |
| `VARP(col)` / `VAR_POP(col)` | Population variance |
| `STDEV(col)` / `STDDEV_SAMP(col)` | Sample standard deviation |
| `STDEVP(col)` / `STDDEV_POP(col)` | Population standard deviation |
| `COVAR_SAMP(x, y)` | Sample covariance |
| `COVAR_POP(x, y)` | Population covariance |
| `CORR(x, y)` | Pearson correlation coefficient (−1.0 to 1.0) |
| `EVERY(expr)` | `TRUE` when every non-null boolean input is true; `NULL` when there are no non-null inputs |
| `ANY(expr)` / `SOME(expr)` | `TRUE` when any non-null boolean input is true; `NULL` when there are no non-null inputs |
| `STRING_AGG(col, sep) [WITHIN GROUP (ORDER BY col)]` | Rows concatenated into one string |

*Example:*
```sql
SELECT
    AVG(Price)           AS AvgPrice,
    APPROX_COUNT_DISTINCT(CustomerId) AS ApproxCustomers,
    STDEV(Price)         AS PriceVolatility,
    CORR(Price, Qty)     AS PriceQtyCorrelation,
    EVERY(InStock)       AS AllInStock,
    ANY(Backordered)     AS HasBackorders,
    STRING_AGG(SKU, ', ') WITHIN GROUP (ORDER BY SKU ASC) AS AllSKUs
FROM #products;
```

---

## 7. Conditional & Null-Handling Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `COALESCE` | `COALESCE(v1, v2, ...)` | First non-null value |
| `ISNULL` / `NVL` / `IFNULL` | `ISNULL(v, default)` | `default` if `v` is null, else `v` |
| `NVL2` | `NVL2(v, if_not_null, if_null)` | Oracle-style conditional null |
| `NULLIF` | `NULLIF(v1, v2)` | `NULL` if `v1 = v2`, else `v1` |
| `IS_NULL` | `IS_NULL(expr)` | `TRUE` if expression is null |
| `IS_NOT_NULL` | `IS_NOT_NULL(expr)` | `TRUE` if expression is not null |
| `IIF` | `IIF(cond, true_val, false_val)` | Inline conditional return |
| `DECODE` | `DECODE(val, s1, r1, ..., default)` | Oracle-style `CASE` shorthand |
| `CASE...WHEN...END` | — | Sequential conditional evaluation |

*Example:*
```sql
-- Default to 'Unknown' if region is null or empty
SELECT COALESCE(NULLIF(TRIM(Region), ''), 'Unknown') AS Region FROM #staging;

-- Inline conditional
SELECT IIF(Score >= 90, 'Pass', 'Fail') AS Result FROM #tests;
```

---

## 8. System & Identity Functions

| Function | Returns |
| :--- | :--- |
| `NEWID()` / `NEWSEQUENTIALID()` | New UUID v7 (time-ordered unique identifier) |
| `GETDATE()` / `NOW()` | Current system date and time |
| `SYSDATE` / `CURRENT_TIMESTAMP` | Current date/time (bare identifiers — no parentheses) |
| `ERROR_MESSAGE()` | Error message string inside `CATCH` block |
| `ERROR_NUMBER()` | Error number/code inside `CATCH` block |
| `ERROR_SEVERITY()` | Error severity level inside `CATCH` block |
| `ERROR_STATE()` | Error state code inside `CATCH` block |
| `ERROR_LINE()` | Line number where error occurred |
| `ENV(variable)` | Value of a host environment variable (see security note below) |
| `CONNECTION_PROPERTY(conn, prop)` | Value of a connection option, masking passwords/keys |
| `@@TRANCOUNT` | Current transaction nesting level |
| `@@VERSION` | Full engine version and metadata string |
| `@@RESULTSETS` | Number of result sets produced by the last statement |
| `@@ERROR` | Error number of the last statement (0 = success); equivalent to `ERROR_NUMBER()` inside a `CATCH` |
| `@@TOTAL_SPILLED_BYTES` | Bytes written to disk by the external window/join engine during the last spilling operation |
| `@@PARTITIONS_COUNT` | Number of disk-partition files created during the last external spill |
| `@@SUBQUERY_CACHE_HITS` | Total scalar subquery hits in the result cache |
| `@@SUBQUERY_CACHE_MISSES` | Total scalar subquery misses in the result cache |
| `@@PEAK_MEMORY_MB` | Peak memory (Working Set) of the engine process in MB |
| `@@SORT_SPILLS` | Number of external sort runs that spilled to disk |
| `@@LAST_EXEC_MS` | Milliseconds taken by the last executed statement |
| `@@ROWCOUNT` | Number of rows processed by the last statement |
| `@@FILE_EXISTS(path)` | `TRUE` if the specified file exists on disk |
| `@@DIRECTORY_EXISTS(path)` | `TRUE` if the directory exists on disk |

> [!IMPORTANT]
> **Security Guardrail (Zero-Trust):** To prevent unauthorized harvesting of host information, `ENV()` can only access environment variables explicitly authorized in the `SecurityService.AllowedEnvVars` allow-list. Accessing an unauthorized variable throws a `SecurityException`.

---

## 9. Hashing & Checksums

| Function | Returns | Return Type |
| :--- | :--- | :--- |
| `CHECKSUM(v1, v2, ...)` | 64-bit integer hash of all arguments | `BIGINT` |
| `BINARY_CHECKSUM(v1, ...)` | Binary-compatible hash | `BIGINT` |
| `HASHBYTES('algo', val)` | Cryptographic hash | `VARBINARY` |

Supported algorithms for `HASHBYTES`: `MD5`, `SHA1`, `SHA256` / `SHA2_256`, `SHA512` / `SHA2_512`

*Change-data-capture (CDC) pattern — detect changed rows efficiently:*
```sql
-- Source has changed rows if the checksum differs from last load
SELECT
    s.id,
    CHECKSUM(s.Name, s.Status, s.UpdatedAt) AS RowHash
INTO #staging_cdc
FROM source_db.Customers AS s;

-- MERGE only rows whose hash changed
MERGE INTO target.Customers AS T
USING #staging_cdc AS S
ON T.id = S.id
WHEN MATCHED AND T.RowHash <> S.RowHash THEN
    UPDATE SET T.RowHash = S.RowHash, T.UpdatedAt = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (id, RowHash) VALUES (S.id, S.RowHash);
```

---

## 10. Collection & List Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `LENGTH` | `LENGTH(list\|str)` | Item count (list) or character count (string) |
| `SORT_LIST` | `SORT_LIST(list [, 'ASC'\|'DESC'])` | Sorted copy of the list |
| `APPEND_TO_LIST` / `ADD_TO_LIST` | `APPEND_TO_LIST(@list, value)` | List with value added |
| `REMOVE_FROM_LIST` | `REMOVE_FROM_LIST(@list, value)` | List with all occurrences of value removed |
| `GENERATE_SERIES` | `GENERATE_SERIES(start, stop [, step])` | Table: one row per value in the numeric range |

*Example:*
```sql
DECLARE @ids LIST = (1, 2, 3);
SET @ids = APPEND_TO_LIST(@ids, 4);
FOREACH @id IN @ids
BEGIN
    SELECT * INTO #rows FROM conn.Data WHERE id = @id;
END
```

---

## 11. JSON Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `JSON_VALUE` | `JSON_VALUE(json, path)` | Scalar extracted value |
| `JSON_QUERY` | `JSON_QUERY(json, path)` | JSON object/array fragment |
| `JSON_MODIFY` | `JSON_MODIFY(json, path, val)` | Updated JSON string |
| `ISJSON` | `ISJSON(str)` | `1` if valid JSON |
| `JSON_EXISTS` | `JSON_EXISTS(json, path)` | `1` if path exists |
| `JSON_OBJECT` | `JSON_OBJECT(k1, v1, k2, v2, ...)` | JSON object from key/value pairs |
| `JSON_ARRAY` | `JSON_ARRAY(v1, v2, ...)` | JSON array from values |
| `JSON_TABLE` | `JSON_TABLE(json, path COLUMNS (...))` | Table projected from JSON rows |
| `OPENJSON` | `OPENJSON(json [, path])` | SQL Server-style JSON expansion |

`JSON_TABLE` supports the SQL-style `COLUMNS` clause for typed projection, ordinality, existence checks, and missing-value defaults:

```sql
SELECT *
FROM JSON_TABLE(@payload, '$.items[*]' COLUMNS (
    ord FOR ORDINALITY,
    id INT PATH '$.id',
    name STRING PATH '$.name' DEFAULT 'Unknown' ON EMPTY,
    has_discount EXISTS PATH '$.discount'
));
```

---

## 12. XML Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `XMLVALUE` / `EXTRACTVALUE` | `XMLVALUE(xml, xpath)` | Scalar value at XPath |
| `XMLEXISTS` | `XMLEXISTS(xml, xpath)` | `1` if XPath matches |
| `XMLQUERY` | `XMLQUERY(xml, xpath)` | XML fragment |
| `XMLTABLE` | `XMLTABLE(xml, xpath)` | Table from XML rows |
| `XMLELEMENT` | `XMLELEMENT(name, contents)` | Constructs an XML element |
| `XMLATTRIBUTES` | `XMLATTRIBUTES(n1, v1, ...)` | XML attributes for an element |
| `XMLFOREST` | `XMLFOREST(n1, v1, ...)` | Forest of XML elements |

---

## 13. Window Functions

Window functions compute a value for each row based on a related set of rows defined by the `OVER` clause. They do not collapse rows the way `GROUP BY` does.

### 13.1 Syntax

```sql
<function>() OVER (
    [PARTITION BY <cols>]
    [ORDER BY <col> [ASC|DESC], ...]
    [ROWS | RANGE BETWEEN <start> AND <end>]
)
```

**Frame bounds:** `UNBOUNDED PRECEDING`, `n PRECEDING`, `CURRENT ROW`, `n FOLLOWING`, `UNBOUNDED FOLLOWING`

When no frame is specified, aggregate window functions default to the full partition; analytic functions (`LAG`, `LEAD`) default to a single row.

### 13.2 Ranking Functions

| Function | Returns |
| :--- | :--- |
| `ROW_NUMBER()` | Unique sequential integer per partition, starting at 1 |
| `RANK()` | Rank with gaps on ties (1, 1, 3, 4, …) — requires `ORDER BY` |
| `DENSE_RANK()` | Rank without gaps on ties (1, 1, 2, 3, …) — requires `ORDER BY` |
| `NTILE(n)` | Bucket number 1–n distributed as evenly as possible |
| `PERCENT_RANK()` | Relative rank as (rank − 1) / (N − 1), range 0–1 |
| `CUME_DIST()` | Cumulative distribution as peer_end_position / N, range (0, 1] |

### 13.3 Analytic Functions

| Function | Returns |
| :--- | :--- |
| `LAG(col [, offset [, default]])` | Value from a previous row (`offset` default: 1) |
| `LEAD(col [, offset [, default]])` | Value from a subsequent row (`offset` default: 1) |
| `FIRST_VALUE(col)` | First value in the partition |
| `LAST_VALUE(col)` | Last value in the partition |
| `NTH_VALUE(col, n)` | Value of the Nth row in the window frame |

> **Note:** `FIRST_VALUE` and `LAST_VALUE` always use the full partition, not an explicit frame clause. Explicit frames on these functions are parsed but not applied; use `NTH_VALUE` when frame-scoped first/last is needed.

### 13.4 Aggregate Window Functions

All standard aggregates support an `OVER` clause. When used as window functions they produce per-row values without collapsing the result set.

| Function | Description |
| :--- | :--- |
| `SUM(col)` | Running or framed sum |
| `AVG(col)` | Running or framed average |
| `COUNT(*) / COUNT(col)` | Row count in the window |
| `MIN(col)` / `MAX(col)` | Minimum / maximum in the window |
| `VAR(col)` / `VAR_SAMP(col)` | Sample variance |
| `VARP(col)` / `VAR_POP(col)` | Population variance |
| `STDEV(col)` / `STDDEV_SAMP(col)` | Sample standard deviation |
| `STDEVP(col)` / `STDDEV_POP(col)` | Population standard deviation |
| `COVAR_SAMP(x, y)` | Sample covariance of two columns |
| `COVAR_POP(x, y)` | Population covariance of two columns |
| `CORR(x, y)` | Pearson correlation coefficient |
| `STRING_AGG(col, sep)` | Concatenate values with separator |

### 13.5 Distribution Functions

| Function | Returns |
| :--- | :--- |
| `PERCENTILE_CONT(n)` | Continuous percentile; uses `WITHIN GROUP (ORDER BY col)` |
| `PERCENTILE_DISC(n)` | Discrete percentile; uses `WITHIN GROUP (ORDER BY col)` |

### 13.6 Frame Behavior

| Frame type | Behavior |
| :--- | :--- |
| `ROWS BETWEEN n PRECEDING AND m FOLLOWING` | Physical row offsets — always fully supported |
| `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW` | Cumulative from partition start to current row |
| `RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW` | Extends to all peers of the current row (logical peers share the same `ORDER BY` value) |
| Other `RANGE` bounds | Parsed; treated as full partition (true range-based peer comparison is not implemented) |

### 13.7 Examples

```sql
-- Running total and 3-row moving average
SELECT
    Date,
    Amount,
    SUM(Amount)  OVER(ORDER BY Date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningTotal,
    AVG(Amount)  OVER(ORDER BY Date ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING)         AS Moving3Avg,
    LAG(Amount, 1, 0) OVER(ORDER BY Date)                                             AS PrevDayAmount,
    RANK()       OVER(PARTITION BY Region ORDER BY Amount DESC)                       AS RankInRegion
FROM #DailySales;

-- Median price per category (continuous percentile)
SELECT
    Category,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Price) OVER(PARTITION BY Category) AS MedianPrice
FROM #products;

-- Standard deviation and correlation over a partition
SELECT
    Region,
    SaleDate,
    Amount,
    STDEV(Amount)      OVER(PARTITION BY Region) AS RegionStdDev,
    CORR(Amount, Cost) OVER(PARTITION BY Region) AS PriceCorrelation
FROM #sales;
```

### 13.8 Large-Scale Window Processing (ExternalWindowEngine)

When the number of rows in a `SELECT` result exceeds `WINDOW_SPILL_THRESHOLD` (default 100,000), the engine automatically switches from in-memory processing to the `ExternalWindowEngine`. This is transparent — query syntax does not change.

**How it works:**

1. Window functions in the same `SELECT` that share identical `PARTITION BY` + `ORDER BY` are grouped into a single processing pass.
2. For each group, the input stream is hash-partitioned into `EXTERNAL_HASH_PARTITIONS` (default 32) buckets and written to disk as newline-delimited JSON.
3. Each bucket is loaded and processed independently, keeping memory bounded regardless of total row count.
4. If one partition exceeds the threshold and the group contains only `ROW_NUMBER`, `RANK`, or `DENSE_RANK`, a streaming *deep-spill* path is used — the partition is never fully materialized.
5. When a `SELECT` mixes window functions with different signatures, each signature group is processed in sequence, spilling intermediate results between passes.

**Configuration:**

```sql
SET WINDOW_SPILL_THRESHOLD   = 50000;  -- lower threshold for tighter memory limits
SET EXTERNAL_HASH_PARTITIONS = 64;     -- more partitions for larger datasets
```

Spill metrics are available after a query:

```sql
SHOW VARIABLES;
-- @@TOTAL_SPILLED_BYTES  — bytes written to disk
-- @@PARTITIONS_COUNT     — number of partition files created
```

---

## 14. File System Functions
| Function | Signature | Returns |
| :--- | :--- | :--- |
| `FILE_EXISTS` | `FILE_EXISTS(path)` | `TRUE` if the file exists |
| `DIRECTORY_EXISTS` | `DIRECTORY_EXISTS(path)` | `TRUE` if the directory exists |
| `FILE_LIST` | `FILE_LIST(path [, recursive])` | Table: `NAME`, `PATH`, `EXTENSION`, `SIZE`, `LASTMODIFIED`, `ISREADONLY`, `CREATIONTIME` |
| `REMOTE_FILE_LIST` | `REMOTE_FILE_LIST(conn_name [, path])` | Table from SFTP/FTP/Blob: `NAME`, `FULLPATH`, `SIZE`, `LASTMODIFIED`, `ISDIRECTORY` |
| `REMOTE_FILE_EXISTS` | `REMOTE_FILE_EXISTS(conn_name, path)` | `TRUE` if the remote file or directory exists |
| `FILE_HASH` | `FILE_HASH(path [, algo])` | Lowercase hex checksum of file (`MD5`, `SHA1`, `SHA256`, `SHA512`) |
| `FILE_SIZE` | `FILE_SIZE(path)` | Size of local file in bytes |
| `FILE_MODIFIED` | `FILE_MODIFIED(path)` | Last write timestamp as a `DATETIME` |
| `PATH_COMBINE` | `PATH_COMBINE(p1, p2 [, ...])` | Combines path segments securely |
| `PATH_FILENAME` | `PATH_FILENAME(path)` | Extracts the filename and extension portion |
| `PATH_EXTENSION` | `PATH_EXTENSION(path)` | Extracts the extension portion (with leading dot) |
| `PATH_DIRECTORY` | `PATH_DIRECTORY(path)` | Extracts the directory path portion |


#### `FILE_LIST` / `DIRECTORY` Schema
Returns a table with one row per file found:
- `NAME` (STRING): The filename with extension (e.g. `data.csv`).
- `PATH` (STRING): The absolute local path (e.g. `C:\Data\data.csv`).
- `EXTENSION` (STRING): The file extension including the dot.
- `SIZE` (DECIMAL): Size in bytes.
- `LASTMODIFIED` (DATETIME): Last write time.
- `ISREADONLY` (BIT): Whether the file is read-only.
- `CREATIONTIME` (DATETIME): Creation time.

#### `REMOTE_FILE_LIST` Schema
Returns a table from a remote connection (SFTP, FTP, Azure Blob):
- `NAME` (STRING): The name of the file or directory.
- `FULLPATH` (STRING): The full remote path.
- `SIZE` (DECIMAL): Size in bytes.
- `LASTMODIFIED` (DATETIME): Last modify time provided by the server.
- `ISDIRECTORY` (BIT): `TRUE` if the entry is a directory.

*Examples:*
```sql
-- Check before processing
IF FILE_EXISTS('C:\Incoming\data.csv')
BEGIN
    CREATE CONNECTION src AS FLATFILE('C:\Incoming\data.csv');
END

-- List an SFTP directory as a queryable table
SELECT Name, Size, LastModified
INTO #remote_inventory
FROM REMOTE_FILE_LIST(sftp_conn, '/var/ftp/incoming/');
```

---

## 15. Lineage & Metadata Tag Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `GET_TAGS` | `GET_TAGS(table [, column])` | `LIST` of all custom tag names on the table or column |
| `GET_TAG_VALUE` | `GET_TAG_VALUE(table, column, tag_name)` | String value of a specific tag |

*Example:*
```sql
-- Apply tags during SELECT
SELECT
    UserId   /* @d: Internal user ID; @PII: true; */,
    UserName /* @d: Full display name; @owner: Chuck; */
INTO #TaggedUsers
FROM m.Users /* @sensitivity: high; */;

-- Interrogate tags programmatically
DECLARE @tags LIST = GET_TAGS('#TaggedUsers', 'UserId');
IF 'PII' IN @tags
BEGIN
    PRINT 'Warning: UserId is tagged as PII.';
END

PRINT GET_TAG_VALUE('#TaggedUsers', 'UserId', 'd');  -- 'Internal user ID'
```

---

## 16. Fuzzy Matching Functions

Fuzzy matching functions enable comparison and normalization of strings that may differ due to typos, abbreviations, formatting variation, or phonetic spelling differences. Apply `NORMALIZE` first to eliminate surface variation, then use `SIMILARITY` or a phonetic function to score candidates.

### 16.1 `NORMALIZE` — Domain-Aware Preprocessing

Preprocesses a string to eliminate surface variation before similarity scoring. Applying `NORMALIZE` before `SIMILARITY` typically raises match rates by 5–15 percentage points at the same threshold.

| Syntax | What it does |
| :--- | :--- |
| `NORMALIZE(s)` | Base: lowercase, trim, collapse whitespace, Unicode NFC, strip control characters |
| `NORMALIZE(s, 'COMPANY')` | Remove legal suffixes (LLC, Inc, Corp, Ltd…), expand `&` → `and`, `Mfg` → `Manufacturing`, strip leading articles (The/A/An), strip punctuation |
| `NORMALIZE(s, 'PERSON')` | Remove titles and generational suffixes (Mr, Mrs, Dr, Jr, Sr, MD, PhD…), normalize hyphens in hyphenated names |
| `NORMALIZE(s, 'ADDRESS')` | Expand directional abbreviations (N → North, NE → Northeast…), expand street type abbreviations (St → Street, Ave → Avenue, Blvd → Boulevard…), remove unit designators (Apt, Ste, #) |
| `NORMALIZE(s, 'PHONE')` | Strip all non-digit characters; remove leading country code `1` if result is 11 digits |
| `NORMALIZE(s, 'EMAIL')` | Lowercase and trim only |

```sql
-- Normalize before scoring to reduce false non-matches
SELECT SIMILARITY(
    NORMALIZE(a.company_name, 'COMPANY'),
    NORMALIZE(b.company_name, 'COMPANY')
) AS score
FROM #unstructured a
CROSS JOIN #reference b
WHERE SIMILARITY(
    NORMALIZE(a.company_name, 'COMPANY'),
    NORMALIZE(b.company_name, 'COMPANY')
) > 0.80;

-- Normalize into a temp table once, score repeatedly
SELECT id, NORMALIZE(name, 'COMPANY') AS norm INTO #norm_ref FROM #reference;
```

### 16.2 `SIMILARITY` — Normalized Similarity Score

Returns a `DECIMAL` in `[0.0, 1.0]` — `1.0` means identical, `0.0` means completely unlike.

```sql
SIMILARITY(a, b)                         -- default algorithm: JAROWINKLER
SIMILARITY(a, b, 'JAROWINKLER')          -- Jaro-Winkler (best for short strings, names)
SIMILARITY(a, b, 'LEVENSHTEIN')          -- 1 − (edit_distance / max_length)
SIMILARITY(a, b, 'TRIGRAM')              -- Sørensen-Dice on character trigrams (general purpose)
SIMILARITY(a, b, 'JACCARD')             -- word-token Jaccard: |intersect| / |union|
SIMILARITY(a, b, 'TOKENSORT')           -- Jaro-Winkler after sorting tokens (handles name reversal)
```

| Algorithm | Best for | Avoid when |
| :--- | :--- | :--- |
| `JAROWINKLER` | Person names, short identifiers, prefix-heavy strings | Long strings, word-order variation |
| `LEVENSHTEIN` | Short strings with typos, product codes | Strings of very different lengths |
| `TRIGRAM` | General purpose, partial matches, longer strings | Very short strings (< 4 chars) |
| `JACCARD` | Strings where word presence matters more than order | Single-word strings |
| `TOKENSORT` | Names where first/last may be swapped | Strings that aren't name-like |

### 16.3 `LEVENSHTEIN` — Raw Edit Distance

Returns a whole-number `DECIMAL` — the minimum number of single-character insertions, deletions, or substitutions needed to transform `a` into `b`.

```sql
LEVENSHTEIN('kitten', 'sitting')  -- → 3
LEVENSHTEIN('Smith', 'Smith')     -- → 0
```

Use `LEVENSHTEIN` directly when you need the raw distance (e.g., to enforce a maximum number of changes). Use `SIMILARITY(a, b, 'LEVENSHTEIN')` when you need a normalized 0–1 score.

### 16.4 Phonetic Encoding Functions

Phonetic functions encode pronunciation rather than spelling. They enable fast exact-join blocking on phonetically similar strings.

| Function | Signature | Returns | Best for |
| :--- | :--- | :--- | :--- |
| `SOUNDEX` | `SOUNDEX(s)` | 4-character code (e.g. `'R163'`) | English names; very fast; rough |
| `METAPHONE` | `METAPHONE(s)` | Variable-length code | English; more accurate than Soundex |
| `DMETAPHONE` | `DMETAPHONE(s)` | Primary code | Multi-origin names; handles European patterns |
| `DMETAPHONE_ALT` | `DMETAPHONE_ALT(s)` | Alternate code | Join on either primary or alternate for better recall |
| `DIFFERENCE` | `DIFFERENCE(s1, s2)` | Score `0`–`4` | Quick Soundex similarity rank between two strings (`4` = identical codes) |

```sql
-- Fast phonetic blocking before expensive SIMILARITY scoring
SELECT a.*, b.*, SIMILARITY(a.name, b.name) AS score
INTO   #candidates
FROM   #dirty a
JOIN   #reference b AS METAPHONE(a.name) = METAPHONE(b.name);   -- blocking pass

SELECT *, ROW_NUMBER() OVER (PARTITION BY a_id ORDER BY score DESC) AS rank
FROM   #candidates
WHERE  score > 0.75;

-- Double Metaphone: match on primary OR alternate for better recall
SELECT a.*, b.*
FROM   #dirty a
JOIN   #reference b
    AS DMETAPHONE(a.name) = DMETAPHONE(b.name)
    OR DMETAPHONE_ALT(a.name) = DMETAPHONE(b.name)
    OR DMETAPHONE(a.name) = DMETAPHONE_ALT(b.name);
```

### 16.5 `NGRAMS` / `NGRAM_TOKENS` — Blocking Utilities

Table-valued functions that return character n-grams. Used with `CROSS APPLY` to build inverted-index blocking tables that dramatically reduce the candidate set before scoring.

```sql
NGRAMS(s, n)        -- Returns a table of n-character grams. NGRAMS('hello', 3) → 'hel', 'ell', 'llo'
NGRAM_TOKENS(s)     -- Convenience: 3-grams, space-padded, lowercased. NGRAM_TOKENS('cat') → ' ca', 'cat', 'at '
```

Both return a one-column table with column name `Value`.

```sql
-- Build a trigram inverted index on the reference side (once per session)
SELECT gram, ref_id
INTO   #ref_index
FROM   #reference r
CROSS APPLY (SELECT Value AS gram FROM NGRAM_TOKENS(r.name)) t;

-- Look up candidates for each unstructured record
SELECT DISTINCT d.id AS dirty_id, r.ref_id
INTO   #candidates
FROM   #dirty d
CROSS APPLY (SELECT Value AS gram FROM NGRAM_TOKENS(d.name)) dg
JOIN   #ref_index r ON dg.gram = r.gram;

-- Score only the candidate pairs (far fewer than a full cross join)
SELECT c.dirty_id, c.ref_id,
       SIMILARITY(d.name, r.name) AS score
FROM   #candidates c
JOIN   #dirty     d ON d.id = c.dirty_id
JOIN   #reference r ON r.id = c.ref_id
WHERE  SIMILARITY(d.name, r.name) > 0.80
ORDER  BY c.dirty_id, score DESC;
```

> **Performance note:** A raw `CROSS JOIN` + `WHERE SIMILARITY(...) > threshold` is O(n × m). With 10 k unstructured records × 100 k reference records that is 1 billion comparisons. The phonetic blocking pattern (§16.4) or the trigram inverted-index pattern above reduce that to tens or hundreds of candidates per row. `FUZZY JOIN` (see §5.4.2 of Grammar.md) automates this blocking — use it when the pipeline fits; use the manual pattern above when you need finer control over blocking strategy.

---

## 17. Data Generation Functions

These functions are used exclusively within the `GENERATE` statement to define mock data production rules.

| Function | Signature | Description |
| :--- | :--- | :--- |
| `SEQUENCE` | `SEQUENCE(start, step [, unit])` | Produces an arithmetic or temporal sequence. `unit` can be `DAY`, `MONTH`, `YEAR`. |
| `RANDOM` | `RANDOM(val1, val2, ...)` | Selects a value randomly from the provided literal list. |
| `RANDOM_INT` | `RANDOM_INT(min, max)` | Returns a random integer between `min` and `max` (inclusive). |
| `RANDOM_DECIMAL` | `RANDOM_DECIMAL(min, max)` | Returns a random decimal between `min` and `max`. |

*Example:*
```sql
GENERATE 10 ROWS INTO #test AS (
    id   = 'SEQUENCE(1, 1)',
    cat  = 'RANDOM(A, B, C)',
    amt  = 'RANDOM_DECIMAL(10.5, 99.9)'
);
```
