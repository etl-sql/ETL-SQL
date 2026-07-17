# Expressions and Operators


### 14.1 Arithmetic Operators
`+` (Add), `-` (Subtract), `*` (Multiply), `/` (Divide), `%` (Modulo)

### 14.2 Logical Operators
`AND`, `OR`, `NOT`

### 14.3 Comparison Operators
`=`, `<>`, `!=`, `<`, `<=`, `>`, `>=`, `IN`, `LIKE`, `ILIKE`, `~`, `~*`, `BETWEEN`, `IS [NOT] NULL`, `IS [NOT] DISTINCT FROM`

### 14.4 Null-Coalescing Shorthand `??`
`a ?? b [?? c ...]` is ETL-SQL dialect shorthand that compiles to `COALESCE(a, b, c)` at parse time.
the engine, lineage tracking, and SQL pushdown all see a plain `COALESCE`, so scripts using `??` push
down to every connector unchanged. `CASE`/`COALESCE` remain the portable standard to teach; `??` is a
convenience.

Precedence: binds tighter than comparisons and looser than arithmetic.
`amount ?? 0 > 5` means `(amount ?? 0) > 5`, and `a + b ?? 0` means `(a + b) ?? 0`.

```sql
SELECT amount ?? 0 AS amount FROM #orders;
SELECT nickname ?? legal_name ?? '(unknown)' AS display_name FROM #people;
```

### 14.5 Arrow Conditional `=>`
`cond => value : else` is ETL-SQL dialect shorthand that compiles to
`CASE WHEN cond THEN value ELSE else END` at parse time. Chains flatten into **one** CASE with
multiple WHEN arms, evaluated top to bottom, exactly like CASE (short-circuit; universal SQL on
pushdown):

```sql
-- CASE WHEN score >= 90 THEN 'A' WHEN score >= 80 THEN 'B' ELSE 'F' END
SELECT score >= 90 => 'A' : score >= 80 => 'B' : 'F' AS grade FROM #tests;
```

Rules:
- The final `: else` branch is **required**. A dangling `cond => value` is a syntax error, never an
  implicit NULL.
- Lowest precedence (below `OR`): `a OR b => x : y` means `(a OR b) => x : y`.
- A `NULL`/UNKNOWN condition falls through to the next arm/else (standard CASE behavior).
- `CASE` remains the documented portable standard; `=>` is a convenience.

### 14.6 JSON Access Operators `->` / `->>`
PostgreSQL/MySQL/SQLite-style JSON access, compiled at parse time to the `JSON_GET` /
`JSON_GET_TEXT` functions:
- `json -> key` accesses an object field (string key) or array element (integer index; negative counts from
  the end) **as JSON**. Strings keep their quotes, so steps chain.
- `json ->> key` performs the same access **as text**. Strings are unquoted; objects/arrays are raw JSON text.

Left-associative and binding tighter than arithmetic. Null-propagating: a missing key, out-of-range
index, or invalid JSON yields `NULL`, never an error.

```sql
SELECT doc -> 'customer' -> 'address' ->> 'city' AS city FROM #orders;
SELECT doc ->> 'qty' ?? '0' AS qty FROM #orders;   -- combines with ??
SELECT '[10,20,30]' ->> -1;                        -- '30' (negative index from the end)
```

#### `BETWEEN`
Checks if a value is within an inclusive range (equivalent to `val >= start AND val <= end`).

```sql
SELECT * FROM #audit WHERE event_date BETWEEN '2024-01-01' AND '2024-06-30';
SELECT * FROM #data WHERE id NOT BETWEEN @min AND @max;
```

#### `IS [NOT] DISTINCT FROM`
Null-safe comparison that treats `NULL` as an ordinary comparable value rather than producing `UNKNOWN`. Unlike `=`/`<>`, it never yields `NULL`.

- `a IS DISTINCT FROM b` returns `TRUE` when the operands differ, **including** when exactly one is `NULL`; `FALSE` when they are equal or **both** `NULL`.
- `a IS NOT DISTINCT FROM b` is the logical negation: a null-safe equality (`NULL IS NOT DISTINCT FROM NULL` is `TRUE`).

```sql
-- Find rows whose value changed, counting NULL <-> value transitions as changes
SELECT id FROM #staging s
JOIN #target t ON s.id = t.id
WHERE s.value IS DISTINCT FROM t.value;

-- Null-safe equality (matches NULL rows, unlike `col = @p`)
SELECT * FROM #data WHERE notes IS NOT DISTINCT FROM @expected;
```

| `a` | `b` | `a IS DISTINCT FROM b` | `a IS NOT DISTINCT FROM b` |
| :-- | :-- | :--: | :--: |
| `1` | `1` | `FALSE` | `TRUE` |
| `1` | `2` | `TRUE` | `FALSE` |
| `1` | `NULL` | `TRUE` | `FALSE` |
| `NULL` | `NULL` | `FALSE` | `TRUE` |

### 14.7 Temporal Expressions

#### `AT TIME ZONE`
Converts a `DATETIME` or `DATETIMEOFFSET` expression to the target timezone. If the input has no offset, it is assumed to be **UTC**.

IANA and Windows timezone IDs are supported. Unknown IDs raise an execution error. See
[Dates, Times, and Time Zones](../dates-times/dates-times.md) for cross-platform aliases, DST behavior, and
connector storage rules.

```sql
SELECT OrderDate AT TIME ZONE 'Pacific Standard Time' AS local_time FROM #orders;

-- Using a variable for the timezone
DECLARE @tz = 'Eastern Standard Time';
SELECT SYSDATE AT TIME ZONE @tz;
```

**Common Timezone IDs (Windows):**
- `UTC`
- `Eastern Standard Time`
- `Central Standard Time`
- `Mountain Standard Time`
- `Pacific Standard Time`
- `Alaskan Standard Time`
- `Hawaiian Standard Time`
- `GMT Standard Time`
- `W. Europe Standard Time`
- `E. Europe Standard Time`
- `Tokyo Standard Time`
- `AUS Eastern Standard Time`

> [!NOTE]
> Timezone IDs are OS-dependent. On Windows, they follow the *Registry Time Zone* names. On Linux/macOS, the engine automatically attempts to map these to *IANA* names (e.g., `America/New_York`), but using the native OS names is recommended for maximum reliability.

---

