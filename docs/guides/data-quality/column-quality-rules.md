# Column Quality Rules (EXPECT / ON FAILURE)

ETL-SQL lets you declare value-level validation rules directly on column projections in `SELECT` statements with an `EXPECT` clause. The engine evaluates these rules as rows stream through memory, so invalid data is caught and handled before it reaches target destinations.

Rules are part of the statement, not a comment on it. A rule decides which rows leave the statement, so it has to be something the parser reads and no formatter can strip; comments stay for the things that describe data (`@d`, `@owner`, `@pii`) rather than change it.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Rule Declarations & Actions

A rule is written after the column and its optional alias:

```sql
SELECT
    Email EXPECT NOT NULL ON FAILURE WARN
INTO #clean_users
FROM raw_users;
```

### Supported Rule Predicates

| Predicate | Example | Description |
| :--- | :--- | :--- |
| `NOT NULL` | `EXPECT NOT NULL` | Rejects NULL values. |
| `NOT BLANK` | `EXPECT NOT BLANK` | Rejects empty strings `''` and whitespace-only strings `'   '`. |
| Comparison | `EXPECT >= 0 AND <= 120` | Numeric range comparison against constants. |
| `BETWEEN` | `EXPECT BETWEEN 18 AND 65` | Numeric or date expression range check. |
| `MATCHES` | `EXPECT MATCHES '^[^@]+@[^@]+$'` | Regular expression pattern matching. The pattern is quoted. |
| `NOT MATCHES`| `EXPECT NOT MATCHES '<script'` | Rejects strings matching the regex. |
| `IN` | `EXPECT IN ('Active', 'Pending')` | Checks membership in a constant list. |
| `NOT IN` | `EXPECT NOT IN ('UNKNOWN', 'N/A')` | Rejects placeholder values. |
| `CASTABLE AS`| `EXPECT CASTABLE AS DATE` | Verifies value can be parsed as the target data type. |

Combine rules with `AND` / `OR`. A comma separates columns in the select list, so it never separates
rules — `NOT NULL AND >= 0`, not `NOT NULL, >= 0`.

### Failure Actions (`ON FAILURE`)

| Action | Behavior | Best Used When... |
| :--- | :--- | :--- |
| `THROW` | Halts script execution immediately and rolls back uncommitted changes. | Corrupted join keys, missing primary keys, or broken feeds. |
| `WARN` (default) | Increments a warning metric and logs summary samples; allows the row to load. | Calibrating new rules or when minor anomalies are acceptable. |
| `QUARANTINE` | Diverts the bad row to a quarantine table while allowing clean rows to load. | Production data imports requiring human triage or reprocessing. |

---

## Example 1: Basic Ingestion Rules with Warnings

When `ON FAILURE` is omitted, the action defaults to `WARN` — fail-safe, not silent. The engine logs an aggregated diagnostic at the end of the run with sample values (capped at 10 to protect log files).

```sql
CREATE CONNECTION src AS FLATFILE('data/raw_customers.csv');

SELECT
    CustomerId EXPECT NOT NULL,
    Age        EXPECT >= 0 AND <= 120,
    Email      EXPECT NOT BLANK
INTO #clean_customers
FROM src
ON FAILURE WARN;
```

---

## Example 2: Multiple Rules on a Single Column

To apply several distinct rules with different failure actions to the same column, repeat the clause.

```sql
CREATE CONNECTION src AS FLATFILE('data/orders.csv');

import_orders:
SELECT
    -- A missing OrderId stops the run (THROW), but a duplicate is set aside (QUARANTINE)
    OrderId     EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE QUARANTINE,
    CustomerEmail EXPECT MATCHES '^[^@]+@[^@]+$' ON FAILURE QUARANTINE,
    OrderAmount   EXPECT > 0 ON FAILURE THROW
INTO clean_orders
FROM src
ON FAILURE QUARANTINE TO quarantine_orders WITH (RETENTION = '30 DAYS')
ON FAILURE THROW;
```

---

## Example 3: Text Validation and Type Castability

When reading unstructured flat files where every column arrives as text, use `CASTABLE AS` to verify type safety before executing casts.

```sql
CREATE CONNECTION src AS FLATFILE('data/raw_feed.txt');

SELECT
    TransactionDate EXPECT CASTABLE AS DATE ON FAILURE QUARANTINE,
    Amount          EXPECT CASTABLE AS DECIMAL(18,2) ON FAILURE QUARANTINE,
    StatusCode      EXPECT NOT IN ('UNKNOWN', 'ERROR') ON FAILURE WARN
INTO #staged_transactions
FROM src
ON FAILURE QUARANTINE TO quarantine_transactions WITH (RETENTION = '14 DAYS')
ON FAILURE WARN;
```

---

## Two Critical Behaviors to Understand

1. **NULL skips all rules except `NOT NULL`**: Following the standard SQL `CHECK` constraint convention, a `NULL` value in an `Age` column will **not** trigger a failure for `>= 0`. If you require a non-null, positive value, combine both: `EXPECT NOT NULL AND >= 0`.
2. **Rules see the projected value**: If you select `UPPER(Email) EXPECT MATCHES '^[A-Z]+@'`, the rule validates the result of the `UPPER()` transformation, not the raw source value.

---

## Common Pitfalls

- **Routing must match**: ETL-SQL requires an exact match between the actions columns elect and the trailing `ON FAILURE` clauses. A column electing `QUARANTINE` with no routing clause is an error, and so is a routing clause no column elects — routing that nothing uses reads as enforcement that is not happening.

---

## Related Topics

- [Quarantine and Remediation](quarantine-and-remediation.md) — Routing failed rows, inspecting `__dq_*` columns, and replaying fixed records.
- [Multi-Row and Cross-Table Rules](multi-row-and-cross-table-rules.md) — `UNIQUE`, `UNIQUE_FIRST BY`, and `EXISTS IN`.
- [Run-Level Assertions](run-level-assertions.md) — Load-level quality assertions with `ASSERT JOB`.
