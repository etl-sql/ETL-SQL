# Column Quality Rules (@expect / @fail)

ETL-SQL lets you declare value-level validation rules directly on column projections in `SELECT` statements using `@expect` and `@fail` comment annotations. The engine evaluates these rules as rows stream through memory, ensuring invalid data is caught and handled before it reaches target destinations.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Rule Declarations & Actions

Column rules are declared inside block comments immediately following the projected column:

```sql
SELECT
    Email /* @expect: 'NOT NULL'; @fail: 'WARN'; */
INTO #clean_users
FROM raw_users;
```

### Supported Rule Predicates

| Predicate | Example | Description |
| :--- | :--- | :--- |
| `NOT NULL` | `@expect: 'NOT NULL'` | Rejects NULL values. |
| `NOT BLANK` | `@expect: 'NOT BLANK'` | Rejects empty strings `''` and whitespace-only strings `'   '`. |
| Comparison | `@expect: '>= 0, <= 120'` | Numeric range comparison against constants. |
| `BETWEEN` | `@expect: 'BETWEEN 18 AND 65'` | Numeric or date expression range check. |
| `MATCHES` | `@expect: 'MATCHES ^[^@]+@[^@]+$'` | Regular expression pattern matching. |
| `NOT MATCHES`| `@expect: 'NOT MATCHES <script'` | Rejects strings matching the regex. |
| `IN` | `@expect: "IN ('Active', 'Pending')"` | Checks membership in a constant list. |
| `NOT IN` | `@expect: "NOT IN ('UNKNOWN', 'N/A')"` | Rejects placeholder values. |
| `CASTABLE AS`| `@expect: 'CASTABLE AS DATE'` | Verifies value can be parsed as the target data type. |

### Failure Actions (`@fail`)

| Action | Behavior | Best Used When... |
| :--- | :--- | :--- |
| `THROW` | Halts script execution immediately and rolls back uncommitted changes. | Corrupted join keys, missing primary keys, or broken feeds. |
| `WARN` (default) | Increments a warning metric and logs summary samples; allows the row to load. | Calibrating new rules or when minor anomalies are acceptable. |
| `QUARANTINE` | Diverts the bad row to a quarantine table while allowing clean rows to load. | Production data imports requiring human triage or reprocessing. |

---

## Example 1: Basic Ingestion Rules with Warnings

When `@fail` is omitted, the action defaults to `WARN`. The engine logs an aggregated diagnostic at the end of the run with sample values (capped at 10 to protect log files).

```sql
CREATE CONNECTION src AS FLATFILE('data/raw_customers.csv');

SELECT
    CustomerId /* @expect: 'NOT NULL'; */,
    Age        /* @expect: '>= 0, <= 120'; */,
    Email      /* @expect: 'NOT BLANK'; */
INTO #clean_customers
FROM src
ON FAILURE WARN;
```

---

## Example 2: Multiple Rules on a Single Column (Numbered Suffixes)

To apply multiple distinct rules with different failure actions to the same column, use numbered suffixes (`@expect_1` / `@fail_1`).

```sql
CREATE CONNECTION src AS FLATFILE('data/orders.csv');

import_orders:
SELECT
    -- A missing OrderId stops the run (THROW), but a duplicate is set aside (QUARANTINE)
    OrderId     /* @expect: 'NOT NULL'; @fail: 'THROW';
                   @expect_1: 'UNIQUE'; @fail_1: 'QUARANTINE'; */,
    CustomerEmail /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */,
    OrderAmount   /* @expect: '> 0'; @fail: 'THROW'; */
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
    TransactionDate /* @expect: 'CASTABLE AS DATE'; @fail: 'QUARANTINE'; */,
    Amount          /* @expect: 'CASTABLE AS DECIMAL(18,2)'; @fail: 'QUARANTINE'; */,
    StatusCode      /* @expect: "NOT IN ('UNKNOWN', 'ERROR')"; @fail: 'WARN'; */
INTO #staged_transactions
FROM src
ON FAILURE QUARANTINE TO quarantine_transactions WITH (RETENTION = '14 DAYS')
ON FAILURE WARN;
```

---

## Two Critical Behaviors to Understand

1. **NULL skips all rules except `NOT NULL`**: Following the standard SQL `CHECK` constraint convention, a `NULL` value in an `Age` column will **not** trigger a failure for `>= 0`. If you require a non-null, positive value, combine both: `'NOT NULL, >= 0'`.
2. **Rules see the projected value**: If you select `UPPER(Email) /* @expect: 'MATCHES ^[A-Z]+@' */`, the rule validates the result of the `UPPER()` transformation, not the raw source value.

---

## Common Pitfalls

- **Stripped comments**: Because rules live inside comments, external code formatters or database migration tools that strip comments can silently disable checks. ETL-SQL requires an exact match between declared `QUARANTINE` rules and trailing `ON FAILURE` clauses; a missing clause causes a script compilation error.

---

## Related Topics

- [Quarantine and Remediation](quarantine-and-remediation.md) — Routing failed rows, inspecting `__dq_*` columns, and replaying fixed records.
- [Multi-Row and Cross-Table Rules](multi-row-and-cross-table-rules.md) — `UNIQUE`, `UNIQUE_FIRST BY`, and `EXISTS IN`.
- [Run-Level Assertions](run-level-assertions.md) — Load-level quality assertions with `ASSERT JOB`.
