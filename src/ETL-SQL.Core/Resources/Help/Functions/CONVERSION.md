Type conversion and hashing functions.

Casting:
  CAST(expr AS type)              — strict conversion; raises error on failure
  TRY_CAST(expr AS type)          — returns NULL instead of error on failure
  CONVERT(type, expr [, style])   — SQL Server-style; optional style code for dates
  TRY_CONVERT(type, expr [, sty]) — NULL on failure
  PARSE(s AS type [USING culture])
                                  — parse string with optional culture (e.g. 'en-US')
  TRY_PARSE(s AS type [USING culture])
                                  — NULL on failure

String shorthand:
  TO_STR(n)                       — convert any value to its string representation

Hashing (non-cryptographic unless noted):
  HASHBYTES('algorithm', expr)    — cryptographic hash; returns varbinary
    Algorithms: SHA2_256, SHA2_512, MD5, SHA1
  CHECKSUM(col1, col2, ...)       — fast non-cryptographic integer hash; use for change detection
  BINARY_CHECKSUM(col1, ...)      — binary-comparison checksum (case-sensitive)

Common date style codes for CONVERT:
  101  MM/DD/YYYY       103  DD/MM/YYYY       104  DD.MM.YYYY
  110  MM-DD-YYYY       112  YYYYMMDD         120  YYYY-MM-DD HH:MI:SS
  126  ISO 8601         127  ISO 8601 with tz

```sql
-- Safe string-to-int conversion
SELECT TRY_CAST(user_input AS INT) AS id FROM #form;

-- Date parsing with known format
SELECT CONVERT(DATE, '15/03/2025', 103) AS parsed_date;

-- Culture-aware parse
SELECT PARSE('15 mars 2025' AS DATE USING 'fr-FR') AS date_fr;

-- Detect changed rows by comparing checksums
SELECT id
FROM #source s
JOIN #target t ON s.id = t.id
WHERE CHECKSUM(s.name, s.email) <> CHECKSUM(t.name, t.email);

-- SHA-256 hash of a value (for pseudonymisation)
SELECT CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', email), 2) AS email_hash
FROM #users;

-- Explicit type widening
SELECT CAST(quantity AS DECIMAL(10,2)) / CAST(capacity AS DECIMAL(10,2)) AS fill_rate
FROM #tanks;

-- Any-to-string
SELECT TO_STR(GETDATE()) AS ts_label FROM #events;
```
