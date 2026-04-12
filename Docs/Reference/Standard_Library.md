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
| `DATENAME` | `DATENAME(part, date)` | String name of the date part (e.g. month → `'April'`) |
| `EXTRACT` | `EXTRACT(field FROM source)` | ANSI-style part extraction (`YEAR`, `MONTH`, `DAY`, `DOW`, `DOY`, `HOUR`, `MINUTE`, `SECOND`) |
| `YEAR` | `YEAR(date)` | Integer year |
| `MONTH` | `MONTH(date)` | Integer month (1–12) |
| `DAY` | `DAY(date)` | Integer day of month |
| `EOMONTH` | `EOMONTH(date [, months])` | Last day of the month; optionally offset by N months |
| `ISDATE` | `ISDATE(expr)` | `1` if parseable as date, `0` otherwise |
| `DATETIMEFROMPARTS` | `DATETIMEFROMPARTS(y, m, d, h, mi, s, ms)` | Constructs `DATETIME` from component parts |
| `TIMEFROMPARTS` | `TIMEFROMPARTS(h, mi, s, frac, prec)` | Constructs `TIME` from parts |
| `TRUNC` / `TO_DATE` | `TRUNC(datetime)` | Truncates the time portion; returns date |
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

---

## 6. Statistical Aggregates

For paired functions (`CORR`, `COVAR_*`), rows where either input is `NULL` are excluded from the calculation.

| Function | Returns |
| :--- | :--- |
| `SUM(col)` | Total sum |
| `AVG(col)` | Arithmetic mean |
| `COUNT([DISTINCT] col)` | Non-null count (or distinct count) |
| `MIN(col)` / `MAX(col)` | Minimum / maximum value |
| `VAR(col)` / `VAR_SAMP(col)` | Sample variance |
| `VARP(col)` / `VAR_POP(col)` | Population variance |
| `STDEV(col)` / `STDDEV_SAMP(col)` | Sample standard deviation |
| `STDEVP(col)` / `STDDEV_POP(col)` | Population standard deviation |
| `COVAR_SAMP(x, y)` | Sample covariance |
| `COVAR_POP(x, y)` | Population covariance |
| `CORR(x, y)` | Pearson correlation coefficient (−1.0 to 1.0) |
| `STRING_AGG(col, sep) [WITHIN GROUP (ORDER BY col)]` | Rows concatenated into one string |

*Example:*
```sql
SELECT
    AVG(Price)           AS AvgPrice,
    STDEV(Price)         AS PriceVolatility,
    CORR(Price, Qty)     AS PriceQtyCorrelation,
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
| `ERROR_MESSAGE()` | Error message inside `CATCH` block |
| `@@TRANCOUNT` | Current transaction nesting level |
| `FILE_EXISTS(path)` | `TRUE` if the specified file exists on disk |
| `DIRECTORY_EXISTS(path)` | `TRUE` if the directory exists on disk |

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
| `JSON_TABLE` | `JSON_TABLE(json, path)` | Table from JSON array or object |
| `OPENJSON` | `OPENJSON(json [, path])` | SQL Server-style JSON expansion |

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
    [ORDER BY <col> [ASC|DESC]]
    [ROWS | RANGE BETWEEN <start> AND <end>]
)
```

**Frame bounds:** `UNBOUNDED PRECEDING`, `n PRECEDING`, `CURRENT ROW`, `n FOLLOWING`, `UNBOUNDED FOLLOWING`

### 13.2 Ranking Functions

| Function | Returns |
| :--- | :--- |
| `ROW_NUMBER()` | Unique sequential integer per partition, starting at 1 |
| `RANK()` | Rank with gaps (1, 1, 3, 4, …) |
| `DENSE_RANK()` | Rank without gaps (1, 1, 2, 3, …) |
| `NTILE(n)` | Bucket number (1 to n) distributed evenly |

### 13.3 Analytic Functions

| Function | Returns |
| :--- | :--- |
| `LAG(col [, offset [, default]])` | Value from a previous row |
| `LEAD(col [, offset [, default]])` | Value from a subsequent row |
| `FIRST_VALUE(col)` | First value in the window frame |
| `LAST_VALUE(col)` | Last value in the window frame |
| `NTH_VALUE(col, n)` | Value of the Nth row in the window frame |

### 13.4 Distribution Functions

| Function | Returns |
| :--- | :--- |
| `CUME_DIST()` | Cumulative distribution (0 < value ≤ 1) |
| `PERCENT_RANK()` | Relative rank as a fraction (0 ≤ value ≤ 1) |
| `PERCENTILE_CONT(n)` | Continuous percentile; uses `WITHIN GROUP (ORDER BY col)` |
| `PERCENTILE_DISC(n)` | Discrete percentile; uses `WITHIN GROUP (ORDER BY col)` |

*Examples:*
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
```

---

## 14. File System Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `FILE_EXISTS` | `FILE_EXISTS(path)` | `TRUE` if the file exists |
| `DIRECTORY_EXISTS` | `DIRECTORY_EXISTS(path)` | `TRUE` if the directory exists |
| `FILE_LIST` | `FILE_LIST(path [, recursive])` | Table: `Name`, `Path`, `Extension`, `Size`, `LastModified` |
| `REMOTE_FILE_LIST` | `REMOTE_FILE_LIST(conn_name [, path])` | Table from SFTP/FTP/Blob: `Name`, `Path`, `Size`, `LastModified` |

*Examples:*
```sql
-- Check before processing
IF FILE_EXISTS('C:\Incoming\data.csv')
BEGIN
    CREATE CONNECTION src ON FLATFILE('C:\Incoming\data.csv');
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
