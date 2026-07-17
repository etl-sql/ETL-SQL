# DAGs and Advanced Orchestration

## 13. Advanced Orchestration: DAGs (Directed Acyclic Graphs)

As your data ecosystem grows, you will inevitably need to orchestrate complex dependencies where scripts must run in a specific order, sometimes in parallel, and often gated by the appearance of external data.

### 13.1 Composition with `RUN SCRIPT`

ETL-SQL promotes **modular orchestration**. Instead of one 5,000-line script, break your pipeline into logical modules (Extract, Transform, Load) and coordinate them from a "Main" orchestrator script.

```sql
-- Main Orchestrator.etlsql
DECLARE @run_date DATE = GETDATE();

PRINT '--- Starting Nightly Pipeline ---';

RUN SCRIPT '01_Extract.etlsql' WITH (@date = @run_date);
RUN SCRIPT '02_Transform.etlsql' WITH (@date = @run_date);
RUN SCRIPT '03_Load.etlsql' WITH (@date = @run_date);

PRINT '--- Pipeline Complete ---';
```

### 13.2 Fan-out / Parallel Execution

When multiple independent scripts can run at once, use the `PARALLEL` block. This significantly reduces total wall-clock time by utilizing all available CPU cores and I/O bandwidth.

```sql
PRINT 'Starting parallel extracts...';

PARALLEL (4) -- Limit to 4 concurrent scripts
BEGIN
    RUN SCRIPT 'extract_erp.etlsql';
    RUN SCRIPT 'extract_crm.etlsql';
    RUN SCRIPT 'extract_logs.etlsql';
    RUN SCRIPT 'extract_legacy.etlsql';
END;

PRINT 'All extracts finished. Starting transformation...';
RUN SCRIPT 'global_transform.etlsql';
```

> [!NOTE]
> The engine automatically handles **Context Forking** inside `PARALLEL` blocks. Each script runs in its own isolated environment, and variables/connections are merged back into the main thread in the order they were submitted.

### 13.3 Dependency Gating (WAIT UNTIL) Gating

Often, a pipeline should not start until an external file arrives (e.g., from an SFTP source or an upstream process). Use `WAITFOR (condition)` to create dynamic gates.

```sql
PRINT 'Waiting for daily_source.csv...';

-- Polls every 200ms until the file exists
WAITFOR (FILE_EXISTS('C:\DropZone\daily_source.csv'));

PRINT 'File arrived. Beginning ingestion.';
RUN SCRIPT 'ingest_file.etlsql';
```

### 13.4 Conditional Branching

You can use standard `IF` logic to handle optional execution paths based on data state or environment.

```sql
-- Only run the archive job on the first day of the month
IF DAY(GETDATE()) = 1
BEGIN
    PRINT 'Detected first of month. Running archive...';
    RUN SCRIPT 'monthly_archive.etlsql';
END
ELSE
BEGIN
    PRINT 'Skipping archive (not 1st of month).';
END
```

### 13.5 Visualizing the DAG

When running complex nested scripts, use the `--progress` flag in the CLI:

```bash
ETL-SQL run orchestrator.etlsql --progress
```

The console will render a live, dynamic **Execution Tree** showing which scripts are currently running, which are queued in `PARALLEL` blocks, and their individual progress bars.

---

---

