# Parallel Pipeline Execution

To accelerate batch processing windows and maximize system throughput, ETL-SQL provides the **`PARALLEL`** block. Independent script tasks or queries inside a parallel block execute concurrently across separate worker threads.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## The PARALLEL Block & Concurrency Throttling

```sql
PARALLEL
BEGIN
    RUN SCRIPT 'extract_crm.etlsql';
    RUN SCRIPT 'extract_erp.etlsql';
    RUN SCRIPT 'extract_billing.etlsql';
END
```

The parent script pauses execution until **all** branches inside the block complete (barrier synchronization).

### Throttling Concurrency with `PARALLEL(n)`

To prevent exhausting database connection pools or system memory during large batch runs, specify a maximum concurrency limit:

```sql
-- Limit concurrency to 4 simultaneous tasks
PARALLEL(4)
BEGIN
    RUN SCRIPT 'feed_01.etlsql';
    RUN SCRIPT 'feed_02.etlsql';
    RUN SCRIPT 'feed_03.etlsql';
    RUN SCRIPT 'feed_04.etlsql';
    RUN SCRIPT 'feed_05.etlsql';
    RUN SCRIPT 'feed_06.etlsql';
END
```

---

## Example 1: Parallel Extraction Across Multiple Sources

Extract data from independent regional source databases simultaneously into distinct staging tables.

```sql
BEGIN TRY
    PARALLEL
    BEGIN
        -- Branch 1: North America
        SELECT OrderId, Region, Amount 
        INTO #na_orders 
        FROM na_db.Orders 
        WHERE OrderDate = CURRENT_DATE;

        -- Branch 2: EMEA
        SELECT OrderId, Region, Amount 
        INTO #emea_orders 
        FROM emea_db.Orders 
        WHERE OrderDate = CURRENT_DATE;

        -- Branch 3: APAC
        SELECT OrderId, Region, Amount 
        INTO #apac_orders 
        FROM apac_db.Orders 
        WHERE OrderDate = CURRENT_DATE;
    END

    -- All branches have completed: combine results
    SELECT * INTO #all_orders FROM #na_orders
    UNION ALL
    SELECT * FROM #emea_orders
    UNION ALL
    SELECT * FROM #apac_orders;

    PRINT 'Parallel extraction and union complete.';
END TRY
BEGIN CATCH
    PRINT 'Parallel extraction failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

---

## Example 2: Concurrent Sub-Script Pipeline

Run independent sub-scripts in parallel with explicit parameter injection.

```sql
DECLARE @runDate DATE = '2026-06-01';

PARALLEL(2)
BEGIN
    RUN SCRIPT 'tasks/sync_inventory.etlsql' WITH (@batchDate = @runDate);
    RUN SCRIPT 'tasks/sync_customers.etlsql' WITH (@batchDate = @runDate);
    RUN SCRIPT 'tasks/sync_vendors.etlsql'   WITH (@batchDate = @runDate);
END

PRINT 'All parallel synchronization tasks completed.';
```

---

## Thread Safety Guidelines

1. **Isolate Temp Tables**: Each parallel branch must write to its own uniquely named `#temp` table (e.g. `#na_orders`, `#emea_orders`). Writing to or reading from the same `#temp` table concurrently produces non-deterministic results.
2. **Session Variable Reads**: Parallel branches can safely read parent `@variables`. Avoid mutating shared variables concurrently inside parallel branches.
3. **Union After Synchronization**: Merge or join branch outputs *after* the `PARALLEL` block closes, ensuring all data is fully written.

---

## Related Topics

- [Modular Scripts and Parameters](modular-scripts-and-parameters.md) — Invoking child scripts.
- [DAG Dependencies and Signals](dag-dependencies-and-signals.md) — Complex workflow coordination.
- [PARALLEL Reference](../../reference/control-flow/parallel.md) — Detailed syntax reference.
