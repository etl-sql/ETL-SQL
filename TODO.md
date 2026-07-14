# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`Docs/Operations/v0.15.0-flaky-tests.md` and `Docs/Operations/v0.15.0-performance-results.md`.

### Restore the 70% coverage gate

`ci.yml`'s threshold was lowered **70.0 -> 69.5** to ship v0.15.0 (landed at 69.8%). Analysis
from 2026-07-13 found that the v0.15.0 headline feature (`Core.Adaptive.*`) is already well-covered;
the remaining gap is infrastructure coverage.

- [ ] `App.*` runners (`WarmJobRunner`, `EnterpriseEnrollmentManager`, `DatabaseMigrationService`) are
      the biggest untested chunk but hardcode elevation checks, stores, and file I/O. Meaningful tests
      need a testability seam first, not error-path-only tests.
- [ ] Iterate CI-in-the-loop: add tests, push, read the CI coverage percentage (the authoritative
      scope; a local run excluding Portal reports around 50%, not comparable), repeat until >= 70.0,
      then restore the `ci.yml` threshold to **70.0**.

---

## Active Development

### Alerting and service objectives
- [x] Define baseline SLIs/SLOs and operator runbooks for Portal operational alert signals.
- [x] Emit structured operational alerts with severity, stable alert code, and runbook link through the operational digest.
- [x] Export active operational alert signals through Prometheus using low-cardinality labels for routing and deduplication.
- [x] Add configurable alert coverage for stale snapshots and datasets using deployment-specific freshness windows.
- [x] Add configurable alert coverage for expiring and expired active policy versions.
- [ ] Add alert coverage for signature failures, certificate expiry, unhealthy fleet nodes, and database pool exhaustion as those metrics become available.

### Historical capacity planning and sizing
- [x] Extend the native capacity report digest with 30-day daily rollup trend summaries, saturation indicators, and disk threshold forecasts.
- [x] Add Portal queue-wait vs. run-duration diagnosis from persisted execution timing so reports can flag likely execution-slot saturation.
- [x] Add durable hourly queue-depth and active-slot pressure rollups inferred from persisted Portal execution lifecycle rows.
- [x] Expand administrator documentation with examples of interpreting capacity trends and deciding between scale-up, scale-out, schedule changes, or workload repartitioning.

### Schema-Resilient Flat File Modes
- [x] Support `IGNORE_EXTRA_COLUMNS = ON`, `NULL_MISSING_COLUMNS = ON`, and `MAP_BY_HEADER_NAME = ON` in `FLATFILE` (CSV) connector.
- [x] Support the same schema resilience options in `EXCEL` connector.
- [x] Pass destination table columns as `templateSchema` in `BULK INSERT` statement execution.
- [x] Add unit tests in `FlatFileTests.cs` and `ExcelTests.cs` to verify positional mapping, header name mapping, extra columns ignoring, and missing columns nulling.
- [x] Reject duplicate source headers when `MAP_BY_HEADER_NAME = ON`; name-based mapping is ambiguous unless source header names are unique case-insensitively.
- [x] Add user-visible diagnostics for resilience behavior: ignored extra-column count, null-filled missing-column count, and affected row count where applicable.
- [x] Document `STRICT_SCHEMA` interaction with the resilience options and confirm `EXPECT SCHEMA` remains the downstream contract check for accepted temp-table shape.

---

## Configuration & Hardening Review (Hardcoded Values)

Track and address hardcoded values that should be backed by configuration settings (`appsettings.json`) or session `SET` commands:

- [x] **MetadataManager Schema Caching TTLs**:
      - `WarehouseCacheTtl` is a `private static readonly TimeSpan` hardcoded to `TimeSpan.FromMinutes(5)` in [MetadataManager.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Services/MetadataManager.cs#L21).
      - `SoftRefreshInterval` is hardcoded to `TimeSpan.FromMinutes(5)` in [MetadataManager.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Services/MetadataManager.cs#L26).
      - `DiskCacheMaxAge` is hardcoded to `TimeSpan.FromDays(14)` in [MetadataManager.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Services/MetadataManager.cs#L35).
      - *Task*: Bind these properties to a new configuration section or utilize `"Connectors:DataWarehouse:SchemaCacheTtlSeconds"` (currently defined in `appsettings.json` but ignored).
- [x] **Connector Default Command Timeouts**:
      - Database connectors (SQL Server, MySQL, ODBC, Oracle, Postgres, REST, SQLite) fall back to a hardcoded default command timeout of `30` seconds if not specified in connection options.
      - *Task*: Update data source constructors to read the configured `"Connectors:DataWarehouse:DefaultCommandTimeoutSeconds"` setting (currently `1800` in `appsettings.json` but ignored) instead of hardcoding `30`.
- [x] **Orchestrator SQLite Database Path**:
      - The configuration key `Orchestrator:DatabasePath` is read in [DependencyInjectionExtensions.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator/DependencyInjectionExtensions.cs#L205) but is missing from [appsettings.json](file:///C:/Users/chuck/scratch/ETL-SQL/src/appsettings.json).
      - *Task*: Add `DatabasePath` to the template `appsettings.json` to make the fallback folder path (`LocalApplicationData/ETL-SQL/etlsql.db`) transparent.
- [x] **Orchestrator API Keys Rotation Limit**:
      - Enforces a hard limit of `1` previous API key in [OrchestratorStartup.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Orchestrator.Service/OrchestratorStartup.cs#L107). Consider making the number of supported previous keys configurable.
- [x] **Configurable Custom Security Blacklists**:
      - `SecurityService.cs` hardcodes system blacklists (file extensions, restricted paths, and critical directories) to prevent configuration-level bypasses. However, administrators should be able to specify *additional* custom blacklisted paths and extensions in `appsettings.json` to lock down the engine further.
