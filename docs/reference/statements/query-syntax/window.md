# WINDOW
WINDOW defines reusable named window specifications for analytic functions in a `SELECT` query. Named windows avoid repeating the same `PARTITION BY`, `ORDER BY`, and frame clauses across multiple `OVER` expressions.

```sql
SELECT <columns>,
  <window_function>() OVER <window_name> AS <alias>
FROM <source>
[HAVING <condition>]
WINDOW <window_name> AS (
  [<base_window_name>]
  [PARTITION BY <expr> [, ...]]
  [ORDER BY <expr> [ASC | DESC] [, ...]]
  [ROWS | RANGE | GROUPS <frame_bound>]
)
[QUALIFY <condition>];
```

- **`OVER window_name`** - Uses the named window exactly as defined.
- **`OVER (window_name ROWS ...)`** - Starts from a named window and adds or overrides frame details.
- **`PARTITION BY`** - Splits input rows into independent window partitions.
- **`ORDER BY`** - Orders rows within each partition.
- **`ROWS`, `RANGE`, `GROUPS`** - Defines the frame used by aggregate window functions.

```sql
SELECT
  region,
  month,
  SUM(amount) OVER regional_months AS running_total
FROM #sales
WINDOW regional_months AS (PARTITION BY region ORDER BY month);
```

```sql
SELECT
  month,
  SUM(amount) OVER (ordered_months ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS rolling_total
FROM #sales
WINDOW ordered_months AS (ORDER BY month);
```

References:
- [Statements](../README.md)
