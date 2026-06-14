# ETL-SQL Administrator's Guide

This guide is for operators who install, configure, back up, and monitor ETL-SQL in production or shared test environments. For day-to-day portal administration, see [ReportPortal_Administrators_Guide.md](ReportPortal_Administrators_Guide.md). For command-line job operations, see [Orchestrators_Guide.md](Orchestrators_Guide.md). For Report Portal and Orchestrator server sizing, see [Operations/Capacity_Planning.md](Operations/Capacity_Planning.md).

---

## 1. Deployment Components

ETL-SQL can be deployed as workstation tooling, server services, or both.

| Component | Purpose | Typical host |
| :--- | :--- | :--- |
| Workstation SDK | `ETL-SQL` CLI, terminal IDE, language server, and report tooling for script authors | Developer workstations, CI runners |
| Orchestrator Service | Background scheduler and job execution service | Application server |
| Report Portal | Web application for report catalog, snapshots, subscriptions, and administration | Application server |

The Orchestrator and Report Portal may run on the same host or on separate hosts. In separate-host deployments, configure the portal with the orchestrator API URL and shared API key. Use [Operations/Capacity_Planning.md](Operations/Capacity_Planning.md) when deciding whether to start shared or split the services.

---

## 2. Production Installation

### Windows

1. Run the `ETL-SQL-Enterprise-v0.10.0.msi` installer.
2. Select the workstation and server features required for the host.
3. The installer registers these Windows services when the server features are selected:
   - `ETL-SQL-Orchestrator`
   - `ETL-SQL-Portal`
4. Review the service accounts before production use. The installer default is `LocalSystem`; use a least-privilege domain or local service account when the service needs access to network shares, database drivers, certificates, or controlled script roots.

### Linux

Install the package for your distribution, then enable the services you intend to run:

```bash
sudo dpkg -i etl-sql_0.10.0_amd64.deb
sudo systemctl enable etl-sql-orchestrator
sudo systemctl start etl-sql-orchestrator
sudo systemctl enable etl-sql-portal
sudo systemctl start etl-sql-portal
```

For RPM-based systems, use the matching `.rpm` package and the same `systemctl` service names.

### Docker / Containerized

ETL-SQL provides pre-configured Docker Compose configurations to run containerized instances of the Orchestrator and Report Portal services.

1. **Pull-Based Deployments (Operator Workflow)**:
   The central [docker-compose.yml](../docker-compose.yml) file is structured for container registry pulls. It references pre-built images:
   - `etl-sql/orchestrator:latest` (runs on port `5001`)
   - `etl-sql/report-portal:latest` (runs on port `5000`)

   Deploying this configuration only requires copying `docker-compose.yml` to your host server and running:
   ```bash
   docker compose up -d
   ```
   *Note: This workflow does not require the C# source tree or SDK tooling to be installed on the host.*

2. **Persistence and Volumes**:
   The compose file exposes volume binds to preserve runtime data on the host machine:
   - `./data` — Holds the portal's SQLite catalog database (`portal.db`)
   - `./Reports` — Directory for uploaded ETL scripts and report queries
   - `./Snapshots` — Storage for generated report extracts and snapshots
   - `./logs/orchestrator` — Background execution log output

3. **Development Builds (Source Override)**:
   If you have the source tree cloned locally and need to test code modifications inside the containers, use the [docker-compose.override.yml](../docker-compose.override.yml) file. When Docker Compose finds this file alongside the main compose config, it automatically overrides the registry images and compiles the local C# code via multi-stage builds.

### First-Run Checklist

Before exposing the services to users:

1. Set a production JWT secret for the portal.
2. Set an orchestrator API key if the management API is reachable beyond a loopback-only or isolated internal network.
3. Configure HTTPS certificates or place the services behind a TLS-terminating reverse proxy.
4. Set script, snapshot, dataset, and map root directories to dedicated service-owned folders.
5. Confirm backup coverage for portal and orchestrator SQLite databases.
6. Run a simple `MOCKDB` script and a sample report from the service account context.

---

## 3. Configuration Files

The published services read `appsettings.json`, environment variables, and encrypted configuration values. Production templates live beside the service projects:

| Service | Template |
| :--- | :--- |
| Orchestrator | `src/ETL-SQL.Orchestrator.Service/appsettings.Production.json.template` |
| Report Portal | `src/ETL-SQL.ReportPortal/appsettings.Production.json.template` |

