# ETL-SQL Standard Library & Data Types Reference

This document is the authoritative dictionary for all built-in types, casting behaviors, and scalar functions within the ETL-SQL engine. It provides concrete examples for leveraging the standard library to transform data in-memory.

---

## 1. Supported Data Types & Precision

ETL-SQL supports a wide range of types tailored for ETL operations. The engine automatically infers types (`ANY`) when not explicitly defined, but strict types are recommended for mission-critical calculations.

### 1.1 Numeric Types
- **`TINYINT` / `SMALLINT` / `INT` / `BIGINT`**: Standard integer types (1, 2, 4, 8 bytes).
- **`DECIMAL(P,S)` / `NUMERIC`**: 16 bytes. Fixed precision up to 28-29 significant digits. Highly recommended for exact math over floats.
  - *Precision (P)*: The maximum total number of decimal digits.
  - *Scale (S)*: The number of digits stored to the right of the decimal.
- **`FLOAT` / `REAL`**: Approximate floating-point types.
- **`MONEY`**: 8-byte fixed-precision decimal optimized for currency.

### 1.2 Temporal Types (Date & Time)
- **`DATE` / `TIME` / `DATETIME` / `DATETIME2` / `DATETIMEOFFSET`**
- **Precision**: High-resolution tracking down to **100 nanoseconds** (0.0001 ms) via `DATETIME2`.
- **Constants**: `SYSDATE`, `CURRENT_TIMESTAMP`, `GETDATE()`, `NOW()`.

### 1.3 Character & Logical Types
- **`STRING` / `VARCHAR(N)` / `NVARCHAR(N)` / `TEXT` / `CHAR(N)`**: Default text processing is Unicode. `(N)` dictates maximum length.
- **`BIT` / `BOOLEAN` / `BOOL`**: Maps to `TRUE` (1) or `FALSE` (0).
- **`VARBINARY` / `BINARY` / `BLOB`**: Binary payloads. Easily cast to Base64 or Hex (`0x...`).

### 1.4 Specialized Types
- **`LIST`**: An ordered collection. Use with `IN` or `FOREACH` loops.
- **`PATH`**: Normalizes local system paths.
- **`ENCRYPTED`**: Dedicated type for connection strings or secrets (`'ENC:U2FsdGVkX1+...'`).
- **`UUID`**: Generates v4/v7 IDs (`NEWID()`).
- **`JSON` / `XML` / `VECTOR`**: Document mapping structures.

---

## 2. Type Conversion (Casting)

ETL-SQL provides two primary functions for converting values between data types.

- **`CAST(expression AS type)`**: Strict conversion. Will throw an `ExecutionException` if conversion fails.
- **`TRY_CAST(expression AS type)`**: Soft conversion. Will return `NULL` if conversion fails, rather than crashing the script. Ideal for dirty source data.

### Comprehensive Cast Examples
```sql
-- NUMERIC
SELECT CAST('123.456' AS DECIMAL(5,2)) AS d;     -- Returns 123.46 (rounds)
SELECT CAST('100.00' AS MONEY)         AS m;      -- Currency formatting

-- TEMPORAL
SELECT CAST('2026-04-08' AS DATE)         AS dt;   -- 2026-04-08
SELECT CAST(GETDATE() AS NVARCHAR)        AS str;  -- '2026-04-08 08:30:00'

-- BINARY & HEX
SELECT CAST('SGVsbG8=' AS VARBINARY)      AS b;    -- 0x48656C6C6F ('Hello')
SELECT CAST(0x48656C6C6F AS STRING)       AS s;    -- 'Hello'

-- LOGICAL & SPECIALIZED
SELECT CAST('TRUE' AS BOOL)               AS b1;   -- TRUE
SELECT CAST('C:\Temp\' AS PATH)           AS p;    -- Normalized path object
SELECT CAST('{"id": 1}' AS JSON)          AS j;    -- Parsable JSON
```

---

## 3. String & Regex Functions

The string suite enables deep cleansing of data directly during the extract or stage phase.

### 3.1 Basic Transformations
- **`LEN(@str)`**: Character count.
- **`UPPER` / `LOWER` / `INITCAP`**: Case conversions.
- **`TRIM` / `LTRIM` / `RTRIM`**: Whitespace removal.
- **`TRANSLATE(@str, @from, @to)`**: Replaces characters map-to-map.
- **`SUBSTRING(@str, @start, [@len])`**: 1-indexed slicing.
- **`POSITION(@sub IN @str)`** / **`INSTR(@str, @sub)`**: 1-indexed search.
- **`PATINDEX(@pattern, @str)`**: SQL Wildcard position search.

*Example:*
```sql
-- Clean up a dirty name field: removes spaces, proper cases
SELECT INITCAP(TRIM(NameField)) FROM #staging;
```

