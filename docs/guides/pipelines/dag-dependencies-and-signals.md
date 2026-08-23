# DAG Dependencies and Workflow Signals

ETL-SQL allows you to model complex Directed Acyclic Graph (DAG) dependencies directly using standard procedural control flow—`PARALLEL`, `IF`, `WAIT UNTIL`, and `RUN SCRIPT`—without requiring separate workflow orchestrators or Python frameworks.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Dependency Coordination Patterns

```
   ┌───────────┐      ┌───────────┐
   │ Extract A │      │ Extract B │
   └─────┬─────┘      └─────┬─────┘
         │ (PARALLEL)       │
         └────────┬─────────┘
                  ▼
         ┌─────────────────┐
         │ Transform (Join)│
         └────────┬────────┘
                  ▼
         ┌─────────────────┐
         │ Load Warehouse  │
         └─────────────────┘
```

---

## Example 1: Multi-Stage DAG with Parallel Branches

Coordinate parallel extracts followed by a dependent multi-source merge and warehouse load.

```sql
BEGIN TRY
    -- Stage 1: Parallel Extractions (Independent sources)
    PARALLEL
    BEGIN
        RUN SCRIPT '01_extract_crm.etlsql';
        RUN SCRIPT '02_extract_erp.etlsql';
    END

    -- Stage 2: Dependent Transformation (Runs only after both extracts finish)
    RUN SCRIPT '03_transform_reconcile.etlsql';

    -- Stage 3: Final Load
    RUN SCRIPT '04_load_warehouse.etlsql';

    PRINT 'Full DAG completed successfully.';
END TRY
BEGIN CATCH
    PRINT 'DAG failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

---

## Example 2: Data-Driven Conditional Branching

Branch workflow execution based on whether staging tables contain new data.

```sql
RUN SCRIPT '01_extract_delta.etlsql';

-- Check if any new rows were staged
IF EXISTS (SELECT 1 FROM #delta_staging)
BEGIN
    PRINT 'New records detected. Executing transformation pipeline.';
    RUN SCRIPT '02_transform_dimensions.etlsql';
    RUN SCRIPT '03_merge_facts.etlsql';
END
ELSE
BEGIN
    PRINT 'No new records found. Skipping downstream transformations.';
END
```

---

## Example 3: File-Based Trigger Signals (`WAIT UNTIL`)

Pause pipeline execution until an upstream system or external partner deposits a readiness trigger file on disk.

```sql
PRINT 'Waiting for upstream data drop...';

-- Polls every 200ms until the file appears on disk
WAIT UNTIL FILE_EXISTS('C:\Incoming\ready_signal.txt');

PRINT 'Signal file detected. Starting ingestion.';
RUN SCRIPT 'ingest_payload.etlsql';

-- Cleanup signal file
DELETE FILE 'C:\Incoming\ready_signal.txt';
```

---

## Visualizing Pipeline Execution in Real Time

When running multi-stage or nested scripts from the terminal, use the `--progress` flag:

```bash
etl-sql run master_pipeline.etlsql --progress
```

The CLI renders a live **Execution Tree** displaying active scripts, queued parallel tasks, and individual throughput progress bars.

---

## Related Topics

- [Modular Scripts and Parameters](modular-scripts-and-parameters.md) — Invoking child scripts.
- [Parallel Execution](parallel-execution.md) — Concurrency and thread safety.
- [WAITFOR / WAIT UNTIL Reference](../../reference/control-flow/waitfor.md) — Polling and delay syntax.
