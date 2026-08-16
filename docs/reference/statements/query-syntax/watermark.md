# WATERMARK

Declarative incremental change tracking attached directly to table references in `SELECT` queries via `WITH (WATERMARK = ...)`. The engine automatically retrieves the previous high-water mark from persistent state, injects a filtering predicate, and commits the new maximum boundary upon successful script completion.

---

## Syntax

```sql
SELECT <columns...>
[ INTO #stage_table ]
FROM <table_reference> WITH (
    WATERMARK = 'column_name'
    [, INITIAL = 'initial_value' | number ]
    [, KEY = 'custom_state_key' ]
    [, INCLUSIVE = TRUE | FALSE ]
    [, STRICT = TRUE | FALSE ]
)
[ WHERE <additional_conditions...> ];
```

---

## Options & Watermark Semantics

- **WATERMARK = 'column_name'** — Column name (timestamp, datetime, integer sequence, or monotonic ID) used to track deltas.
- **INITIAL = 'value'** — Initial boundary applied on the first run when no prior state exists (e.g., `'2025-01-01'` or `0`). If omitted, all existing rows are ingested on the first run.
- **KEY = 'state_key'** — Unique identifier key for storing state in the local `.etlstate` file or Orchestrator database. Defaults to `"{TableName}:{ColumnName}"`.
- **INCLUSIVE = TRUE | FALSE** — When `TRUE`, evaluates `>= [watermark]`. When `FALSE`, evaluates `> [watermark]` (default: `FALSE`).
- **STRICT = TRUE | FALSE** — When `TRUE`, enforces strict `>` comparison (default: `TRUE`).

---

## Examples

### 1. Simple Monotonic Sequence Extraction

Ingest only new events based on an auto-incrementing integer key:

```sql
SELECT event_id, event_type, payload, created_at
INTO #new_events
FROM source_db.events WITH (
    WATERMARK = 'event_id',
    INITIAL = 0,
    KEY = 'events_stream'
);
```

### 2. Production Pattern: End-to-End Incremental ETL with MERGE

Extract updated customer records, validate against business rules, and merge into target data warehouse:

```sql
CREATE CONNECTION src AS POSTGRES(HOST='crm.internal', DATABASE='production');
CREATE CONNECTION dw  AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

BEGIN TRY
    -- 1. Extract only records updated since last run
    SELECT id, email, first_name, last_name, status, updated_at
    INTO #staging_customers
    FROM src.customers WITH (
        WATERMARK = 'updated_at',
        INITIAL = '2026-01-01 00:00:00',
        KEY = 'crm_customers_delta'
    );

    -- 2. Data cleansing and standardization
    UPDATE #staging_customers 
    SET email = LOWER(TRIM(email));

    -- 3. Idempotent Upsert into Data Warehouse
    MERGE INTO dw.dbo.DimCustomer AS T
    USING #staging_customers AS S ON T.CustomerId = S.id
    WHEN MATCHED AND S.updated_at > T.UpdatedAt THEN
        UPDATE SET 
            T.Email = S.email,
            T.FirstName = S.first_name,
            T.LastName = S.last_name,
            T.Status = S.status,
            T.UpdatedAt = S.updated_at
    WHEN NOT MATCHED THEN
        INSERT (CustomerId, Email, FirstName, LastName, Status, UpdatedAt)
        VALUES (S.id, S.email, S.first_name, S.last_name, S.status, S.updated_at);

    PRINT 'Incremental customer synchronization completed successfully.';
END TRY
BEGIN CATCH
    PRINT 'Sync failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

---

## State Persistence & Failure Semantics

- **Atomicity**: The new watermark value is only committed when the statement (or enclosing transaction) succeeds. If an error occurs downstream, the previous watermark remains active, ensuring zero data loss on retry.
- **Environment Awareness**: In CLI/workstation mode, state is preserved in local `.etlstate` JSON caches. Under the Orchestrator, watermarks are persisted in shared relational storage with lease fencing.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [MERGE Statement](../dml/merge.md)
- [ETL Cookbook: Incremental Load With High-Water Mark](../../../cookbooks/etl/incremental-load-with-high-water-mark.md)
- [ETL Cookbook: SCD Type 2](../../../cookbooks/etl/scd-type-2.md)
- [Job Orchestration](../../../administration/orchestration/README.md)
