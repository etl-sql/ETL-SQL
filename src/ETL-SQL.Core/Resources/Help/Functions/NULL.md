NULL Handling and Conditional Functions
=======================================

NULL is the absence of a value — it propagates through arithmetic and comparisons.

Null Substitution
-----------------
  ISNULL(expr, replacement)          Return replacement if expr is NULL; otherwise expr.
  IFNULL(expr, replacement)          Alias for ISNULL.
  NVL(expr, replacement)             Oracle-style alias for ISNULL.
  COALESCE(a, b, c, ...)             Return the first non-NULL argument; accepts any number.
  NULLIF(a, b)                       Return NULL if a equals b; otherwise return a.

```sql
-- Replace NULL notes with a default
SELECT id, ISNULL(note, 'No notes') AS note FROM #records;

-- First available contact method
SELECT COALESCE(mobile, home_phone, work_phone, 'No contact') AS phone
FROM #customers;

-- Avoid division by zero
SELECT sales / NULLIF(visits, 0) AS conversion_rate FROM #traffic;

-- COALESCE vs ISNULL: COALESCE accepts any number of args
SELECT COALESCE(a, b, c, d, 0) FROM #t;   -- returns first non-NULL
SELECT ISNULL(a, 0) FROM #t;               -- only two args
```

Extended Oracle Substitution
-----------------------------
  NVL2(expr, if_not_null, if_null)   Return if_not_null when expr is not NULL, else if_null.

```sql
SELECT NVL2(email, 'Has email', 'No email') AS email_status FROM #contacts;
```

Inline Conditionals
--------------------
  IIF(condition, true_val, false_val)
      Return true_val if condition is true, else false_val.
      Equivalent to: CASE WHEN condition THEN true_val ELSE false_val END

  DECODE(val, s1, r1, s2, r2, ..., default)
      Return rN where val matches sN; return default if no match.
      Equivalent to a CASE expression with equality tests.

```sql
SELECT IIF(score >= 90, 'Pass', 'Fail') AS result FROM #results;

SELECT DECODE(status, 'A', 'Active', 'I', 'Inactive', 'Unknown') AS label
FROM #accounts;
```

NULL Predicate Functions
-------------------------
  IS_NULL(expr)         Return TRUE if expr is NULL (function form of IS NULL).
  IS_NOT_NULL(expr)     Return TRUE if expr is not NULL.

```sql
SELECT * FROM #data WHERE IS_NULL(value) = TRUE;
-- equivalent to: WHERE value IS NULL
```

Minimum / Maximum (NULL-ignoring)
-----------------------------------
  GREATEST(v1, v2, ...)   Return the largest value; NULLs are ignored.
  LEAST(v1, v2, ...)      Return the smallest value; NULLs are ignored.

```sql
SELECT GREATEST(10, NULL, 30, 20)   -- 30
SELECT LEAST(10, NULL, 30, 20)      -- 10
```

Predicates (use in WHERE / HAVING / CASE)
------------------------------------------
  col IS NULL
  col IS NOT NULL

NULL Propagation Rules
-----------------------
  - NULL + anything = NULL
  - NULL = NULL    → unknown (use IS NULL, not = NULL)
  - COUNT(*) counts NULLs; COUNT(col) excludes them
  - Aggregate functions (SUM, AVG, MIN, MAX) ignore NULLs

```sql
-- Filter rows where email is missing
SELECT * FROM #contacts WHERE email IS NULL;

-- Safe average that treats NULLs as zero
SELECT AVG(ISNULL(score, 0)) FROM #tests;
```
