NULL handling functions. NULL is the absence of a value — it propagates through arithmetic and comparisons.

Substitution:
  ISNULL(expr, replacement)     — return replacement if expr is NULL; otherwise expr
  NVL(expr, replacement)        — Oracle-style alias for ISNULL
  COALESCE(a, b, c, ...)        — return the first non-NULL argument; accepts any number
  NULLIF(a, b)                  — return NULL if a = b; otherwise return a

Inline conditional:
  IIF(condition, true_val, false_val)
                                — return true_val if condition is true, else false_val
                                  Short for: CASE WHEN condition THEN true_val ELSE false_val END

Predicates (use in WHERE / HAVING / CASE):
  col IS NULL
  col IS NOT NULL

NULL propagation rules:
  - NULL + anything = NULL
  - NULL = NULL    → unknown (use IS NULL, not = NULL)
  - COUNT(*) counts NULLs; COUNT(col) excludes them
  - Aggregate functions (SUM, AVG, MIN, MAX) ignore NULLs

```sql
-- Replace NULL notes with a default
SELECT id, ISNULL(note, 'No notes') AS note FROM #records;

-- First available contact method
SELECT COALESCE(mobile, home_phone, work_phone, 'No contact') AS phone
FROM #customers;

-- Avoid division by zero
SELECT sales / NULLIF(visits, 0) AS conversion_rate FROM #traffic;

-- Conditional without CASE
SELECT IIF(score >= 90, 'Pass', 'Fail') AS result FROM #results;

-- Filter rows where email is missing
SELECT * FROM #contacts WHERE email IS NULL;

-- Safe average that treats NULLs as zero
SELECT AVG(ISNULL(score, 0)) FROM #tests;

-- COALESCE vs ISNULL: COALESCE accepts any number of args
SELECT COALESCE(a, b, c, d, 0) FROM #t;   -- returns first non-NULL
SELECT ISNULL(a, 0) FROM #t;               -- only two args
```
