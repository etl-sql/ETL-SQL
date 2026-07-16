# ETL-SQL Expression Evaluation & Type System

This document covers the expression evaluator, operator precedence, type system, NULL propagation, function dispatch, and how expressions are evaluated over result set rows.

---

## 1. Overview

```
AST Expression node
        │
        ▼
ExpressionEvaluator.EvaluateInternal(expr, currentRow)
        │
        ├─ Literal → return value directly
        ├─ Identifier → look up in currentRow columns
        ├─ Variable → look up via VariableScopeManager
        ├─ Binary → evaluate both sides, apply operator
        ├─ Function → FunctionRegistry.ExecuteAsync() or ProcedureExecutor (UDF)
        ├─ Case → evaluate conditions left-to-right, return first match
        ├─ Subquery → ExecuteQuery(), cache result
        └─ NULL propagation throughout
```

All evaluation is **async** (`Task<object?>`). The `object?` return type holds any .NET value: `int`, `long`, `decimal`, `double`, `string`, `bool`, `DateTime`, `byte[]`, collections, or `null` / `DBNull.Value`.

---

## 2. Expression Type Dispatch

`EvaluateInternal()` dispatches by C# pattern-matching on the expression type:

| AST type | Handler method | Notes |
|----------|---------------|-------|
| `LiteralExpression` | Direct return | String, Number, Bool, Null literals |
| `IdentifierExpression` | `ResolveIdentifier()` | Column name lookup in current row |
| `VariableExpression` | `VariableScopeManager.GetVariable()` | `@var`, `@@system`, `#temp` |
| `MemberAccessExpression` | `EvaluateMemberAccess()` | `expr.Property` |
| `BinaryExpression` | `EvaluateBinary()` | All infix operators |
| `UnaryExpression` | `EvaluateUnary()` | `NOT`, unary `-` |
| `LikeExpression` | `EvaluateLikeExpr()` | SQL `LIKE` with `%` and `_` wildcards |
| `IsNullExpression` | `EvaluateIsNull()` | `IS NULL`, `IS NOT NULL` |
| `CaseExpression` | `EvaluateCase()` | `CASE WHEN … THEN … ELSE … END` |
| `InExpression` | `EvaluateIn()` | `IN (list)`, `IN (SELECT …)` |
| `ExistsExpression` | `EvaluateExists()` | `EXISTS (SELECT …)` |
| `SubqueryExpression` | `EvaluateSubquery()` | Scalar scalar subquery with caching |
| `ListExpression` | `EvaluateList()` | Inline `(1, 2, 3)` or `[a, b]` lists |
| `FunctionCallExpression` | `EvaluateFunction()` | Built-in + user-defined functions |
| `SubstringExpression` | `EvaluateSubstring()` | `SUBSTRING(s, start, len)` |
| `ExtractExpression` | `EvaluateExtract()` | `EXTRACT(YEAR FROM date)` |
| `PositionExpression` | `EvaluatePosition()` | `POSITION(sub IN str)` |
| `OverlayExpression` | `EvaluateOverlay()` | `OVERLAY(s PLACING p FROM i FOR n)` |
| `TrimExpression` | `EvaluateTrim()` | `TRIM([BOTH\|LEADING\|TRAILING] chars FROM s)` |
| `AtTimeZoneExpression` | `EvaluateAtTimeZone()` | `expr AT TIME ZONE 'tz'` |
| `CastExpression` | `EvaluateCast()` | `CAST(expr AS type)` |

---

## 3. Identifier Resolution

`ResolveIdentifier(name, row)` applies a multi-level lookup:

1. **Exact match** in `currentRow` columns (case-insensitive)
2. **Outer row stack** — walks `_context.OuterRowStack` for correlated subquery references
3. **Fuzzy match** — if `name` is unqualified (no `.`), checks if exactly one qualified column (`table.name`) matches
4. Returns `null` if nothing found — the caller then tries `VariableScopeManager.GetVariable()`

This order means column names shadow variable names within a row-processing context. To explicitly access a variable when a column has the same name, use an alias in the SELECT.

---

## 4. Operator Precedence

Precedence is encoded by the parser's recursive-descent structure (see [ParserLexer.md](ParserLexer.md) §4). At evaluation time, the AST already encodes the correct structure — the evaluator just walks it.

