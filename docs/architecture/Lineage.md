# ETL-SQL Lineage & Governance Architecture

**Applies to ETL-SQL 0.16.0**

This document covers the lineage and governance subsystem: what is tracked automatically, how to query it during and after a run, how to export it, and how it connects to Orchestrator execution history.

---

## 1. What is tracked

Every DML and transformation operation that the engine executes is recorded in the in-memory `LineageTracker`. Recording is automatic — scripts do not need to do anything to enable it.

### 1.1 Operations that generate lineage entries

| Operation | Operation label recorded |
| :--- | :--- |
| `INSERT INTO` | `INSERT` |
| `SELECT … INTO` | `SELECT` |
| `UPDATE` | `UPDATE` |
| `MERGE` | `MERGE` |
| `CREATE TABLE AS` / `CTAS` | `CTAS` |
| File import / `BULK INSERT` | `FILE_IMPORT` |
| Database catalog discovery | `DB_CATALOG` |
| Column-level tag annotation | `TABLE_TAGS` / `COLUMN_TAGS` |
| Script `/* @d: … */` inline annotations | propagated via metadata inheritance |

### 1.2 Fields captured per entry

| Field | Description |
| :--- | :--- |
| `TargetTable` | Destination table or virtual table name |
| `TargetColumn` | Destination column (column-level lineage only) |
| `SourceTables` | List of source table names |
| `SourceColumns` | List of corresponding source column names |
| `Operation` | Operation label (see table above) |
| `Metadata` | Tag dictionary (`@owner`, `@d`, `@tags`, custom keys) |
| `DerivedFromDescriptions` | Inherited `@d` descriptions from source columns |
| `TransformationKind` | `Unknown`, `PassThrough`, `Cast`, `FunctionCall`, `CaseExpression`, `Arithmetic`, `StringOperation`, `Aggregation`, `WindowFunction`, `Conditional`, `Literal`, or `Subquery` |
| `TransformationExpression` | Raw SQL expression for computed columns |
| `FunctionsApplied` | List of SQL functions used in the expression |
| `SourceFile` | Script file name |
| `Line` / `Column` | Source location of the statement |
| `Timestamp` | UTC timestamp of the record |

### 1.3 Global metadata

The `LineageTracker` automatically populates `GlobalMetadata` from the script's header comment tags (`/* @owner: … */`, `/* @tags: … */`, etc.) and adds it to every entry recorded during that run. The `author` key defaults to `Environment.UserName` if no `@owner` tag is present.

---

## 2. Enabling and configuring lineage

Lineage tracking is on by default. The relevant settings in `src/appsettings.json`:

```json
{
  "Engine": {
    "LineageEnabled": true
  },
  "Lineage": {
    "OpenLineageFile": null,
    "OpenLineageEndpoint": null
  }
}
```

`SET LINEAGE = OFF;` disables tracking for the current script execution without touching `appsettings.json`.

Auto-export to an OpenLineage endpoint (e.g., Marquez) can be configured by setting `Lineage:OpenLineageEndpoint` — the evaluator POSTs the catalog after each script completes.

---

## 3. Querying lineage in a script

### 3.1 Current-run lineage (in-session)

```sql
-- Full lineage for every table touched in this run
SHOW LINEAGE;

-- All operations that produced #CustomerSummary
SHOW LINEAGE FOR TABLE #CustomerSummary;

-- Column-level trace: how was the Revenue column derived?
SHOW LINEAGE FOR TABLE #CustomerSummary COLUMN Revenue;

-- Report and dataset targets
SHOW LINEAGE FOR REPORT SalesDashboard;
SHOW LINEAGE FOR DATASET &CustomerDS;
```

`SHOW LINEAGE` prints a visual dependency graph and a timestamped audit table to the console. It can also be redirected:

```sql
-- Capture lineage into a temp table for further analysis
SHOW LINEAGE FOR TABLE #CustomerSummary INTO #LineageResult;
SELECT * FROM #LineageResult WHERE Operation = 'SELECT';
```

The columns returned in `INTO` mode match the `LineageEntry` model:
`Timestamp`, `Operation`, `TargetTable`, `TargetColumn`, `SourceTables`, `SourceColumns`, `Description`, `Metadata`, `DerivedFromDescriptions`, `SourceFile`, `Line`, `Column`, `TransformationKind`, `TransformationExpression`, `FunctionsApplied`.

### 3.2 Exporting from a script

```sql
-- Export a Mermaid graph + audit log to a Markdown file
SHOW LINEAGE FOR TABLE #Orders EXPORT TO 'reports/orders_lineage.md';

-- Export in OpenLineage JSON format
SHOW LINEAGE EXPORT AS OPENLINEAGE TO 'exports/run.openlineage.json';
```

