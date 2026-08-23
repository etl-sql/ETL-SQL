# Multi-Row and Cross-Table Quality Rules

While standard `@expect` rules evaluate columns in isolation, many data quality checks require evaluating relationships across multiple rows, across different columns, or against reference dimension tables.

ETL-SQL provides native predicates for uniqueness, relationship lookups, and whole-row expressions.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Rule Categories

| Category | Predicate Syntax | Description |
| :--- | :--- | :--- |
| **Strict Uniqueness** | `@expect: 'UNIQUE'` | Quarantines *all* rows in any duplicate group. |
| **Deduplicated Uniqueness** | `@expect: 'UNIQUE_FIRST BY col'` | Keeps the first row ordered by `col`; quarantines subsequent duplicates. |
| **Composite Uniqueness** | `@expect: 'UNIQUE WITH (col1, col2)'` | Enforces uniqueness across a tuple of columns. |
| **Foreign Key / Exists** | `@expect: 'EXISTS IN dim_table(id)'` | Verifies the value exists in a reference table. |
| **Scoped Multi-Tenant Exists** | `@expect: 'EXISTS WITH (t_id, c_id) IN dim(t_id, c_id)'` | Verifies existence scoped to a tenant boundary. |
| **Row Expression** | `@expect: 'EXPR StartDate <= EndDate'` | Evaluates a custom Boolean expression across multiple columns in the row. |

---

## Example 1: Stream Deduplication with `UNIQUE_FIRST BY`

In event streams (such as Kafka or append-only logs) where at-least-once delivery produces duplicate events, `UNIQUE_FIRST BY` preserves the earliest event and diverts subsequent duplicates.

```sql
CREATE CONNECTION raw_stream AS FLATFILE('data/events.json');

load_events:
SELECT
    EventId   /* @expect: 'UNIQUE_FIRST BY EventTime'; @fail: 'QUARANTINE'; */,
    EventTime,
    EventType,
    Payload
INTO clean_events
FROM raw_stream
ON FAILURE QUARANTINE TO quarantine_events WITH (RETENTION = '14 DAYS');
```

> [!NOTE]
> The `BY <column>` clause is required for deterministic ordering. Statements using `UNIQUE` rules buffer records using engine spill storage to evaluate the full batch while reading the source exactly once.

---

## Example 2: Scoped Multi-Tenant Validation (`EXISTS WITH`)

In multi-tenant systems, checking a single foreign key (like `CustomerId`) can accidentally pass a record if that customer ID belongs to a *different* tenant. Use `EXISTS WITH` to enforce compound multi-tenant boundaries.

```sql
CREATE CONNECTION src AS FLATFILE('data/invoices.csv');

SELECT
    TenantId   /* @expect: 'NOT NULL'; @fail: 'THROW'; */,
    CustomerId /* @expect: 'EXISTS WITH (TenantId, CustomerId) IN dim_customers(TenantId, CustomerId)'; 
                  @fail: 'QUARANTINE'; */,
    InvoiceAmount /* @expect: '> 0'; @fail: 'QUARANTINE'; */
INTO clean_invoices
FROM src
ON FAILURE QUARANTINE TO quarantine_invoices WITH (RETENTION = '30 DAYS')
ON FAILURE THROW;
```

---

## Example 3: Cross-Column Date Range Consistency (`EXPR`)

Validate that event start timestamps always precede end timestamps using `EXPR`.

```sql
CREATE CONNECTION src AS FLATFILE('data/contracts.csv');

SELECT
    ContractId,
    StartDate,
    EndDate /* @expect: 'EXPR StartDate <= EndDate'; @fail: 'QUARANTINE'; */,
    MonthlyValue
INTO #valid_contracts
FROM src
ON FAILURE QUARANTINE TO #invalid_dates WITH (HANDLING = SCRIPT);
```

---

## Common Pitfalls

- **Unprojected Columns in `EXPR` or `EXISTS WITH`**: All columns referenced in an `EXPR` or `EXISTS WITH (...)` predicate must be explicitly selected in the `SELECT` list.
- **Null handling in composite keys**: If any part of a composite key evaluates to `NULL`, the `EXISTS WITH` rule skips evaluation. If null keys are prohibited, combine with `'NOT NULL'`.

---

## Related Topics

- [Column Quality Rules](column-quality-rules.md) — Single-column `@expect` and `@fail` predicates.
- [Quarantine and Remediation](quarantine-and-remediation.md) — Managing diverted records.
- [Run-Level Assertions](run-level-assertions.md) — Validating batch-level metrics with `ASSERT JOB`.