| Operator class | Operators | Associativity |
|----------------|-----------|---------------|
| Logical OR | `OR` | Left |
| Logical AND | `AND` | Left |
| Logical NOT | `NOT` | Right (unary) |
| Comparison | `=`, `<>`, `<`, `>`, `<=`, `>=`, `IN`, `NOT IN`, `LIKE`, `NOT LIKE`, `IS [NOT] NULL` | Left, non-associative |
| Additive | `+`, `-` | Left |
| Multiplicative | `*`, `/`, `%` | Left |
| Unary minus | `-` | Right |
| Primary | literals, calls, `()` | — |

---

## 5. Type System

### Runtime Types

| .NET type | SQL types |
|-----------|-----------|
| `int` | `INT`, `INTEGER`, `SMALLINT`, `TINYINT` |
| `long` | `BIGINT` |
| `decimal` | `DECIMAL`, `NUMERIC`, `MONEY` |
| `double` | `FLOAT`, `DOUBLE`, `REAL` |
| `bool` | `BIT`, `BOOLEAN`, `BOOL` |
| `DateTime` | `DATE`, `DATETIME`, `TIMESTAMP`, `DATETIMEOFFSET` |
| `TimeSpan` | `TIME` |
| `string` | `VARCHAR`, `NVARCHAR`, `CHAR`, `TEXT`, `JSON`, `XML`, `UUID`, `GUID`, `PATH`, `ENCRYPTED` |
| `byte[]` | `VARBINARY`, `BINARY`, `BLOB`, `IMAGE` |

### Soft Equality (`IsSoftEqual`)

Used for `=` and `<>` comparisons and `IN` membership checks. Applies the following coercion ladder before comparing:

1. Both `null` or `DBNull.Value` → `true`
2. One null → `false`
3. Both `decimal` → direct decimal comparison
4. Both `DateTime` → compare year/month/day/hour/minute/second (ignores sub-second)
5. Both parseable as `decimal` → numeric comparison
6. Both parseable as `DateTime` → DateTime comparison
7. Fallback: `string.Equals(…, OrdinalIgnoreCase)`

This means `@x = '5'` where `@x = 5` evaluates to `true` without an explicit `CAST`.

### CAST and TRY_CAST

`CAST(expr AS type)` calls `EvaluationUtils.CastToType(value, typeName)`:

```csharp
var baseType = typeName.Split('(')[0].ToUpperInvariant();  // strip VARCHAR(50) → VARCHAR
var converter = _converters[baseType];  // pre-built converter delegate
return converter(value);
```

Unsupported types fall through and return the value unchanged.

`TRY_CAST(expr AS type)` wraps the same call in a try/catch — exceptions return `null` instead of propagating.

---

## 6. NULL Propagation

The engine implements SQL three-valued logic throughout:

**Arithmetic:** Any `null` operand → `null` result.

**`AND`:** Implements short-circuit with nullable semantics:
- `false AND anything` → `false` (short-circuit)
- `null AND false` → `false`
- `null AND true` → `null`
- `null AND null` → `null`

**`OR`:** Short-circuit with nullable semantics:
- `true OR anything` → `true` (short-circuit)
- `null OR false` → `null`
- `null OR true` → `true`

**Comparisons (`=`, `<>`, etc.):** If either operand is `null`/`DBNull`, the comparison returns `null` per SQL three-valued logic. Use `IS NULL` / `IS NOT NULL` to test nullability explicitly.

**`IS NULL` / `IS NOT NULL`:** Returns `bool`, never null.

**`COALESCE(a, b, …):`** Returns the first non-null argument. Implemented as a built-in function.

---

## 7. CASE Expression

```sql
CASE
  WHEN condition1 THEN result1
  WHEN condition2 THEN result2
  ELSE default_result
END
```

Evaluation:
1. Evaluate `condition1`; if truthy return `result1`
2. Evaluate `condition2`; if truthy return `result2`
3. … (left-to-right, short-circuit)
4. Return `ElseResult` if all conditions false/null, or `null` if no `ELSE`

The searched-case form `CASE expr WHEN val1 THEN …` is compiled during parsing to `CASE WHEN expr = val1 THEN …` so the evaluator only needs one code path.

---

## 8. LIKE Pattern Matching

SQL `LIKE` wildcards are converted to regex before matching:

| SQL | Regex |
|-----|-------|
| `%` | `.*` |
| `_` | `.` |
| `ESCAPE` char | Escapes the next `%` or `_` literally |

Comparison is case-insensitive (`RegexOptions.IgnoreCase`). The full input string is matched from `^` to `$`.

---

## 9. IN and EXISTS

**`IN (list)`:** Evaluates each list item and uses `IsSoftEqual` for membership testing. Short-circuits on first match.

