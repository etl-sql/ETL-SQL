# Database Schema Standards

This document defines the naming conventions, migration policies, and architectural boundaries for internal relational databases across ETL-SQL subsystems.

---

## 1. Scope and Boundary Definition

ETL-SQL distinguishes between **internal subsystem databases** and **external target databases**:

- **Internal Subsystem Databases** (owned by ETL-SQL infrastructure):
  - **Portal Catalog & State** (`PortalDbContext` backed by SQLite or PostgreSQL).
  - **Orchestrator Job & Cluster Storage** (`SQLiteJobHistoryStore`, `RelationalJobHistoryStore`, `SqliteOrchestratorDialect`, `NpgsqlOrchestratorDialect`).
  - **Engine Virtual & Session Tables** (`eng.metrics`, `eng.security_events`, `eng.columns`, `eng.jobs`, and SQLite session audit tables). User-defined `#temp` identifiers are outside this convention.
  - **Rule**: All internal tables, columns, primary keys, foreign keys, and indexes MUST use strict `snake_case`.

- **External Target Databases** (user-defined or remote systems):
  - Remote tables accessed via connectors (MSSQL, PostgreSQL, Oracle, Snowflake, BigQuery, MySQL, SQLite, ODBC).
  - Target tables specified in `LOAD INTO`, `MERGE INTO`, or `CREATE TABLE` scripts.
  - **Rule**: External target schemas preserve the user-specified casing and connector dialect defaults. Never force internal `snake_case` onto external user tables.

---

## 2. Naming Conventions

All internal SQL identifiers follow standard lowercase snake_case:

| Identifier Type | Standard | Example |
| :--- | :--- | :--- |
| Table Name | Plural noun, lowercase, underscore separated | `jobs`, `job_history`, `shared_tenant_control_planes`, `sandbox_admissions` |
| Column Name | Descriptive noun or verb, lowercase, underscore separated | `job_id`, `created_at_utc`, `max_concurrent_jobs`, `is_enabled` |
| Primary Key Constraint | `pk_{table_name}` | `pk_jobs`, `pk_tenant_portability_cutovers` |
| Foreign Key Constraint | `fk_{source_table}_{target_table}_{column}` | `fk_job_schedules_jobs_job_id` |
| Index | `idx_{table_name}_{columns}` | `idx_jobs_sched`, `idx_jh_job_start` |
| Unique Constraint | `uq_{table_name}_{columns}` | `uq_tenant_usage_records_tenant_job` |

### 2.1 C# Mapping Utility

The canonical naming conversion is provided by `DatabaseNamingConvention.ToSnakeCase(string? input)` in the `ETL_SQL.Common` namespace of `ETL-SQL.Core`:
- Inserts an underscore between lowercase/digit and uppercase characters (`JobHistory` → `job_history`, `OAuth2Client` → `oauth2_client`).
- Collapses consecutive uppercase acronyms cleanly (`HTMLParser` → `html_parser`).
- Converts all characters to lowercase.

---

## 3. Portal EF Core Schema & Migrations

Portal uses Entity Framework Core to support both SQLite (`src/ETL-SQL.Portal.Data/`) and PostgreSQL (`src/ETL-SQL.Portal.Migrations.Postgres/`).

### 3.1 Model Builder Conventions

`PortalDbContext.OnModelCreating(ModelBuilder modelBuilder)` applies `DatabaseNamingConvention.ToSnakeCase` across all entity metadata:
- Table names (`entity.SetTableName(ToSnakeCase(tableName))`)
- Column names (`property.SetColumnName(ToSnakeCase(propertyName))`)
- Primary keys (`key.SetName(ToSnakeCase(keyName))`)
- Foreign keys (`fk.SetConstraintName(ToSnakeCase(fkName))`)
- Indexes (`index.SetDatabaseName(ToSnakeCase(indexName))`)

### 3.2 Provider Migrations

Both SQLite and PostgreSQL migration assemblies must remain in sync:
- Migrations are generated against `PortalDbContext` with each provider. SQLite migrations live in `ETL-SQL.Portal.Data`; PostgreSQL migrations live in `ETL-SQL.Portal.Migrations.Postgres`.
- Breaking renames must be registered in `MigrationConvergenceTests.PreDeploymentBreakingMigrations` so automated blue-green convergence tests accept them.

---

## 4. Orchestrator Storage & Forward Migration

Orchestrator uses direct relational storage through `IOrchestratorStoreDialect` implementations (`SqliteOrchestratorDialect` and `NpgsqlOrchestratorDialect`).

### 4.1 Schema DDL & Raw Queries

All DDL definitions in `SQLiteJobHistoryStore.InitializeAsync`, `RelationalSandboxAdmissionLedger`, `RelationalTenantMeteringLedger`, `SharedTenantLifecycleStore`, `TenantJobEvidenceStore`, and `JobThrottle` declare `snake_case` tables and columns:
- All queries, parameters, and ordinal mappings read snake_case columns (`reader.GetOrdinal("job_id")`, `reader.GetOrdinal("start_time")`).
- Case-insensitive column lookup does not bridge inserted underscores; legacy identifiers must be migrated before current queries run.

### 4.2 Automated Migration for Existing Databases

When an Orchestrator database initializes, it automatically detects and migrates legacy PascalCase tables using `OrchestratorSchemaMigration.MigrateLegacyTableAsync`:
- **SQLite Safe Renaming**: Because SQLite table names are case-insensitive, a direct `ALTER TABLE "Jobs" RENAME TO "jobs"` fails with a duplicate table error. The migration helper executes a two-step rename (`Jobs` → `_tmp_snake_jobs` → `jobs`).
- **PostgreSQL Folding**: Legacy unquoted names were folded to lowercase (`JobHistory` became `jobhistory`). The migration recognizes both preserved and folded table names and uses a fixed map of legacy column spellings to restore word boundaries (`jobid` becomes `job_id`).
- **Column Renaming**: Existing columns with PascalCase names are safely altered to snake_case (`ALTER TABLE "jobs" RENAME COLUMN "JobId" TO "job_id"`).
- **Atomicity and Refusal**: Each table's two-step rename and column renames run in one transaction on both providers. Failure rolls back and stops initialization. Coexisting legacy/canonical tables and colliding column names are refused for explicit reconciliation; cancellation is propagated.

---

## 5. Virtual Catalog & Temporary Tables

The engine runtime exposes virtual tables under the `eng.*` schema and creates temporary SQLite session tables:
- **Virtual Catalogs**: `eng.jobs`, `eng.metrics`, `eng.security_events`, `eng.columns`, `eng.lineage`, `eng.locks` all provide snake_case columns.
- **Session Tables**: Audit and control tables (`security_events`, `variables`) created within session storage use snake_case definitions.
- **User Temporary Tables**: Tables prefixed with `#` (e.g. `#staging_data`) are created with columns matching the user script's projection. The engine does not rewrite user `#temp` table column names.
