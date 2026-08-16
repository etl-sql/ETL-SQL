# CASE

Conditional value expression that evaluates a list of conditions and returns one of multiple possible result expressions. Usable anywhere an expression is valid (`SELECT`, `WHERE`, `HAVING`, `ORDER BY`, `GROUP BY`, and `SET @variable = ...`).

---

## Syntax

### 1. Searched Form (Boolean Conditions)
```sql
CASE
  WHEN <boolean_condition_1> THEN <result_1>
  [WHEN <boolean_condition_2> THEN <result_2> ...]
  [ELSE <default_result>]
END
```

### 2. Simple Form (Exact Value Matching)
```sql
CASE <input_expression>
  WHEN <match_value_1> THEN <result_1>
  [WHEN <match_value_2> THEN <result_2> ...]
  [ELSE <default_result>]
END
```

### 3. Arrow Conditional Shorthand (`=>` and `:`)
```sql
-- Two-branch (ternary):
<condition> => <true_result> : <false_result>

-- Multi-branch chaining:
<condition_1> => <result_1>
: <condition_2> => <result_2>
: <default_result>
```

---

## Arguments & Return Types

- **`WHEN <condition>`** — Expression evaluated in top-to-bottom order. Short-circuits on the first true branch.
- **`THEN <result>`** — Output returned when the matching `WHEN` condition is satisfied.
- **`ELSE <default_result>`** — Output returned if no `WHEN` branch matches. If omitted in standard `CASE`, defaults to `NULL`. (The trailing `: <else>` branch is mandatory when using `=>` shorthand).
- **Return Type**: Inferred from the union of all `THEN` and `ELSE` branches. If branch types differ, standard numeric promotion (e.g. `INT` + `DECIMAL` &rarr; `DECIMAL`) or text coercion applies.

---

## Examples

### 1. Basic Classification & Arrow Shorthand

```sql
-- Standard searched CASE
SELECT 
    order_id,
    amount,
    CASE 
        WHEN amount >= 1000 THEN 'Platinum'
        WHEN amount >= 500  THEN 'Gold'
        WHEN amount >= 100  THEN 'Silver'
        ELSE 'Standard'
    END AS tier_label
FROM #orders;

-- Equivalent using compact arrow syntax
SELECT 
    order_id,
    amount >= 1000 => 'Platinum'
  : amount >= 500  => 'Gold'
  : amount >= 100  => 'Silver'
  : 'Standard' AS tier_label
FROM #orders;
```

### 2. Production ETL Pattern: Cross-Source Customer Hygiene & Risk Scoring

Extract dirty customer account data from Postgres, calculate compliance risk scores, and stage normalized records for warehouse loading:

```sql
CREATE CONNECTION pg AS POSTGRES(HOST='crm.internal', DATABASE='customers');
CREATE CONNECTION dw AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Extract raw customer attributes into engine memory
SELECT 
    id,
    name,
    email,
    country_code,
    failed_logins,
    account_balance,
    is_verified
INTO #raw_accounts
FROM pg.customer_records;

-- 2. Stage and enrich with multidimensional risk classification
SELECT 
    id AS customer_id,
    LOWER(TRIM(email)) AS email,
    COALESCE(country_code, 'XX') AS country_code,
    -- Compute composite risk category
    CASE 
        WHEN failed_logins > 10 OR account_balance < -500.0 THEN 'CRITICAL'
        WHEN failed_logins > 5 OR is_verified = 0          THEN 'HIGH'
        WHEN account_balance < 0.0                          THEN 'MEDIUM'
        ELSE 'LOW'
    END AS risk_tier,
    -- Assign review priority score
    (failed_logins > 5 => 50 : 0) + (is_verified = 0 => 25 : 0) AS risk_score,
    GETDATE() AS evaluated_at
INTO #staged_risk_profiles
FROM #raw_accounts;

-- 3. Load high-risk accounts into compliance monitoring table
INSERT INTO dw.dbo.ComplianceWatchlist (CustomerId, Email, RiskTier, RiskScore, EvaluatedAt)
SELECT customer_id, email, risk_tier, risk_score, evaluated_at
FROM #staged_risk_profiles
WHERE risk_tier IN ('CRITICAL', 'HIGH');
```

---

## Remarks & Null Behavior

- **Short-Circuit Evaluation**: Branches are evaluated sequentially. Subsequent branches are not executed once a match is found.
- **NULL Comparisons**: In the Simple Form (`CASE status WHEN NULL THEN ...`), equality with `NULL` always evaluates to false. Use Searched Form (`WHEN status IS NULL THEN ...`) or `IS DISTINCT FROM` when matching `NULL`s.
- **Nesting**: `CASE` expressions can be nested arbitrarily inside `THEN`, `ELSE`, or function arguments.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [Expressions and Operators](../expressions-and-operators.md)
- [COALESCE Function](../../functions/null-handler/coalesce.md)
- [IIF Function](../../functions/conversion/iif.md)
- [IS DISTINCT FROM](is-distinct-from.md)
- [ETL Cookbook: Data Quality Gate](../../../cookbooks/etl/data-quality-gate.md)
- [Syntax Index](../../../syntax-index.md)
