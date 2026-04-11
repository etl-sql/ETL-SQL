# ETL-SQL Standard Library Technical Reference

This document is the authoritative dictionary for all built-in functions, data types, and querying syntax within the ETL-SQL engine.

---

## 1. Supported Data Types & Precision

| Category | Types | Details |
| :--- | :--- | :--- |
| **Numeric** | `INT`, `BIGINT`, `DECIMAL(P,S)`, `FLOAT`, `MONEY` | `DECIMAL` supports up to 28-29 significant digits. |
| **Temporal** | `DATE`, `DATETIME2`, `TIME`, `DATETIMEOFFSET` | `DATETIME2` supports 100-nanosecond precision. |
| **Character** | `STRING`, `VARCHAR(N)`, `CHAR(N)`, `TEXT` | `(N)` overrides determine fixed-width field offsets. |
| **Logical** | `BIT`, `BOOLEAN`, `BOOL` | `TRUE`, `FALSE`, `1`, `0` are all valid literals. |
| **Binary** | `VARBINARY`, `BINARY`, `IMAGE`, `BLOB` | Cast to/from Base64 or Hex (0x...) |
| **Special** | `LIST`, `PATH`, `UUID`, `ENCRYPTED`, `ANY` | `ANY` is the default for dynamic inference. |

---

## 2. String & Regex Functions

### 2.1 Basic Transformations
- **`LEN(@str)`** -> `INT`: Character count.
- **`UPPER(@str)`** / **`LOWER(@str)`** -> `STRING`: Case conversion.
- **`INITCAP(@str)`** -> `STRING`: Capitalizes the first letter of each word.
- **`REVERSE(@str)`** -> `STRING`: Reverses character order.
- **`TRIM(@str)`** -> `STRING`: Removes leading/trailing spaces.
- **`LTRIM(@str)`** / **`RTRIM(@str)`**: Leading or trailing only.
- **`TRANSLATE(@str, @from, @to)`**: Replaces characters in `@from` with corresponding `@to`.

### 2.2 Slicing & Search
- **`SUBSTRING(@str, @start, [@len])`** -> `STRING`: 1-indexed slicing.
- **`POSITION(@sub IN @str)`** / **`INSTR(@str, @sub)`** -> `INT`: 1-indexed position.
- **`PATINDEX(@pattern, @str)`** -> `INT`: 1-based start of a wildcard pattern match.
- **`OVERLAY(@str PLACING @new FROM @start [FOR @len])`**: Replaces a range.
- **`QUOTENAME(@str, [@char])`**: Returns a delimited identifier (default `[]`).

### 2.3 Regex Suite
- **`REGEXP_LIKE(@str, @pattern, [@flags])`** -> `BIT`: Standard PCRE matching.
- **`REGEXP_SUBSTR(@str, @pattern, [@pos, @occ])`** -> `STRING`: Extracts matching substring.
- **`REGEXP_REPLACE(@str, @pattern, @new, [@pos, @occ])`** -> `STRING`: Standard replacement.
- **`REGEXP_COUNT(@str, @pattern)`** -> `INT`: Number of matches found.
- **`REGEXP_SPLIT_TO_TABLE(@str, @pattern)`** -> `TABLE`: Splits string into rows.

---

## 3. Mathematical & Statistical Functions

### 3.1 Scalar Math
- **`ABS(@n)`, `CEILING(@n)`, `FLOOR(@n)`, `ROUND(@n, @d)`, `SIGN(@n)`**.
- **`POWER(@base, @exp)`, `SQRT(@n)`, `EXP(@n)`, `LOG(@n)`, `LOG10(@n)`, `MOD(@n, @d)`**.
- **`RAND([@seed])`**: Pseudo-random float [0,1].
- **Trigonometry**: `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`, `ATAN2(y, x)`. (Inputs/Outputs in **Radians**).

### 3.2 Advanced Aggregates
- **`STDEV(@col)`** / **`STDEVP(@col)`**: Sample or Population Standard Deviation.
- **`VAR(@col)`** / **`VARP(@col)`**: Sample or Population Variance.
- **`COVAR_SAMP(@x, @y)`** / **`COVAR_POP(@x, @y)`**: Statistical Covariance.
- **`CORR(@x, @y)`**: Pearson correlation coefficient (-1.0 to 1.0).
- **`STRING_AGG(@col, @sep) [WITHIN GROUP (ORDER BY @col)]`**: String concatenation.

---

## 4. Temporal (Date & Time) Functions

- **`GETDATE()`** / **`NOW()`** -> `DATETIME`: Current system time.
- **`DATEADD(@part, @n, @date)`**: Adds interval (YEAR, MONTH, DAY, HOUR, etc.).
- **`DATEDIFF(@part, @start, @end)`**: Count of boundaries crossed.
- **`DATENAME(@part, @date)`** / **`DATEPART(@part, @date)`**: String or Int component.
- **`EOMONTH(@date, [@months])`**: Last day of the month.
- **`DATETIMEFROMPARTS(y, m, d, h, mi, s, ms)`**: Constructs a datetime object.
- **`@dt AT TIME ZONE '<tz_id>'`**: Converts between time zones (e.g., 'UTC', 'Central Standard Time').

---

## 5. Window Functions

Window functions operate over the `OVER()` clause with optional partitioning and framing.

### 5.1 Ranking
- **`ROW_NUMBER()`**: Sequential integer per partition.
- **`RANK()`** / **`DENSE_RANK()`**: Rank with/without gaps for ties.
- **`NTILE(@n)`**: Distributes rows into `@n` buckets.

### 5.2 Analytic & Distribution
- **`LAG(@col, [@off, @def])`** / **`LEAD(@col, [@off, @def])`**: Access preceding/following rows.
- **`FIRST_VALUE(@col)`** / **`LAST_VALUE(@col)`**: First/Last in ordered set.
- **`CUME_DIST()`** / **`PERCENT_RANK()`**: Distribution percentiles.
- **`PERCENTILE_CONT(@n) WITHIN GROUP (ORDER BY @col)`**: Continuous percentile.

### 5.3 Framing (`ROWS` | `RANGE`)
```sql
SUM(Amount) OVER(ORDER BY Date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
```

---

## 6. Structured Data (JSON & XML)

- **`JSON_VALUE(@json, @path)`** / **`JSON_QUERY(@json, @path)`**: Extract scalar vs. object.
- **`JSON_MODIFY(@json, @path, @val)`**: Updates/inserts into JSON.
- **`JSON_TABLE(@json, @path)`** / **`OPENJSON(@json)`**: Expands JSON to table.
- **`XMLVALUE(@xml, @xpath)`** / **`XMLQUERY(@xml, @xpath)`**.
- **`XMLELEMENT`, `XMLATTRIBUTES`, `XMLFOREST`**: XML construction suite.

---

## 7. System & Logical Functions

- **`CAST(@expr AS @type)`** / **`TRY_CAST(@expr AS @type)`**: Standard vs. Safe conversion.
- **`COALESCE(@v1, @v2, ...)`**: First non-null.
- **`NULLIF(@v1, @v2)`**: Returns NULL if equal.
- **`IIF(@cond, @t, @f)`**: Inline conditional.
- **`CHECKSUM(@v1, ...)`** / **`HASHBYTES('algo', @v1)`**: Data hashing (MD5, SHA1, SHA256, SHA512).
- **`NEWID()`** / **`NEWSEQUENTIALID()`**: UUID v7 generation.

---
*Refer to [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for syntax rules and [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) for Automation tasks.*
