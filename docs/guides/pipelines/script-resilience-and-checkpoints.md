# Script Resilience, WHAT_IF Validation, and Checkpoints

Production data pipelines must guard against accidental data loss and recover gracefully from midway failures. ETL-SQL provides built-in mechanisms for **WHAT_IF dry-run validation**, **transaction boundaries**, and **label-based checkpoint resume**.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. Dry-Run Validation (`SET WHAT_IF ON`)

Before executing destructive operations (`DELETE`, `TRUNCATE`, `MERGE`, or destructive file operations), use `SET WHAT_IF ON` to preview affected rows without mutating target storage.

```sql
-- Phase 1: Dry-run validation
SET WHAT_IF ON;
DELETE FROM prod_db.dbo.AuditLogs WHERE LogDate < '2025-01-01';
SET WHAT_IF OFF;

-- Phase 2: Live execution
DELETE FROM prod_db.dbo.AuditLogs WHERE LogDate < '2025-01-01';
```

When `WHAT_IF` is active, the engine executes queries in simulation mode and prints the exact count of rows or files that would be modified.

---

## 2. Transaction Boundaries (`BEGIN TRANSACTION`)

Wrap multi-statement writes in a transaction to guarantee atomicity.

```sql
BEGIN TRY
    BEGIN TRANSACTION;

    INSERT INTO edw.dbo.FactOrders SELECT * FROM #staged_orders;
    UPDATE edw.dbo.DimCustomers SET LastOrderDate = GETDATE() WHERE CustomerId IN (SELECT CustomerId FROM #staged_orders);

    COMMIT;
    PRINT 'Transaction committed successfully.';
END TRY
BEGIN CATCH
    ROLLBACK;
    PRINT 'Transaction rolled back due to error: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

---

## 3. Long-Running Checkpoints and Resume (`--session` & `--resume`)

For batch jobs that run for hours across multiple stages (e.g., massive extraction followed by transformation and loading), restarting from the beginning after a late-stage failure wastes time and resources.

By placing **top-level section labels** in your script and running with `--session`, the engine checkpoints `#temp` tables and variable state to disk at every label boundary.

```sql
-- etl_pipeline.etlsql

Extract:
    PRINT 'Extracting 50 million rows from source...';
    SELECT * INTO #staged_records FROM source_db.Transactions;

Transform:
    PRINT 'Applying transformation rules...';
    UPDATE #staged_records SET NormalizedStatus = UPPER(Status);

Load:
    PRINT 'Loading into data warehouse...';
    INSERT INTO edw.dbo.Transactions SELECT * FROM #staged_records;

Cleanup:
    CLEAR SESSION;
```

### Running with Checkpoint Persistence

1. **Initial Run**:
   ```bash
   etl-sql run etl_pipeline.etlsql --session nightly_etl_session
   ```
   If a network failure occurs during the `Load:` step, state from `Extract:` and `Transform:` remains persisted on disk.

2. **Resuming after Fixing the Failure**:
   ```bash
   etl-sql run etl_pipeline.etlsql --session nightly_etl_session --resume
   ```
   The engine detects the last successful checkpoint (`Transform:`), skips re-extracting 50 million rows, and resumes execution directly at `Load:`.

---

## Common Pitfalls

- **Missing `CLEAR SESSION`**: Always invoke `CLEAR SESSION` in your final cleanup step when using `--session`. This purges temporary checkpoint files from disk once the job completes successfully.
- **Session without `--resume`**: Running `etl-sql run script.etlsql --session my_session` without the `--resume` flag always initializes a fresh session from the beginning.

---

## Related Topics

- [Staged vs. Streaming Ingestion](staged-vs-streaming-ingestion.md) — Ingestion mechanics.
- [Error Handling and Retries](error-handling-and-retries.md) — Structured `TRY...CATCH` logic.
- [CLI Reference](../../reference/cli/README.md) — `--session` and `--resume` flag specifications.