Common environment-variable overrides use .NET's double-underscore convention:

```text
Portal__DatabasePath=C:\ETL-SQL\data\portal.db
Portal__ScriptRootPath=C:\ETL-SQL\scripts
Portal__SnapshotDirectory=C:\ETL-SQL\snapshots
Portal__Orchestrator__ApiUrl=https://orchestrator.example.com:5003
Portal__Orchestrator__ApiKey=your-shared-secret
Orchestrator__ApiKey=your-shared-secret
Orchestrator__ScriptRoot=C:\ETL-SQL\scripts
Jobs__UseProcessSpawning=true
Jobs__ExecutablePath=C:\Program Files\ETL-SQL\bin\ETL-SQL.exe
```

Use environment variables or deployment-secret tooling for values that should not be written to disk in plaintext.

---

## 4. Security & Secret Management

ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate passwords, and connection strings. Encrypted values use the `ENC:` prefix.

### 4.1 Encrypting Secrets

Encrypt a value with an explicit master password:

```bash
ETL-SQL encrypt "my-secret-password" --pass "YourMasterKey"
```

The CLI also supports machine-bound encryption when no password is supplied. That is convenient for local services, but the encrypted value will not be portable if the machine key changes or the configuration is moved to another host.

### 4.2 Portal JWT Secret

The Report Portal requires a strong JWT secret. Generate one during deployment:

```bash
ETL-SQL config setup-jwt --update
```

> [!CAUTION]
> Record the plaintext secret in a password manager or deployment vault. If it is stored only as an encrypted value and the machine key is lost, the plaintext cannot be recovered.

For a non-disruptive rotation, place the replacement in `Portal__Jwt__Secret` and retain the old
value temporarily as `Portal__Jwt__PreviousSecrets__0`. The portal signs only with the current secret
and validates against both. Remove the previous value after the maximum access-token lifetime has
elapsed. Removing it sooner intentionally invalidates access tokens signed by that key.

### 4.3 Orchestrator API Key

A shared API key protects every Orchestrator route that submits, cancels, inspects, schedules, or manages jobs — including the ad-hoc execution routes `POST /jobs`, `DELETE /jobs/{id}`, and `GET /jobs/{id}`. Only the unauthenticated probes `GET /health` and `GET /metrics` are exempt. The portal sends the key in the `X-Orchestrator-Key` request header.

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "C:\\ETL-SQL\\scripts"
  }
}
```

The installers (MSI custom action and Linux `postinst`) generate a random `Orchestrator:ApiKey` on first install and mirror it to `Portal:Orchestrator:ApiKey` so the two halves match out of the box.

Rotate without downtime by first adding the new key to `Orchestrator__PreviousApiKeys__0`, restarting
the Orchestrator, switching `Portal__Orchestrator__ApiKey` to the new key, then making the new key
current on the Orchestrator while retaining the old key temporarily in `PreviousApiKeys`. Remove the
old key after every caller has moved. The service compares fixed-length key digests in constant time.

> [!IMPORTANT]
> **The Orchestrator refuses to start unauthenticated on a network-reachable address.** If `Orchestrator:ApiKey` is empty *and* the service binds to a non-loopback address (for example `http://*:5001` or `http://0.0.0.0:5001`), startup fails fast with an actionable error. Configure a key, or bind the service to loopback only (`http://127.0.0.1:5001`). An empty key is permitted **only** for loopback-only bindings, which is development/isolated-host behavior.

---

## 5. HTTPS & Network Configuration

Both the Orchestrator and Portal use Kestrel. The production templates define these defaults:

| Service | HTTP | HTTPS |
| :--- | :--- | :--- |
| Report Portal | `5000` | `5002` |
| Orchestrator Service | `5001` | `5003` |

