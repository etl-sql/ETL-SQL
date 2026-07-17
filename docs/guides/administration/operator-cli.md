# Operator CLI Commands

## 11. Operator CLI Commands

These commands replace manual operator runbooks with supported, repeatable CLI workflows.

### 11.1 First-time onboarding — `etl-sql init`

Scaffolds a starter workspace so a new operator can run something immediately without reading the
full documentation first:

```bash
# Scaffold into the current directory
etl-sql init

# Or into a named directory
etl-sql init my-workspace
```

It writes two files:

- **`appsettings.json`** — a minimal, valid starter configuration with safe defaults and a freshly
  generated Portal JWT secret (so the portal can start without a separate `config setup-jwt` step).
  No connector credentials are emitted.
- **`hello.etlsql`** — a first runnable script that queries the built-in `MOCKDB` sample connector,
  so it works with no external database.

`init` is **idempotent**: it never overwrites an existing file unless you pass `--force`. Re-running
it reports which files were created and which were skipped. After scaffolding it prints the next
steps (run the script, run `admin doctor`, read the User Manual).

### 11.2 Support archives — `etl-sql admin support-bundle`

Collects a single redacted archive an administrator can hand to support:

```bash
# Write a timestamped etl-sql-support-YYYYMMDD-HHMMSS.zip into the working directory
etl-sql admin support-bundle

# Or choose the output path
etl-sql admin support-bundle --output C:\temp\bundle.zip
```

The archive contains:

- **`manifest.json`** — bundle metadata (generated time, tool version, OS, .NET runtime; host and local paths are redacted).
- **`doctor-health.json`** — a full `doctor` health snapshot in machine-readable form.
- **`config-redacted.json`** — your `appsettings.json` with **all credentials redacted**.
- **`enterprise-diagnostics.json`** — enterprise enrollment, current-policy, cache/outbox, and
  security-event health metadata. It includes schema versions, enrollment/machine IDs, policy
  version/hash, issuance/expiry/load timestamps, governed key names, queue health, and local file
  sizes/timestamps. It does not include policy payload values, raw tenant names, policy endpoint
  hosts, signing public keys, certificate thumbprints, service identities, collector hosts, event
  targets, or credentials.
- **`database-metrics.json`** — Portal/Orchestrator database file sizes and last-write times; local paths are redacted.
- **`logs/`** — the most recent application and script log files, rewritten through the diagnostic redactor.

**Redaction contract:** every credential is masked (`***REDACTED***`) before anything is written —
passwords, JWT/at-rest/API keys, connection strings, tokens, and credentials embedded inside
connection-string values. Diagnostic text additionally strips URL query parameter values, local file
paths, email addresses, IP addresses, machine/user identifiers, and table-shaped rows that may contain
private data. Non-secret configuration knobs (timeouts, limits, key *versions*, feature flags) remain
visible for diagnostics. Empty secret fields are kept as empty so you can see whether a value was
configured. Always review a bundle before sharing it.

### 11.3 Backup and restore — `etl-sql admin backup` / `restore`

`etl-sql admin backup` packages the deployment into **two split-custody archives** so a single leaked
artifact can neither read nor decrypt the data:

```bash
# Stop the portal/orchestrator first so no writes are in flight, then:
etl-sql admin backup --output-dir D:\backups
```

- **`etl-sql-backup-<timestamp>.zip`** (data) — for single-node SQLite deployments, the Portal and
  Orchestrator SQLite databases (with their `-wal`/`-shm` sidecars), report snapshots, published
  report scripts, cached dataset parquet, map files, and an `appsettings.json` copy **with every
  secret value stripped out**. A `backup-manifest.json` records a backup id, the tool version, the
  catalog migration version, and a SHA-256 for every file.
- **`etl-sql-keys-<timestamp>.zip`** (keys) — the ASP.NET Data Protection key ring (`.portal-keys/`)
  and a `secrets.json` holding the stripped secrets (dataset at-rest key(s), JWT secret, etc.).