**`IN (SELECT …)`:** Streams the subquery via `EvaluateStream()` and tests each first-column value. Short-circuits on first match.

**`EXISTS (SELECT …)`:** Executes the subquery and returns `true` if at least one row is produced.

**`NOT IN` / `NOT EXISTS`:** Negate the above results.

---

## 10. Subquery Caching

Scalar subqueries that don't reference outer-row columns are cached in `_subqueryCache`:

```
SubqueryExpression.ToSql() → string key (structural equality)
    │
    ├─ Cache hit → return cached result, increment SubqueryCacheHits
    └─ Cache miss:
            push currentRow onto OuterRowStack (for correlated access)
            ExecuteQuery(subquery)
            take first column of first row → scalar result
            pop OuterRowStack
            cache result (if non-null, max 1000 entries)
```

For correlated subqueries (those that reference columns from the outer row), caching is bypassed because the result varies per row.

---

## 11. Function Dispatch

`EvaluateFunction()` resolution order:

1. **User-defined functions** — `VariableScopeManager.TryGetFunction(name)` → `ProcedureExecutor.EvaluateUserDefinedFunction()`
2. **Built-in function registry** — `FunctionRegistry.ExecuteAsync(name, args)`
3. **Pre-calculated aggregates** — check row for `AGG_{function_sql}` key (set by `SelectExecutionEngine` before calling evaluator on HAVING/SELECT columns)
4. **`null`** if nothing matches (evaluated silently — handler logs a warning)

### Built-in Function Categories

| Category | Examples |
|----------|----------|
| String | `UPPER`, `LOWER`, `LEN`, `SUBSTRING`, `TRIM`, `CONCAT`, `REPLACE`, `PATINDEX`, `STUFF`, `STRING_SPLIT`, `REPLICATE` |
| Math | `ABS`, `ROUND`, `CEILING`, `FLOOR`, `SQRT`, `POWER`, `SIN`, `COS`, `TAN`, `ATAN2` |
| Date/Time | `GETDATE`, `NOW`, `DATEADD`, `DATEDIFF`, `DATEPART`, `DATENAME`, `EOMONTH`, `YEAR`, `MONTH`, `DAY` |
| Aggregate | `SUM`, `AVG`, `COUNT`, `MIN`, `MAX`, `STDDEV`, `VAR` |
| Null handling | `COALESCE`, `ISNULL`, `NVL`, `NULLIF` |
| Type conversion | `CAST`, `TRY_CAST`, `FORMAT`, `STR` |
| Collections | `LENGTH`, `APPEND_TO_LIST`, `REMOVE_FROM_LIST`, `SORT_LIST`, `GENERATE_SERIES` |
| Hashing | `HASHBYTES`, `CHECKSUM`, `NEWID`, `NEWSEQUENTIALID` |
| Environment | `ENV`, `FILE_EXISTS`, `DIRECTORY_EXISTS` |
| Error context | `ERROR_NUMBER`, `ERROR_MESSAGE`, `ERROR_SEVERITY`, `ERROR_LINE` |

Functions are registered with `RegisterWithHelp(name, impl, helpText)`. The help text is used by the language server's `SignatureHelpProvider`.

---

## 12. Batch-Row Evaluation

When `SelectExecutionEngine` processes a `SELECT`, it evaluates column expressions once per row:

```
for each row in source:
    currentRow = row
    for each SelectColumn:
        value = await Evaluate(column.Expression, currentRow)
        outputRow[column.Alias] = value
```

`EvaluateStream(expr, row)` is used when an expression can yield multiple values (e.g., a list variable used as a subquery, or `STRING_SPLIT()` returning multiple rows). The engine handles these via `IAsyncEnumerable<Row>`.

**Aggregate pre-computation:** Aggregate functions (`SUM`, `COUNT`, etc.) in `SELECT` and `HAVING` are detected before per-row evaluation runs. `SelectExecutionEngine` groups rows, computes aggregates, and stores results in each group's representative row under the key `AGG_{expr.ToSql().ToUpperInvariant()}`. The per-row expression evaluator then finds these pre-computed values via the cache key lookup described in §11.

---

## 13. Date Arithmetic

Special handling in `EvaluateBinary()`:

```csharp
// DateTime + number → add days
if (left is DateTime dt && op == "+")
    return dt.AddDays(Convert.ToDouble(right));

// DateTime - number → subtract days
if (left is DateTime dt && op == "-" && right is not DateTime)
    return dt.AddDays(-Convert.ToDouble(right));

// DateTime - DateTime → days between (decimal)
if (left is DateTime dt1 && right is DateTime dt2 && op == "-")
    return (decimal)(dt1 - dt2).TotalDays;
```