Configure certificates directly in Kestrel or terminate TLS at a reverse proxy:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://*:5002",
      "Certificate": {
        "Path": "C:\\Certs\\etl-sql.pfx",
        "Password": "ENC:ENCRYPTED_PFX_PASSWORD"
      }
    }
  }
}
```

When the Portal and Orchestrator run on different servers, configure the Portal with the Orchestrator's reachable URL:

```json
"Portal": {
  "Orchestrator": {
    "ApiUrl": "https://orchestrator-server:5003",
    "ApiKey": "your-shared-secret"
  }
}
```

The same values can be set through the Portal Admin UI under **Admin -> Settings -> Orchestrator Connection**. UI-saved values are written to a `portal-orchestrator.json` sidecar file next to the portal database and take precedence over startup configuration.

For report execution, the Orchestrator returns the completed report manifest over the authenticated
job-status API and the Portal writes it under `Portal:SnapshotDirectory`. Separate-host deployments do
not require a shared snapshot filesystem, but both services must have the same non-empty API key so
report data is never returned from the backward-compatible unauthenticated job-status surface. After
configuration, execute a small report and confirm its snapshot manifest and CSV export are available.

### Same-Host Service Start

On Windows, if the Portal and Orchestrator run on the same host, the portal can start the Orchestrator service through `ServiceController` when it is offline:

```json
"Portal": {
  "Orchestrator": {
    "SameHost": true
  }
}
```

Leave `SameHost = false` for separate-server deployments.

---

## 6. Portal Data Roots

The Report Portal constrains filesystem access to configured roots. Set these to service-owned directories rather than broad user folders:

| Setting | Purpose | Default in code |
| :--- | :--- | :--- |
| `Portal:DatabasePath` | Portal SQLite database | `./portal.db` |
| `Portal:ScriptRootPath` | Report and job script browser root | `./Reports` |
| `Portal:SnapshotDirectory` | Report snapshot output | `./Snapshots` |
| `Portal:DatasetRootPath` | Dataset files managed by the portal | `./data/datasets` |
| `Portal:MapRootPath` | Map assets used by reports | `./data/maps` |

The portal rejects script, snapshot, map, and dataset paths that resolve outside their configured roots.

---

## 7. Resource Controls

Use resource settings to keep one report or job from consuming the whole host.

### Orchestrator Lockbox Bundles

Published Orchestrator bundles are stored in the Orchestrator SQLite database as immutable versions. Back up the database together with any configured lockbox key material.

| Mode | Operational note |
|---|---|
| `ENCRYPT = MACHINE` | Default single-host mode. Bundle secrets are protected by the Orchestrator host identity. Restoring to another host may require republishing or re-entering secrets. |
| `ENCRYPT = KEYFILE` | Portable/cluster-friendly mode. Back up the keyfile separately from the database and restrict file permissions to the Orchestrator service account. |

Do not delete bundle versions referenced by active or historical jobs unless the retirement is deliberate and audited. `EXPORT SCRIPT` can recover script text and folder structure from a published bundle, but it will not reveal decrypted secrets.

### Portal Report Execution

```json
"Portal": {
  "Resources": {
    "MaxConcurrentReportExecutions": 4,
    "ExecutionTimeoutSeconds": 300,
    "SessionCacheMaxSize": 50,
    "SessionCacheTtlMinutes": 30
  }
}
```

### Orchestrator Job Execution

```json
"Jobs": {
  "UseProcessSpawning": true,
  "MaxParallelJobs": 8,
  "ExecutablePath": "C:\\Program Files\\ETL-SQL\\bin\\ETL-SQL.exe"
}
```

Use process spawning for production isolation. In-process execution is useful for development and fallback scenarios, but it gives jobs less isolation from the service process.

### Engine Defaults

Scripts can override some engine behavior, but administrators can set defaults in `appsettings.json`:

```json
"Engine": {
  "CaseSensitiveComparison": false,
  "LineageEnabled": true,
  "TelemetryEnabled": true
}
```

### Lineage and OpenLineage Configuration

Lineage tracking and automatic exports to OpenLineage endpoints or files can be configured in the `Lineage` block:

```json
"Lineage": {
  "Namespace": "etl-sql",
  "OpenLineageFile": "logs/lineage/openlineage.jsonl",
  "OpenLineageEndpoint": "http://localhost:5000/api/v1/lineage",
  "ImportCatalogMetadata": false
}
```

* **Namespace**: The default namespace name representing the running job (defaults to `"etl-sql"`). Can be overridden ad-hoc in scripts using `SET LINEAGE_NAMESPACE = '...'`.
* **OpenLineageFile**: Optional file path to append OpenLineage events to.
* **OpenLineageEndpoint**: Optional HTTP endpoint to post OpenLineage events to.
* **ImportCatalogMetadata**: Reads native column metadata — comments/descriptions, data type, nullability, primary-key status — from the SQL Server, PostgreSQL, and MySQL catalog providers and folds it into lineage. A column's database comment becomes the column's **lineage description**, so it **inherits onto derived columns** (e.g. `SUM(Amount) AS total` carries Amount's comment) and surfaces in the report portal's structure/lineage views.

> **⚠ Off by default to minimize latency.** When enabled, the engine issues catalog queries (`sys.extended_properties`, `pg_catalog.col_description`, `information_schema … COLUMN_COMMENT`) against each distinct source table the first time it is read. That adds round-trips and requires catalog read permission, so it is **disabled by default**. Enable it only where you want database comments in lineage.
>
> **Enable it two ways:**
> - **Globally** — set `"ImportCatalogMetadata": true` in the `Lineage` block of `appsettings.json`.
> - **Per script** — `SET LINEAGE_IMPORT_CATALOG = ON;` (and `= OFF;` to disable again) at the top of a script. The `SET` command overrides the config value for that run.
>
> Only SQL Server, PostgreSQL, and MySQL connectors currently expose a catalog provider; other connectors are unaffected.


### User Snippet Templates

To deploy team-standard snippet templates that appear in the TUI and VS Code autocomplete alongside the built-in `$trigger` templates, configure a shared directory:

```json
"Snippets": {
  "UserSnippetsPath": "C:\\SharedConfig\\etlsql-snippets"
}
```

Each `.md` file in the directory must follow the standard snippet frontmatter format:

```markdown
---
trigger: $myconn
label: Production DB Connection
description: Company-standard production database connection
---
CREATE CONNECTION «ConnName» AS MSSQL(
  SERVER             = '«prod-sql01.example.com»',
  DATABASE           = '«database»',
  TRUSTED_CONNECTION = ON
);
```

User snippets with the same trigger as a built-in override the built-in. The directory is loaded once at startup; restart the application to pick up changes. The path can be a UNC share for team-wide deployment (`\\fileserver\etlsql\snippets`). See [User_Manual.md §15.6](User_Manual.md#156-snippet-templates) for the full authoring reference.

---

## 8. Backup & Maintenance

### Databases

| Database | Typical path | Backup guidance |
| :--- | :--- | :--- |
| Portal SQLite DB | `Portal:DatabasePath` | Stop the portal or use SQLite online backup / `VACUUM INTO`. |
| Orchestrator SQLite DB | Orchestrator data directory | Stop the orchestrator or use SQLite online backup / `VACUUM INTO`. |

Back up the portal sidecar files, script roots, snapshots, datasets, map roots, and service configuration alongside the databases. A portal restore without the script root or snapshot directory is incomplete.

### Logs

Default log locations vary by deployment, but the bundled services write application logs under `logs/` unless overridden:

| Service | Common default |
| :--- | :--- |
| CLI / workstation | `logs/app`, `logs/scripts` |
| Orchestrator | `logs/orchestrator` |
| Portal | ASP.NET logs plus configured host/service logs |

Set log retention and size limits in configuration where supported, and make sure service accounts can write to the chosen directories.

### Deleting all data

To wipe every piece of ETL-SQL runtime data consistently — reports, snapshots, the portal and orchestrator databases, logs, persistent sessions, and portal data directories — use the built-in purge command. It resolves the actual configured locations (and the `LocalApplicationData` defaults for sessions and orchestrator history), so it works the same whether ETL-SQL was installed by the Windows MSI, the Linux `.deb`, the macOS bundle, or run ad hoc.

```bash
# Preview exactly what would be deleted, with sizes — deletes nothing
etl-sql purge --dry-run