The two archives share a backup id and must be **stored in separate custody**. The data archive's
SMTP/Orchestrator secrets are Data-Protection-encrypted and its dataset caches are encrypted at rest —
neither can be read without the keys archive.

Enterprise machine enrollment (`Enterprise/enrollment.json`), the protected policy cache, and the
enrolled security-event outbox are host identity state and are not portable application artifacts.
Do not include them in reusable images or cross-environment restores. For same-machine bare-metal
recovery, restore them only with their original OS ACLs/permissions and validate with
`etl-sql enterprise status`; for replacement hosts, revoke the old machine identity and enroll a new
one. Portal audit rows and `AuditOutboxMessages` are database state and remain part of the Portal
backup set; change collector endpoints and credentials before starting a restored non-production
copy.

Restore validates before it writes, and **fails closed** on any mismatch:

```bash
# Verify integrity, key versions, and version compatibility WITHOUT writing anything
etl-sql admin restore --from data.zip --keys keys.zip --validate --report recovery-report.json

# Restore into a clean directory once validation passes
etl-sql admin restore --from data.zip --keys keys.zip --to D:\restore-target --report recovery-report.json
```

Validation checks that the two archives are a matching pair (same backup id), that the data archive's
at-rest key version is present in the keys archive, that every file matches its recorded checksum, and
that the backup was **not** produced by a newer release than the restoring binary. Restore
reconstructs the on-disk layout and re-injects the secrets into the restored `appsettings.json`; on the
next portal start, pending migrations apply automatically. Dataset caches referenced by **absolute**
path in the catalog must be restored to their original `DatasetRootPath` (or re-materialized) — see
[§6.5](../report-portal-admin.md#versioned-upgrades-and-rollback).
The optional `--report` path writes a machine-readable recovery report containing the backup id,
validation status, achieved RPO/data-loss window, missing dependencies, and required operator actions.

This is the auditable, supported alternative to the manual file-copy backup in §8 for single-node
deployments. In HA deployments, back up PostgreSQL with your database backup tooling and snapshot the
shared artifact roots/key ring as one coordinated recovery set.

### 11.4 Upgrading in place

ETL-SQL applies pending database schema migrations automatically on startup — the Portal runs EF Core
migrations against the configured Portal database, and the Orchestrator store adds any missing columns
when it initializes. Both are **forward-only**: an in-place N→N+1 upgrade preserves authentication,
folder permissions, jobs, subscriptions, datasets (and their at-rest key version), and audit history.

The full in-place upgrade procedure, the post-upgrade verification checklist, and the supported
rollback path (**restore-from-backup, not a down-migration**) are documented in
[Report Portal Admin Guide: Versioned Upgrades and Rollback](../report-portal-admin.md#versioned-upgrades-and-rollback).

This upgrade path is gated before every release tag by the **"N→N+1 upgrade-path drill"** phase in
`scripts/Test-PreRelease.ps1`, which seeds the previous release's schema, migrates forward over
populated data, and asserts continuity.

### 11.5 Migrating from SQLite to PostgreSQL — `etl-sql admin migrate-database`

SQLite is the default, single-node store. To run multiple Portal/Orchestrator nodes behind a load
balancer they must share **PostgreSQL**; `etl-sql admin migrate-database` copies your existing
single-node state into a Postgres deployment.

This is a **row copy, not a schema tool** — the target schema must already exist:

1. Provision PostgreSQL and set the target connection strings in `appsettings.json`
   (`Portal:Database:ConnectionString` and `Orchestrator:Database:ConnectionString`), but **leave each
   `Provider` on `Sqlite`** for now so the running nodes still read the old data.
2. Create the empty target schema: start the Portal once pointed at Postgres (it applies its EF
   migrations automatically), and let the Orchestrator initialize its store. *(Or apply the Portal
   migrations with `dotnet ef database update` against the Postgres connection.)*
3. Stop the portal/orchestrator so no writes are in flight, then verify and migrate:

```bash
# Verify row counts and target-schema compatibility WITHOUT writing anything
etl-sql admin migrate-database --from sqlite --to postgres --dry-run

# Perform the copy (target tables are cleared and repopulated)
etl-sql admin migrate-database --from sqlite --to postgres
```

The migrator reads the SQLite Portal and Orchestrator databases and copies every table into the
configured Postgres. Because EF Core maps the same model to **different physical types per provider**
(a `bool` is `INTEGER` in SQLite but `boolean` in Postgres; `DateTime`/`decimal`/`Guid` are `TEXT`
versus `timestamp`/`numeric`/`uuid`), each value is **coerced to the target column's type**. Foreign-key
enforcement is disabled for the load (`session_replication_role = replica`, which requires a
**privileged role** — run as the database owner/superuser; the tool fails closed with a clear message
otherwise), identity sequences are advanced past the copied keys, and **every table's row count is
verified** on both sides. Any mismatch rolls the whole transaction back — the migration is
all-or-nothing.

Once the migration succeeds, switch each `Provider` from `Sqlite` to `Postgres` and restart to cut over.
After cutover, configure every Portal node with the same shared artifact roots and key-ring path,
configure load-balancer affinity, and verify `GET /healthz` on each node before sending user traffic.

### 11.6 PostgreSQL HA soak operations — `etl-sql admin ha-soak`

The HA soak workflows are native admin CLI commands so operators do not need PowerShell or knowledge
of the repository's script layout. Capture the command transcript when running long soaks so failures
can be diagnosed later without monitoring the run live.

```bash
# Prepare a disposable topology run root without starting containers
etl-sql admin ha-soak prepare --run-id ha-20260710 --output-root .ha-soak-runs --force

# Materialize the sustained workload and operator artifacts
etl-sql admin ha-soak workload --run-root .ha-soak-runs/ha-20260710 --force
etl-sql admin ha-soak runbook --run-root .ha-soak-runs/ha-20260710 --mode ManualCertification --force
etl-sql admin ha-soak evidence --run-root .ha-soak-runs/ha-20260710 --force
etl-sql admin ha-soak large-job-plan --run-root .ha-soak-runs/ha-20260710 --mode ManualCertification --force
etl-sql admin ha-soak large-job-run --run-root .ha-soak-runs/ha-20260710 --force
etl-sql admin ha-soak fault-plan --run-root .ha-soak-runs/ha-20260710 --mode ManualCertification --force
etl-sql admin ha-soak fault-run --run-root .ha-soak-runs/ha-20260710 --force

# Capture post-run evidence or diagnostics
etl-sql admin ha-soak metrics --run-root .ha-soak-runs/ha-20260710 --force
etl-sql admin ha-soak diagnostics --run-root .ha-soak-runs/ha-20260710 --log-tail 1000 --force

# Validate completed sustained-load evidence before citing capacity claims
etl-sql admin ha-soak validate --run-root .ha-soak-runs/ha-20260710 --required-gate Sustained --markdown-report certification-results/postgres-ha-soak/ha-20260710/evidence-validation.md
```

Use `prepare --start --pull` only when you are ready to start the Docker topology. Generated env
files and local workload configs may contain disposable credentials or API keys; they belong in the
ignored run root, not source control. Developers and release maintainers still have script-level
contract tests in `scripts/README.md`, but administrators should use the `etl-sql admin ha-soak`
commands as the stable cross-platform interface. `large-job-run` writes `soak-report.json/.md`,
per-scenario `result.json/.md`, and `runner.log` files under
`certification-results/ha-large-job-soak/<run-id>` by default; use `--duration-seconds` for a short
diagnostic run. `fault-run` writes `fault-report.json/.md`, per-fault `fault-result.json/.md`,
`cleanup-invariants.json`, and `runner.log` files under
`certification-results/ha-fault-injection/<run-id>` by default. Use `validate --required-gate All`
only after sustained-load, large-job, and fault-injection measured reports exist; the native
`LargeJob` and `FaultInjection` gates cover bounded CI-smoke evidence, while release publication
still requires the longer operator-run evidence called out in `TODO.md`.