### 3.2 The Regex Suite
Standard PCRE (Perl Compatible Regular Expressions) are fully supported.
- **`REGEXP_LIKE(@str, @pattern)`**: Boolean match test.
- **`REGEXP_SUBSTR(@str, @pattern)`**: Extracts the exact match.
- **`REGEXP_REPLACE(@str, @pattern, @new)`**: Pattern replacement.
- **`REGEXP_SPLIT_TO_TABLE(@str, @pattern)`**: Returns a single-column table of splits.

*Example:*
```sql
-- Extract a phone number from a text block, or replace digits
SELECT 
    REGEXP_SUBSTR(Notes, '\(\d{3}\) \d{3}-\d{4}') AS PhoneFound,
    REGEXP_REPLACE(Notes, '\d', '*') AS RedactedNotes
FROM #comments;
```

---

## 4. Temporal (Date & Time) Functions

Advanced Date manipulation, calculation, and timezone translation.

- **`DATEADD(@part, @n, @date)`**: Adds interval (`YEAR`, `MONTH`, `DAY`, `HOUR`).
- **`DATEDIFF(@part, @start, @end)`**: Returns interval crossed.
- **`DATENAME(@part, @date)`**: String portion (e.g., 'Tuesday').
- **`EOMONTH(@date)`**: Last day of the month.
- **`AT TIME ZONE '<timezone_id>'`**: Automatic translation between local and global times. Common IDs: `'UTC'`, `'Eastern Standard Time'`, `'Pacific Standard Time'`.

*Example:*
```sql
-- Convert current UTC execution time to Eastern Standard Time
DECLARE @nyTime = GETDATE() AT TIME ZONE 'Eastern Standard Time';

-- Find customers created in the last 7 days
SELECT * FROM Customers 
WHERE CreatedDate >= DATEADD(DAY, -7, GETDATE());
```

---

## 5. Mathematical & Statistical Functions

### 5.1 Scalar Math
- **`ABS`**, **`CEILING`**, **`FLOOR`**, **`ROUND(@n, @d)`**, **`SIGN`**.
- **`POWER`**, **`SQRT`**, **`EXP`**, **`LOG`**, **`LOG10`**, **`MOD(@n, @d)`**.
- **`RAND([@seed])`**: Pseudo-random float [0,1].
- **Trig**: `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`, `ATAN2` (Calculated in Radians).

### 5.2 Statistical Aggregates
Advanced aggregates ignore `NULL` values. For paired functions, rows are excluded if either is `NULL`.
- **`VAR` / `VARP`**: Sample / Population Variance.
- **`STDEV` / `STDEVP`**: Sample / Population Standard Deviation.
- **`COVAR_SAMP` / `COVAR_POP`**: Covariance of two values.
- **`CORR(x, y)`**: Pearson correlation coefficient (-1.0 to 1.0).

*Example:*
```sql
SELECT 
    AVG(Price) AS AvgPrice,
    STDEV(Price) AS PriceVolatility,
    CORR(Price, Quantity) AS PriceQuantityCorr
FROM #MarketData;
```

---

## 6. Windowing & Structural Functions

### 6.1 Window Functions
Operate across an `OVER(PARTITION BY ... ORDER BY ...)` clause.
- **Ranking**: `ROW_NUMBER()`, `RANK()`, `DENSE_RANK()`, `NTILE(@n)`.
- **Analytic**: `LAG(@col)`, `LEAD(@col)`, `FIRST_VALUE(@col)`, `LAST_VALUE(@col)`.
- **Distribution**: `CUME_DIST()`, `PERCENT_RANK()`, `PERCENTILE_CONT(@n)`.

*Example:*
```sql
-- Get the rolling sum of sales, and the previous day's sales
SELECT 
    Date,
    Amount,
    SUM(Amount) OVER(ORDER BY Date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RollingTotal,
    LAG(Amount, 1, 0) OVER(ORDER BY Date) AS PreviousDayAmount
FROM #DailySales;
```

### 6.2 System & Logical Nulls
- **`COALESCE(@v1, @v2, ...)`**: Returns the first non-null input. Extremely useful during mapping.
- **`NULLIF(@v1, @v2)`**: Returns `NULL` if the two inputs match exactly.
- **`IIF(@cond, @t, @f)`**: Inline conditional return.

*Example:*
```sql
-- Default to 'Unknown' if the string is empty or null
SELECT COALESCE(NULLIF(TRIM(Region), ''), 'Unknown') FROM #staging;
```

### 6.3 Hashing
- **`CHECKSUM(@v1, ...)`**: Generates an INT checksum. Useful for change-data-capture row comparison.
- **`HASHBYTES('algo', @v1)`**: Generates cryptographic hashes (`MD5`, `SHA1`, `SHA2_256`, `SHA2_512`).