# Delete after an interactive confirmation
etl-sql purge

# Non-interactive (scripts / uninstall automation)
etl-sql purge --yes
```

> [!CAUTION]
> `etl-sql purge` permanently deletes all reports, snapshots, databases, logs, and sessions. It cannot be undone. Back up anything you need first (see **Databases** above). Stop the Portal and Orchestrator services before purging so database files are not locked or recreated.

The Windows MSI uninstaller and the Linux `.deb` purge step still remove this same data automatically when you opt in during uninstall; `etl-sql purge` gives you the same cleanup on demand and on platforms without an uninstall wizard.

---

## 9. Operational Checks

After installation or upgrade:

1. Start both services and confirm they remain running.
2. Confirm the Portal can reach the Orchestrator from **Admin -> Settings -> Orchestrator Connection**.
3. Confirm the Orchestrator API rejects unauthenticated calls when `Orchestrator:ApiKey` is configured.
4. Run a small scheduled job using `MOCKDB`.
5. Publish and execute a small report, then confirm the snapshot is written under `Portal:SnapshotDirectory`.
6. Confirm logs, backup jobs, and monitoring checks are collecting the expected files.

For report catalog, user, group, ACL, subscription, snapshot, and export operations, continue in [ReportPortal_Administrators_Guide.md](ReportPortal_Administrators_Guide.md).

---

## 10. Environment Validation with `etl-sql doctor`

The `etl-sql doctor` command is a built-in health check that validates the most common setup problems before you begin using the environment. It is also available as **`etl-sql admin doctor`** — the same check under the `admin` command group, alongside `admin support-bundle` (§11.2). The top-level `etl-sql doctor` spelling is retained for backward compatibility and IDE integration; both accept the same `--profile`, `--strict`, and `--json` options.

### Quick check (default)

```bash
etl-sql doctor
```

Runs immediately (no database or network required) and prints a status table covering:

- OS and .NET runtime version
- Write access to the base directory, temp directory, and log directories
- Available disk space on the app drive
- ODBC driver manager presence
- `appsettings.json` present and readable
- Security authorized-hosts count
- Connector registry loaded
- Orchestrator history DB path configured

### Full check

```bash
etl-sql doctor --profile full
```

Adds smoke tests and optional endpoint probes that take a few seconds but exercise the runtime itself:

- Parses a trivial script
- Runs a live MOCKDB query through the engine
- Verifies the `ENC:` encrypt/decrypt round-trip
- Runs the linter on a simple script
- Verifies the security path guardrail
- Builds a small Report-SQL manifest and PDF payload
- Checks optional Graphviz/browser capability, shared asset drift, Node.js, and portal DB configuration
- Probes configured Report Portal `/health`, Orchestrator `/health`, SMTP, SFTP, and Azure Blob endpoints

### CI and monitoring integration

```bash
# Fail the CI step if any check is WARN or FAIL
etl-sql doctor --strict

