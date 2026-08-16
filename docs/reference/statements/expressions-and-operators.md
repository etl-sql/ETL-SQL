# Expressions and Operators

ETL-SQL provides a rich set of scalar operators, standard logical and comparison expressions, JSON access operators, and dialect shorthands for null handling and conditional branching.

---

## Operator Precedence

Operators are evaluated in order of precedence from highest to lowest. Use parentheses `()` to explicitly control evaluation order.

| Precedence | Category | Operators | Associativity | Description |
| :--- | :--- | :--- | :--- | :--- |
| **1 (Highest)** | Primary / Grouping | `()`, literals, identifiers | Left-to-right | Grouping, functions, literals |
| **2** | JSON Access | `->`, `->>` | Left-to-right | JSON field & text extraction |
| **3** | Unary | `+`, `-`, `~`, `NOT` (prefix) | Right-to-left | Unary plus/minus, bitwise NOT |
| **4** | Multiplicative | `*`, `/`, `%` | Left-to-right | Multiplication, division, modulo |
| **5** | Additive & String | `+`, `-` | Left-to-right | Addition, subtraction, string concatenation |
| **6** | Bitwise Shifts | `<<`, `>>` | Left-to-right | Left shift, right shift |
| **7** | Null-Coalescing | `??` | Left-to-right | First non-NULL fallback (lowers to `COALESCE`) |
| **8** | Comparison & Test | `=`, `<>`, `!=`, `<`, `<=`, `>`, `>=`, `BETWEEN`, `IN`, `LIKE`, `ILIKE`, `~`, `~*`, `IS NULL`, `IS DISTINCT FROM` | Left-to-right | Value comparisons and pattern matches |
| **9** | Logical NOT | `NOT` | Right-to-left | Logical negation |
| **10** | Logical AND | `AND` | Left-to-right | Logical conjunction |
| **11** | Logical OR | `OR` | Left-to-right | Logical disjunction |
| **12 (Lowest)** | Arrow Conditional | `=>` ... `:` | Left-to-right | Conditional ternary/branching (lowers to `CASE`) |

---

## Arithmetic Operators

Standard mathematical operators support integer, decimal, and floating-point numeric operands:

- **`+` (Addition / String Concatenation)** — Adds two numbers, or concatenates strings.
- **`-` (Subtraction)** — Subtracts right operand from left.
- **`*` (Multiplication)** — Multiplies two numbers.
- **`/` (Division)** — Divides left operand by right.
- **`%` (Modulo)** — Returns the remainder of integer division.

```sql
SELECT 
    10 + 5    AS sum_val,
    10 - 5    AS diff_val,
    10 * 5    AS prod_val,
    10 / 4.0  AS div_val,
    10 % 3    AS mod_val;

-- String concatenation
SELECT 'User: ' + username AS display_title FROM #users;
```

---

## Comparison Operators

Comparison operators evaluate expressions and return boolean values (`TRUE`, `FALSE`, or `UNKNOWN` when `NULL` is involved).

- **`=` / `==`** — Equality test.
- **`<>` / `!=`** — Inequality test.
- **`<` / `<=`** — Less than / Less than or equal to.
- **`>` / `>=`** — Greater than / Greater than or equal to.
- **`BETWEEN start AND end`** — Inclusive range test (`val >= start AND val <= end`).
- **`NOT BETWEEN start AND end`** — Outside range test.
- **`IN (val1, val2, ...)` / `IN (subquery)`** — Set membership test.
- **`LIKE pattern` / `ILIKE pattern`** — Wildcard matching (`%`, `_`). `ILIKE` is case-insensitive.
- **`~ pattern` / `~* pattern`** — Regular expression matching. `~*` is case-insensitive regex.
- **`IS [NOT] NULL`** — Tests whether an expression evaluates to `NULL`.
- **`IS [NOT] DISTINCT FROM`** — Null-safe equality comparison treating `NULL` as a comparable value (never yields `NULL`).

### `BETWEEN` Examples

```sql
SELECT * FROM #audit WHERE event_date BETWEEN '2026-01-01' AND '2026-06-30';
SELECT * FROM #data WHERE id NOT BETWEEN @min AND @max;
```

### `IS [NOT] DISTINCT FROM` Comparison Table

| `a` | `b` | `a = b` | `a IS DISTINCT FROM b` | `a IS NOT DISTINCT FROM b` |
| :-- | :-- | :--: | :--: | :--: |
| `1` | `1` | `TRUE` | `FALSE` | `TRUE` |
| `1` | `2` | `FALSE` | `TRUE` | `FALSE` |
| `1` | `NULL` | `UNKNOWN` | `TRUE` | `FALSE` |
| `NULL` | `NULL` | `UNKNOWN` | `FALSE` | `TRUE` |

```sql
-- Find rows whose value changed, counting transitions to/from NULL as changes
SELECT s.id FROM #staging s
JOIN #target t ON s.id = t.id
WHERE s.value IS DISTINCT FROM t.value;
```

---

## Logical Operators

- **`NOT`** — Inverts a boolean condition (`NOT TRUE` → `FALSE`).
- **`AND`** — Evaluates to `TRUE` only if both conditions are `TRUE`.
- **`OR`** — Evaluates to `TRUE` if either condition is `TRUE`.

---

## Null-Coalescing Operator (`??`)

