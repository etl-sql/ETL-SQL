# Data Quality Quarantine and Remediation

When data imports encounter invalid rows, stopping the entire batch (`THROW`) is often unacceptable. ETL-SQL's **Quarantine** pattern diverts invalid rows into a durable capture table with complete diagnostic metadata, allowing valid records to load unimpeded.

Data stewards can triage the captured rows, correct root causes, and reprocess them via `REPLAY QUARANTINE`.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## The Quarantine Lifecycle

```
┌────────────────────────────────────────────────────────┐
│ 1. Ingestion: SELECT ... ON FAILURE QUARANTINE         │
├───────────────────────────┬────────────────────────────┤
│ Valid Rows                │ Invalid Rows               │
│ → Load to clean target    │ → Diverted to quarantine   │
└───────────────────────────┴─────────────┬──────────────┘
                                          │
                                          ▼
                            ┌────────────────────────────┐
                            │ 2. Triage & Edit           │
                            │ Inspect __dq_* metadata;   │
                            │ Fix values & set:          │
                            │ __dq_status = 'released'   │
                            └─────────────┬──────────────┘
                                          │
                                          ▼
                            ┌────────────────────────────┐
                            │ 3. Replay Quarantine       │
                            │ REPLAY QUARANTINE <table>; │
                            │ Validated rows reload into │
                            │ production clean target    │
                            └────────────────────────────┘
```

---

## The Quarantine Capture Schema

Quarantine tables capture the source row **as it was read** (including unselected source columns) along with engine-generated diagnostic columns:

| Column | Type | Description |
| :--- | :--- | :--- |
| `__dq_column` | `VARCHAR` | The column that failed the rule. |
| `__dq_rule` | `VARCHAR` | The rule predicate (e.g. `'NOT NULL'`, `'MATCHES ^[^@]+@[^@]+$'`). |
| `__dq_reason` | `VARCHAR` | Human-readable explanation of the validation failure. |
| `__dq_value` | `VARCHAR` | Offending value as a string representation. |
| `__dq_status` | `VARCHAR` | Lifecycle state: `'quarantined'`, `'released'`, `'replaying'`, or `'replayed'`. |
| `__dq_ts` | `DATETIME`| Timestamp when the row was quarantined. |
| `__dq_row_id` | `VARCHAR` | Stable unique identifier for the captured row. |

---

## Example 1: Standard Staged Import with Quarantine

Quarantine requires a section label (`import_customers:`) which acts as the re-entry point during subsequent replay operations.

```sql
CREATE CONNECTION src AS FLATFILE('data/customers.csv');

import_customers:
SELECT
    CustomerId /* @expect: 'NOT NULL'; @fail: 'THROW'; */,
    Email      /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */,
    Age        /* @expect: '>= 0, <= 120'; @fail: 'QUARANTINE'; */,
    Region     /* @expect: "IN ('NA', 'EMEA', 'APAC')"; @fail: 'QUARANTINE'; */
INTO clean_customers
FROM src
ON FAILURE QUARANTINE TO quarantine_customers WITH (RETENTION = '30 DAYS')
ON FAILURE THROW;
```

---

## Example 2: Triaging Captured Rows with SQL

Data stewards can query quarantine tables directly to identify common failure trends and inspect source context.

```sql
-- 1. Identify which rules fail most frequently
SELECT __dq_column, __dq_rule, COUNT(*) AS FailureCount
FROM quarantine_customers
WHERE __dq_status = 'quarantined'
GROUP BY __dq_column, __dq_rule
ORDER BY FailureCount DESC;

-- 2. Inspect specific bad records
SELECT CustomerId, Email, Age, __dq_column, __dq_reason
FROM quarantine_customers
WHERE __dq_column = 'Email' AND __dq_status = 'quarantined';
```

---

## Example 3: Replaying Corrected Rows

To remediate quarantined rows:
1. Update the invalid data fields in the quarantine table.
2. Set `__dq_status = 'released'`.
3. Execute `REPLAY QUARANTINE`.

```sql
-- Fix invalid email domain and mark row ready for replay
UPDATE quarantine_customers
SET Email = 'alex@example.com',
    __dq_status = 'released'
WHERE CustomerId = 'CUST-1049' AND __dq_status = 'quarantined';

-- Replay all released rows through the original pipeline section
REPLAY QUARANTINE quarantine_customers;
```

During replay, the engine claims released rows, re-runs them through the section label logic (`import_customers:`), inserts successful rows into `clean_customers`, and marks them `__dq_status = 'replayed'`.

---

## Example 4: In-Script Remediation (`HANDLING = SCRIPT`)

If a pipeline already knows how to fix bad rows programmatically in the same run (without human intervention), use `HANDLING = SCRIPT`.

```sql
SELECT 
    CustomerId /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */,
    Region
INTO clean_orders
FROM raw_orders
ON FAILURE QUARANTINE TO #needs_default WITH (HANDLING = SCRIPT);

-- Fix bad rows immediately in the same session
INSERT INTO clean_orders (CustomerId, Region)
SELECT 0, Region 
FROM #needs_default 
WHERE __dq_rule = 'NOT NULL';
```

`HANDLING = SCRIPT` disables replay manifests and steward queue entries, eliminating unnecessary administrative backlog.

---

## Common Pitfalls

- **Quarantining into `#temp` tables**: Except when using `HANDLING = SCRIPT`, quarantine targets must be **durable database tables**, not session `#temp` tables. `#temp` tables are destroyed when the session exits, which destroys the quarantine evidence.
- **Modifying diagnostic columns**: When updating rows for replay, edit only the source columns (e.g. `Email`, `Age`). Do not alter `__dq_rule` or `__dq_row_id`.

---

## Related Topics

- [Column Quality Rules](column-quality-rules.md) — Declaring `@expect` and `@fail` predicates.
- [Multi-Row and Cross-Table Rules](multi-row-and-cross-table-rules.md) — Checking uniqueness and foreign key existence.
- [Data Stewardship and Impact Analysis](data-stewardship-and-impact.md) — Portal steward workflows.
