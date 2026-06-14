# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Operator Tooling

> Status: **active.**
> Goal: replace manual operator runbooks with first-class, supported CLI commands. An administrator
> should be able to diagnose, bundle support data, back up, restore, upgrade, and onboard from the
> command line without hand-editing files or following a wiki page.
>
> Priority convention: **P1** the supported operator path that must exist and be safe before the
> capability can be claimed; **P2** verification, hardening, and ergonomics around that path.
>
> Verified-against-code baseline (2026-06-13):
> - `etl-sql doctor` already exists (top-level, `--profile quick|full`, `--json`, `--strict`) in
>   `EngineRunner.RunDoctor` — substantial environment + smoke coverage already shipped.
> - Portal already auto-applies EF migrations on startup in `src/ETL-SQL.ReportPortal/Program.cs`
>   (was `Database.Migrate()`; P1.7 changed it to `MigrateAsync()` with pending-set logging + fail-fast);
>   Orchestrator SQLite store self-migrates via its `ALTER TABLE ADD COLUMN` sweep in
>   `SQLiteJobHistoryStore.InitializeAsync`.
> - The N→N+1 in-place upgrade **drill** already exists (`UpgradePathDrillTests`, shipped in v0.11.0);
>   only the release-gate wiring is outstanding.

### Phase 1 — System Diagnostics

- [x] **P1.1 Introduce an `etl-sql admin` command group and route `doctor` under it.**
  Add an `admin` parent command. Expose `admin doctor`, `admin support-bundle`, and (Phase 2)
  `admin backup`/`admin restore` as subcommands. Keep the existing top-level `etl-sql doctor`
  working as a backward-compatible alias (it is special-cased in `Program.cs` and used by IDEs).
  *(done)* `CliOrchestrator` now has an `admin` command group; `BuildDoctorCommand` mints a fresh
  doctor `Command` for both the top-level alias and `admin doctor` (a System.CommandLine `Command`
  cannot have two parents). `admin doctor` dispatches to the same `"doctor"` handler — full parity.
  `Program.cs` treats `admin` and `init` as one-shot (no scheduler). Tests:
  `CliOrchestratorTests.CliOrchestrator_AdminDoctorRoutesToDoctorCommand`.
- [x] **P1.2 Implement `etl-sql admin support-bundle`.**
  Produce a single redacted archive an administrator can hand to support: system/runtime config,
  the `doctor` health snapshot, recent logs, and database metrics. **Redact all credentials** before
  anything is written.
  *(done)* New `SupportBundleBuilder` writes a `.zip` containing `manifest.json`, `doctor-health.json`
  (captured from the full `doctor` JSON snapshot), `config-redacted.json`, `database-metrics.json`
  (Portal/Orchestrator DB file path/size/mtime), and recent `logs/`. The recursive JSON redactor
  masks secret-keyed values, fully masks string leaves inside secret containers (`PreviousSecrets`,
  `PreviousAtRestKeys`, `ConnectionStrings`), masks credentials embedded in connection-string values,
  and **leaves non-secret knobs visible** (numbers/bools, `*Version`/`*Note`/`*Limit`/`*Seconds`
  suffixes) for diagnostics. Default output is a timestamped zip in the cwd; `--output` overrides.
  Tests: `OperatorToolingTests` (marker secret never appears; knobs preserved; embedded-credential
  masking; empty-secret preservation). **The support-bundle's DB metrics are file-level only** (path/
  size/mtime — the bundle does not open SQLite); row-count/migration-version surfacing is deferred to
  Phase 3 (P2.3). (Phase 2's backup later added `Microsoft.Data.Sqlite` to the App project for a
  read-only catalog-version read, but the support bundle does not use it.)
- [x] **P1.3 Implement `etl-sql init`.**
  Scaffold a starter configuration + first script, idempotent and safe to re-run.
  *(done)* New `InitScaffolder` writes `appsettings.json` (minimal valid config + freshly generated
  256-bit JWT secret, no connector creds) and `hello.etlsql` (queries built-in `MOCKDB`, needs no
  external DB). Idempotent: skips existing files unless `--force`; reports created/skipped and prints
  next-steps (run script, `admin doctor`, User Manual). Tests: `OperatorToolingTests`
  (create + generated JWT, no-clobber idempotency, `--force` regenerates a fresh secret).
- [x] **P2.1 Tests + docs for Phase 1.**
  *(done)* 5 new `OperatorToolingTests` + 3 new `CliOrchestratorTests` (19 in the Phase-1 filtered run, all
  green). Documented in `Docs/Administrators_Guide.md` §10 (admin doctor alias) and a new §11
  (`init`, `admin support-bundle` with the redaction contract), and in the CLI `ShowAdvancedHelp`
  table. **Verified manually**: `init` scaffolds + the generated `hello.etlsql` runs; `admin doctor`
  renders; `admin support-bundle` produces a valid zip with redacted secrets.

### Phase 2 — Backup and Disaster Recovery

- [x] **P1.4 Implement `etl-sql admin backup`.**
  *(done)* New `BackupRestoreService.BackupAsync`/`BackupCoreAsync` packages Portal + Orchestrator
  SQLite databases (`.db` + `-wal`/`-shm` sidecars for a consistent cold copy), report snapshots,
  published report scripts, cached dataset parquet, and map files into an `etl-sql-backup-<ts>.zip`,
  with a `backup-manifest.json` recording a backup id, app version, catalog migration HEAD (read
  read-only from `__EFMigrationsHistory` via `Microsoft.Data.Sqlite`), and a SHA-256 per file.
  `--output-dir` overrides the destination. Paths resolve via the same config keys as
  `DataPurgeService`.