The Mermaid export wraps the graph in a fenced ` ```mermaid ` block and appends a full audit log table, ready for GitHub rendering or import into Notion/Confluence.

---

## 4. Cross-run history (lineage catalog)

While in-session lineage covers the current run, the `ILineageCatalogStore` persists entries across runs in the Orchestrator state store. Single-node deployments use SQLite by default; HA deployments use the same PostgreSQL-backed `RelationalJobHistoryStore` selected by `Orchestrator:Database:Provider=Postgres`. This catalog is populated automatically whenever an Orchestrator-managed job completes. Portal executions also flush run lineage through the configured catalog store. For standalone `--run` executions the catalog is populated when `Engine:AuditAdHocRuns = true`.

### 4.1 Querying the catalog

```sql
-- All runs that ever wrote to or read from DW.FactSales
SHOW LINEAGE HISTORY FOR TABLE FactSales;

-- Runs tagged @owner = 'finance'
SHOW LINEAGE HISTORY FOR TAG owner = 'finance';

-- All lineage recorded by job DailyLoad
SHOW LINEAGE HISTORY FOR JOB DailyLoad;

-- Limit results
SHOW LINEAGE HISTORY FOR TABLE FactSales LIMIT 50;

-- Capture into a temp table
SHOW LINEAGE HISTORY FOR TAG owner INTO #OwnerHistory;
```

Catalog query results return these columns: `Id`, `RunAt`, `JobName`, `TargetTable`, `TargetColumn`, `SourceTables`, `Operation`, `Tags`, `SourceFile`, `Line`.

### 4.2 Remote catalog queries

When the Orchestrator is deployed as a service, catalog queries can be routed to it from a client script using `AT`:

```sql
SHOW LINEAGE HISTORY FOR TABLE FactSales AT OrchestratorConn;
```

`OrchestratorConn` must be a registered portal admin connection. The `AT` clause is available on all three `SHOW LINEAGE HISTORY FOR` variants.

---

## 5. Metadata inheritance

When the engine records a lineage entry for a derived column, it calls `LineageTracker.InheritMetadata()` to forward tags from the source columns to the destination. The inheritance rules:

1. Table-level metadata (lower priority) is merged first.
2. Column-level metadata (higher priority) overrides table-level keys.
3. When multiple sources contribute a `@d` (description) tag, the last non-null value wins and all individual descriptions are joined into `DerivedFromDescriptions` for audit purposes.
4. Keys from `GlobalMetadata` (script header tags) are added to every new entry but never override per-column values.

This means a column annotated `/* @pii: true */` in a source table will automatically propagate `pii: true` into all downstream derived columns without any explicit script changes.

---

## 6. Integration with Orchestrator execution history

`RelationalJobHistoryStore` implements both `IJobHistoryStore` (job run records) and `ILineageCatalogStore` (lineage catalog). `SQLiteJobHistoryStore` is now a thin SQLite wrapper over that relational store, while HA deployments use the PostgreSQL dialect through `NpgsqlOrchestratorDialect`.

```
Orchestrator state store
├── Jobs              (CREATE JOB schedule definitions)
├── JobHistory        (per-run start/end/status/rows)
└── LineageHistory    (cross-run lineage entries)
```

After each Orchestrator job run, the evaluator's full lineage is flushed via `ILineageCatalogStore.SaveLineageAsync()`. The `JobName` column in `LineageHistory` is populated from the Orchestrator job name, enabling `SHOW LINEAGE HISTORY FOR JOB` queries.

---

## 7. OpenLineage export

ETL-SQL supports emitting lineage in the [OpenLineage](https://openlineage.io/) specification for integration with Marquez, DataHub, or any OpenLineage-compatible catalog.

**File export** (one-shot from a script):
```sql
SHOW LINEAGE EXPORT AS OPENLINEAGE TO 'lineage/run.json';
```

**HTTP push** (automatic after every run):
```json
{
  "Lineage": {
    "OpenLineageEndpoint": "http://marquez:5000/api/v1/lineage"
  }
}
```

The exporter (`OpenLineageExporter`) builds a `RunEvent` per lineage entry, using the script's `@owner` tag as the job namespace. The `author` global metadata key is used as the job name fallback when no explicit namespace is provided.

---

## 8. Architecture summary

```
Script execution
      │
      ▼
  Evaluator
      │  calls LineageTracker.Record() for each DML
      ▼
  LineageTracker (in-memory, per-run)
      │
      ├─► SHOW LINEAGE ──────────────► Console / INTO #table
      │
      ├─► EXPORT TO '*.md' ──────────► Mermaid + audit log file
      │
      ├─► EXPORT AS OPENLINEAGE ─────► .json file
      │
      └─► ILineageCatalogStore ──────► Orchestrator state store
                                             (SQLite or PostgreSQL LineageHistory)
               │                             │
               └──────────────────────────────►  SHOW LINEAGE HISTORY
                                              │  (cross-run, by table/tag/job)
                                              └►  AT OrchestratorConn (remote)
```
