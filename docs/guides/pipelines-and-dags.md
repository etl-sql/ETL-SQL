# Orchestrating Pipelines & DAGs

ETL-SQL handles pipeline coordination with normal script control flow: `RUN SCRIPT`, `PARALLEL`, `IF`, `TRY...CATCH`, scheduler jobs, and file or data readiness checks. This guide shows how to model DAG-style dependencies without introducing a separate workflow language.

Published Orchestrator bundles require literal `RUN SCRIPT 'child.etlsql'` dependencies so the full graph can be versioned. Pipelines that choose sub-scripts dynamically with variables or expressions should remain in live file mode.

---

## 1. Modularizing with `RUN SCRIPT`

Don't build monolithic scripts. Break your pipeline into logical modules (Extract, Transform, Load) and coordinate them from a master script.

```sql
-- master_pipeline.etlsql
PRINT 'Starting Nightly ETL...';

-- Run sub-scripts and pass parameters
RUN SCRIPT '01_extract_crm.etlsql' WITH (@env = 'PROD');
RUN SCRIPT '02_extract_erp.etlsql' WITH (@env = 'PROD');

PRINT 'Extraction complete.';
```

### Passing Data via `INPUT` / `OUTPUT`
Use `INPUT` and `OUTPUT` modifiers on `DECLARE` statements to pass state between scripts.

```sql
-- Parent
DECLARE @Status STRING;
RUN SCRIPT 'sub.etlsql' WITH (@Status = @Status);
PRINT 'Sub-script status: ' + @Status;

-- sub.etlsql
DECLARE @Status STRING OUTPUT = 'Success'; -- updates parent variable
```

---

## 2. Parallel Execution

The `PARALLEL` block allows you to run independent branches concurrently. This is essential for high-throughput batch windows.

```sql
PARALLEL
BEGIN
    RUN SCRIPT 'extract_marketing.etlsql';
    RUN SCRIPT 'extract_finance.etlsql';
    RUN SCRIPT 'extract_inventory.etlsql';
END
-- Execution waits for ALL branches above to finish before proceeding
PRINT 'Parallel extraction complete.';
```

> [!TIP]
> Use `PARALLEL(n)` to limit concurrent branches to `n`. Extra branches will wait in a queue, preventing resource exhaustion during massive loads.

Parallel branches share session variables but should write to separate `#temp` tables. Avoid having two branches mutate or read/write the same temp table at the same time; make the dependency explicit by joining or merging after the `PARALLEL` block completes.

---

## 3. Dependency Management (DAGs)

In a DAG, "Task B" can only start after "Task A" completes. You can achieve this using standard procedural logic and file-dependency checks.

### Pattern 1: Sequential Blocks
The simplest dependency is top-to-bottom execution.

```sql
-- Task A
RUN SCRIPT '01_load_staging.etlsql';

-- Task B (starts only if A succeeded)
RUN SCRIPT '02_aggregate_summary.etlsql';
```

### Pattern 2: Result-Based Dependencies
Check for the existence of data before proceeding.

```sql
RUN SCRIPT '01_extract.etlsql';

IF EXISTS (SELECT 1 FROM #staging_ready)
BEGIN
    RUN SCRIPT '02_transform.etlsql';
END
ELSE
BEGIN
    PRINT 'No data found in staging. Skipping transform.';
END
```

### Pattern 3: File-Based Signals
Use a "signal file" or "trigger file" pattern for cross-process coordination.

```sql
-- Wait until the data dump from the ERP is actually on disk
WAIT UNTIL FILE_EXISTS('C:\ETL\Trigger\dump_ready.txt');

-- Proceed with ingestion
RUN SCRIPT 'ingest_dump.etlsql';

-- Cleanup signal file
DELETE FILE 'C:\ETL\Trigger\dump_ready.txt';
```

---

## 4. Error Handling & Resilience

Wrap your pipeline steps in `TRY...CATCH` to handle failures gracefully.

```sql
BEGIN TRY
    RUN SCRIPT 'critical_task.etlsql';
END TRY
BEGIN CATCH
    PRINT 'Critical task failed: ' + ERROR_MESSAGE();
    SEND EMAIL
        TO 'admin@company.com'
        SUBJECT 'Pipeline Error'
        BODY ERROR_MESSAGE()
        AT mailer;
    THROW; -- Stop the entire master pipeline
END CATCH
```

The example assumes an SMTP connection named `mailer` already exists. Keep alert bodies free of connection strings, passwords, API keys, and `ENC:` values.

### Automatic Retries
For transient failures (network blips), use the **Job Scheduler** retry policy:

```sql
CREATE JOB NightlyETL ON SCHEDULE EVERY 1 DAY AT '02:00'
WITH (MAX_RETRIES = 3, RETRY_DELAY = 60)
AS
    RUN SCRIPT 'master_pipeline.etlsql';
```

---

## 5. Full DAG Example

```sql
-- Master Orchestrator
BEGIN TRY
    -- Stage 1: Parallel Extracts
    PARALLEL
    BEGIN
        RUN SCRIPT 'extract_src_a.etlsql';
        RUN SCRIPT 'extract_src_b.etlsql';
    END

    -- Stage 2: Dependent Transform (depends on A and B)
    IF FILE_EXISTS('C:\Temp\A_Ready.tmp') AND FILE_EXISTS('C:\Temp\B_Ready.tmp')
    BEGIN
        RUN SCRIPT 'transform_combined.etlsql';
    END

    -- Stage 3: Load
    RUN SCRIPT 'load_warehouse.etlsql';

    PRINT 'Pipeline finished successfully.';
END TRY
BEGIN CATCH
    PRINT 'Pipeline failed at: ' + GETDATE();
    THROW;
END CATCH

---

## 6. Visualizing the DAG

When running complex nested scripts, use the `--progress` flag in the CLI:

```bash
ETL-SQL run orchestrator.etlsql --progress
```

The console will render a live, dynamic **Execution Tree** showing which scripts are currently running, which are queued in `PARALLEL` blocks, and their individual progress bars.