`a ?? b [?? c ...]` is ETL-SQL dialect shorthand that compiles directly to **`COALESCE(a, b, c)`** at parse time. 

Because it lowers to standard `COALESCE`, SQL pushdown, lineage tracking, and the engine evaluator see universal SQL without dialect lock-in.

### Precedence & Associativity
- Evaluated left-to-right.
- Binds **tighter than comparisons** (`amount ?? 0 > 5` parses as `(amount ?? 0) > 5`).
- Binds **looser than arithmetic** (`a + b ?? 0` parses as `(a + b) ?? 0`).

### Examples

```sql
-- Default NULL amounts to zero
SELECT order_id, amount ?? 0.00 AS final_amount FROM #orders;

-- Chain multiple fallbacks
SELECT user_id, nickname ?? preferred_name ?? legal_name ?? '(anonymous)' AS display_name
FROM #profiles;

-- Use in WHERE filters and arithmetic expressions
SELECT * FROM #inventory WHERE (quantity_on_hand ?? 0) - (allocated_units ?? 0) > 10;
```

---

## Arrow Conditional (`=>` and `:`)

`cond => true_val : else_val` is ETL-SQL dialect shorthand that compiles directly to **`CASE WHEN cond THEN true_val ELSE else_val END`** at parse time.

Chains of arrow conditionals flatten into **one** multi-branch `CASE` expression with multiple `WHEN` arms.

### Syntax & Rules
- **Ternary Form**: `condition => true_value : else_value`
- **Chained Form**: `cond1 => val1 : cond2 => val2 : cond3 => val3 : else_val`
- **Mandatory Else**: The trailing `: else_val` branch is **required**. A dangling `cond => val` without `: else` is a syntax error (prevents unintended `NULL` values).
- **Lowest Precedence**: Evaluated below `OR`, so `a OR b => x : y` parses as `(a OR b) => x : y`.
- **Short-Circuit Evaluation**: Untaken branches are never evaluated (e.g., `x = 0 => 0 : 1 / x` will not cause divide-by-zero).

### Examples

```sql
-- Simple two-branch ternary
SELECT user_id, is_active => 'Active' : 'Disabled' AS status FROM #users;

-- Multi-branch conditional chain (flattens into one CASE expression)
SELECT 
    score >= 90 => 'Grade A'
  : score >= 80 => 'Grade B'
  : score >= 70 => 'Grade C'
  : 'Grade F' AS final_grade
FROM #student_scores;

-- Safe from runtime evaluation errors on untaken branches
SELECT total_items > 0 => total_revenue / total_items : 0.00 AS avg_price FROM #summary;
```

---

## JSON Access Operators (`->` and `->>`)

PostgreSQL/MySQL/SQLite-style JSON path traversal operators that compile at parse time to **`JSON_GET`** and **`JSON_GET_TEXT`** functions:

- **`json -> key`** — Extracts an object property (string key) or array element (integer index) **as JSON**. Preserves quotes and JSON types so steps chain.
- **`json ->> key`** — Extracts the value **as unquoted plain text**.
- **Negative Indices**: Supported for array indexing (`-1` selects the last element).
- **Null Safety**: Accessing nonexistent keys, out-of-bounds indices, or invalid JSON returns `NULL` rather than throwing an error.

### Examples

```sql
-- Chain object traversal and extract final field as text
SELECT payload -> 'customer' -> 'address' ->> 'city' AS customer_city FROM #raw_events;

-- Array element access (positive and negative indices)
SELECT 
    tags ->> 0  AS first_tag,
    tags ->> -1 AS last_tag
FROM #posts;

-- Combine JSON extraction with ?? null-coalescing
SELECT payload ->> 'country_code' ?? 'US' AS country FROM #webhooks;
```

---

## String Variable Interpolation (`${@var}` / `${var}`)

String literals support inline variable interpolation using `${@varName}` or `${varName}` syntax. During evaluation, matching `${...}` placeholders are replaced with the variable's runtime value.

```sql
DECLARE @date_str VARCHAR = '2026-08-16';
DECLARE @filename VARCHAR = 'export_${@date_str}.csv';
-- Resolves @filename to 'export_2026-08-16.csv'

COPY FILE 'C:\data\input.csv' TO 'C:\data\backup\data_${date_str}.csv';
```

- Supports both `${@var}` and `${var}` formats.
- Sensitive variables declared with `PASSWORD` / `ENC:` decrypt automatically during string interpolation.
- Unmatched variables remain intact as literal strings (avoiding conflicts with regexes or external templates).

---

## Temporal Expressions & Timezones (`AT TIME ZONE`)

Converts a `DATETIME` or `DATETIMEOFFSET` expression to the target timezone. If the input has no offset, it is assumed to be **UTC**.

```sql
SELECT OrderDate AT TIME ZONE 'Pacific Standard Time' AS local_time FROM #orders;

-- Dynamic timezone from a variable
DECLARE @tz VARCHAR = 'Eastern Standard Time';
SELECT SYSDATE AT TIME ZONE @tz AS est_time;
```

---

## References

- [Statement Reference](README.md)
- [CASE Expression](query-syntax/case.md)
- [COALESCE Function](../functions/null-handler/coalesce.md)
- [JSON Functions](../functions/json-xml/README.md)
- [Syntax Index](../../syntax-index.md)