- [x] **P1.5 Enforce split-custody key handling.**
  *(done)* Backup emits **two** archives sharing a backup id: the data archive above (config copy has
  every secret value stripped) and `etl-sql-keys-<ts>.zip` holding the ASP.NET Data Protection key
  ring (`.portal-keys/`) + a `secrets.json` of the stripped secrets (dataset at-rest key(s), JWT
  secret, etc.). The data archive's SMTP/API secrets are Data-Protection-encrypted and its dataset
  caches are encrypted at rest, so neither archive alone can decrypt the other's material. Secret
  detection reuses `SupportBundleBuilder.IsSecretKey` (one source of truth). Documented in admin
  guide §11.3.
- [x] **P1.6 Implement `etl-sql admin restore --validate`.**
  *(done)* `BackupRestoreService.RestoreAsync` always validates first and **fails closed**: matching
  backup-id pair, at-rest key version present in the keys archive, every file matches its manifest
  checksum, and the backup app version is not newer than the restoring binary. `--validate` is
  verify-only (writes nothing); otherwise `--to <dir>` materializes the layout and re-injects secrets
  into the restored `appsettings.json`. The dataset absolute-path caveat is surfaced in output and
  admin guide §6.5/§11.3.
- [x] **P2.2 Backup/restore drill + docs.**
  *(done)* `BackupRestoreServiceTests` (fast lane) drives the real service: split (secrets only in the
  keys archive), full round-trip (`--validate` then `--to`, secrets re-injected, layout materialized),
  fail-closed on a mismatched archive pair, and `SplitConfigSecrets` path capture. CLI parsing covered
  in `CliOrchestratorTests`. Manually verified `admin backup` + `admin restore --validate` end to end.
  Operator procedure + split-custody contract documented in `Administrators_Guide.md` §11.3.
  **Note:** added `Microsoft.Data.Sqlite` as a direct App dependency (already in CPM + a product dep via
  Orchestrator — no new third-party license).

### Phase 3 — Database Migrations

- [x] **P1.7 Confirm and formalize automatic SQLite migrations on startup/upgrade.**
  *(done)* Verified both already run on startup/upgrade (Portal `Database.Migrate()`; Orchestrator
  `ALTER TABLE` sweep). Formalized the Portal startup block (`Program.cs`): it now logs the **pending
  migration set before applying** and a success line after (`PortalDatabaseMigration` logger), and
  **fails fast** — a migration exception is logged `Critical` with the restore-from-backup guidance and
  rethrown so the host never serves requests against a half-migrated catalog.
- [x] **P2.3 Migration status surface + tests.**
  *(done)* `OperationalMetricsService` (admin `GET /api/admin/metrics/operational`) now reports
  `appliedMigrations`, `pendingMigrations`, `lastAppliedMigration`, and `schemaUpToDate` so an operator
  confirms a full upgrade (`pendingMigrations: 0`) without shell access — documented in the portal admin
  guide §6.7. (Portal migration status is surfaced by the Portal, not the CLI `doctor`, because the App
  process does not reference `PortalDbContext`; the support-bundle's file-level DB metrics remain.) New
  `MigrationConvergenceTests` proves a **fresh** DB and an **out-of-date** DB (seeded at the previous
  release) both converge to HEAD on `MigrateAsync` with `pendingMigrations` reported as 0.

### Phase 4 — N→N+1 Upgrade Validation

- [x] **P1.8 Wire the in-place upgrade drill into `Test-PreRelease.ps1`.**
  *(done)* Added a dedicated **"N→N+1 upgrade-path drill"** phase (in both `Get-PlannedPreReleasePhases`
  for `-Explain` and the execution block, right after the Fast lane) that runs
  `dotnet test ETL-SQL.ReportPortal.Tests --filter FullyQualifiedName~UpgradePathDrillTests`. The fast
  lane already exercises it (it is `Category=Portal`), but the named phase makes the upgrade gate
  visible and independently logged so it can never be silently lost. Verified: the phase shows in
  `-Explain`, and the filter selects + passes the 2 drill tests.
- [x] **P2.4 Document the supported upgrade + rollback procedure.**
  *(done)* The full procedure already lives in `ReportPortal_Administrators_Guide.md` §6.5 "Versioned
  Upgrades and Rollback" (forward-only migration; rollback = restore-from-backup, not down-migration).
  Added `Administrators_Guide.md` §11.4 cross-linking it and naming the release-gate drill phase.

---

## Verification Notes

- The non-Docker Portal run passed **226 tests** on June 14, 2026 (a pre–Operator Tooling snapshot;
  Operator Tooling has since added Portal tests — e.g. `MigrationConvergenceTests` — so the current
  count is higher; not re-run in full here).
- The documentation, parser, and portal syntax verification run passed **60 tests** (pre–Operator
  Tooling snapshot; not re-run here).
- True dual-node/process, network-partition, disk-pressure, clock-skew, and distributed workload
  fairness certification belongs to the Practical High Availability phase in `ROADMAP.md`, because
  v0.11.0 intentionally supports one active Portal process per SQLite database.
