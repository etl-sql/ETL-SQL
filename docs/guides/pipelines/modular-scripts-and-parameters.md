# Modular Scripts and Parameter Passing

Instead of building monolithic script files, ETL-SQL allows you to decompose complex data workflows into modular, reusable `.etlsql` scripts and orchestrate them using **`RUN SCRIPT`**.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## The Modular Pipeline Pattern

```
master_pipeline.etlsql
  ├── RUN SCRIPT 'extract_crm.etlsql'
  ├── RUN SCRIPT 'extract_erp.etlsql'
  ├── RUN SCRIPT 'transform_sales.etlsql'
  └── RUN SCRIPT 'load_warehouse.etlsql'
```

### Passing State with `INPUT` and `OUTPUT`

Variables declared in a child script can receive values from a caller (`INPUT`) or return modified state back to the parent script (`OUTPUT`):

- **`INPUT` (default)**: Receives a value passed via `WITH (@param = value)`.
- **`OUTPUT`**: Updates the parent script's variable when the child finishes.

---

## Example 1: Master Orchestration Script

Coordinate extract, transform, and load steps with explicit parameter passing.

```sql
-- master_orchestrator.etlsql
PRINT 'Starting Nightly ETL Window...';

DECLARE @env STRING = 'PROD';
DECLARE @batchDate DATE = '2026-06-01';

-- Run sub-scripts and pass parameters
RUN SCRIPT '01_extract_crm.etlsql' WITH (@env = @env, @batchDate = @batchDate);
RUN SCRIPT '02_extract_erp.etlsql' WITH (@env = @env, @batchDate = @batchDate);
RUN SCRIPT '03_load_warehouse.etlsql' WITH (@env = @env);

PRINT 'Nightly ETL Window completed successfully.';
```

---

## Example 2: Returning State via `OUTPUT` Parameters

Capture execution status and row counts from a child script.

```sql
-- Parent Script: master.etlsql
DECLARE @status  STRING;
DECLARE @rowsOut INT = 0;

RUN SCRIPT 'child_extract.etlsql' WITH (
    @status  = @status,
    @rowsOut = @rowsOut
);

PRINT 'Child completed with status: ' + @status + ' (Processed: ' + CAST(@rowsOut AS STRING) + ' rows)';
```

```sql
-- Child Script: child_extract.etlsql
DECLARE @status  STRING OUTPUT = 'Pending';
DECLARE @rowsOut INT    OUTPUT = 0;

CREATE CONNECTION src AS MOCKDB();
SELECT * INTO #extracted FROM src.Orders;

SET @rowsOut = (SELECT COUNT(*) FROM #extracted);
SET @status = 'Success';
```

---

## Common Pitfalls

- **Dynamic Script Paths in Orchestrator Bundles**: When publishing scripts as versioned Orchestrator bundles, use string literals for script paths (`RUN SCRIPT 'extract.etlsql'`). Dynamic string expressions (`RUN SCRIPT @dynamicPath`) cannot be statically packaged into dependency trees.
- **Overwriting shared temp tables**: Sub-scripts execute in the parent session context. Use distinct temp table names across sub-scripts to avoid naming collisions.

---

## Related Topics

- [Parallel Execution](parallel-execution.md) — Running sub-scripts concurrently.
- [DAG Dependencies and Signals](dag-dependencies-and-signals.md) — Coordinating dependent tasks.
- [RUN Reference](../../reference/control-flow/run.md) — Statement reference.