For interval arithmetic (`DATEADD`, `DATEDIFF`) use the built-in functions rather than operators.

---

## 14. Adding a New Built-in Function

1. Open `ETL-SQL.Engine/Functions/StandardFunctions.cs`
2. Add a registration in the constructor or the appropriate category section:
   ```csharp
   registry.RegisterWithHelp(
       "MY_FUNC",
       async (args, ctx) => { /* implementation */ return result; },
       "MY_FUNC(arg1, arg2): Description for signature help.");
   ```
3. Add the function name to the language server's `SignatureHelpProvider` hard-coded dictionary if it takes parameters (so editors show hints)
4. Add a test in `ETL-SQL.Tests` using the standard `FunctionTests` pattern

---

## 15. Known Behaviors and Engine Quirks

These behaviors were discovered during SLT corpus authoring and are tested by the SLT suite. They match SQL standard semantics but may surprise readers expecting C#/Java arithmetic defaults.

### Division Semantics

ETL-SQL implements SQL integer division (truncation toward zero) when both operands are integer-valued:

| Expression | Result | Notes |
|------------|--------|-------|
| `7 / 2` | `3` | Both integer-valued → truncate |
| `7.0 / 2` | `3.5` | Left has fractional scale → decimal |
| `7 / 2.0` | `3.5` | Right has fractional scale → decimal |
| `-7 / 2` | `-3` | Truncation is toward zero, not floor |

**Implementation:** `BinaryOperatorFactory.MathOp` checks whether both operands convert to `decimal` with zero fractional scale. If so, it applies `Math.Truncate` before dividing. A literal `2` becomes `decimal` with scale 0; a literal `2.0` has scale 1 and stays real.

### CAST Truncation

`CAST(expr AS INT)` (and `TINYINT`, `SMALLINT`, `BIGINT`) truncates toward zero — it does not round:

```sql
SELECT CAST(3.9 AS INT)   -- 3  (truncated, not rounded to 4)
SELECT CAST(-3.9 AS INT)  -- -3 (toward zero, not floor to -4)
```

**Implementation:** `TypeConverter.Cast` applies `Math.Truncate(d)` before `Convert.ToDecimal(long)` for integer target types.

### Aggregates Nested Inside CASE / Scalar Functions

The engine correctly handles aggregates that appear inside `CASE` expressions, `COALESCE`, or other scalar wrappers:

```sql
-- All of these are correctly computed in GROUP BY context:
SELECT CASE WHEN SUM(amount) > 100 THEN 'high' ELSE 'low' END FROM t GROUP BY dept
SELECT COALESCE(AVG(score), 0) FROM t GROUP BY category
SELECT SUM(amount) * 1.1 FROM t GROUP BY region
```

**Implementation:** `AggregateEngine.ApplyAggregation` calls `CollectAggregates` recursively on each SELECT column expression, not just on top-level aggregate calls. Nested aggregate states are pre-computed and stored under the `AGG_<expr>` key; the outer expression then reads them during re-evaluation.

> **Limitation:** Aggregates inside scalar *user-defined functions* (UDFs registered via `CREATE FUNCTION`) are not pre-collected and will return NULL if used in a GROUP BY query. Only built-in expression types (CASE, COALESCE, arithmetic operators) are traversed by `CollectAggregates`.

### Three-Valued Logic and NULL Comparison

Comparison operators (`=`, `<>`, `<`, `>`, `<=`, `>=`) return `NULL` — not `false` — when either operand is `NULL`. This propagates through `AND`/`OR` per SQL three-valued logic:

| Expression | Result |
|------------|--------|
| `NULL = 1` | NULL |
| `NULL <> 1` | NULL |
| `NULL = NULL` | NULL |
| `1 = 1 AND NULL = 1` | NULL |
| `1 = 1 OR NULL = 1` | TRUE |

Use `IS NULL` / `IS NOT NULL` to test for null. `NULLIF(a, b)` returns NULL if `a = b`, otherwise `a`. Both are correctly implemented and tested in `null_edge_cases.test`.

### String Case Sensitivity

By default string comparisons (in `=`, `LIKE`, `IN`, and `ORDER BY`) are **case-insensitive**. This can be changed per-session:

```sql
SET CASE_SENSITIVE = ON;   -- comparisons become case-sensitive
SET CASE_SENSITIVE = OFF;  -- restore default
```

See the Administrators Guide §8.1 for the full interaction with connectors and `ORDER BY` collation.