# Machine-readable output for monitoring scripts
etl-sql doctor --json

# Deep validation during release pipeline or first-time host setup
etl-sql doctor --profile full --strict --json
```

**Recommended use:**
- Run `etl-sql doctor` as the first step of any new host setup or post-upgrade verification.
- Add `etl-sql doctor --strict` to the service startup validation in your CI/CD pipeline.
- Use `etl-sql doctor --json` to feed a monitoring system that alerts on WARN/FAIL status.
- See the [Production Readiness Checklist](ReportPortal_Administrators_Guide.md#14-production-readiness-checklist) in the portal admin guide for the full go-live gate.

---

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

- **`manifest.json`** — bundle metadata (generated time, tool version, OS, .NET runtime, host).
- **`doctor-health.json`** — a full `doctor` health snapshot in machine-readable form.
- **`config-redacted.json`** — your `appsettings.json` with **all credentials redacted**.
- **`database-metrics.json`** — Portal/Orchestrator database file paths, sizes, and last-write times.
- **`logs/`** — the most recent application and script log files.

**Redaction contract:** every credential is masked (`***REDACTED***`) before anything is written —
passwords, JWT/at-rest/API keys, connection strings, tokens, and credentials embedded inside
connection-string values. Non-secret configuration knobs (timeouts, limits, key *versions*, feature
flags) remain visible for diagnostics. Empty secret fields are kept as empty so you can see whether a
value was configured. Always review a bundle before sharing it.

### 11.3 Upgrading in place

ETL-SQL applies pending database schema migrations automatically on startup — the Portal runs EF Core
migrations against `portal.db`, and the Orchestrator store adds any missing `etlsql.db` columns when it
initializes. Both are **forward-only**: an in-place N→N+1 upgrade preserves authentication, folder
permissions, jobs, subscriptions, datasets (and their at-rest key version), and audit history.

The full in-place upgrade procedure, the post-upgrade verification checklist, and the supported
rollback path (**restore-from-backup, not a down-migration**) are documented in
[ReportPortal_Administrators_Guide.md §6.5 → "Versioned Upgrades and Rollback"](ReportPortal_Administrators_Guide.md#versioned-upgrades-and-rollback).

This upgrade path is gated before every release tag by the **"N→N+1 upgrade-path drill"** phase in
`scripts/Test-PreRelease.ps1`, which seeds the previous release's schema, migrates forward over
populated data, and asserts continuity.
