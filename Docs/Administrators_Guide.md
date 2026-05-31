# ETL-SQL Administrator's Guide

This guide is for operators who install, configure, back up, and monitor ETL-SQL in production or shared test environments. For day-to-day portal administration, see [ReportPortal_Administrators_Guide.md](ReportPortal_Administrators_Guide.md). For command-line job operations, see [Orchestrators_Guide.md](Orchestrators_Guide.md).

---

## 1. Deployment Components

ETL-SQL can be deployed as workstation tooling, server services, or both.

| Component | Purpose | Typical host |
| :--- | :--- | :--- |
| Workstation SDK | `ETL-SQL` CLI, terminal IDE, language server, and report tooling for script authors | Developer workstations, CI runners |
| Orchestrator Service | Background scheduler and job execution service | Application server |
| Report Portal | Web application for report catalog, snapshots, subscriptions, and administration | Application server |

The Orchestrator and Report Portal may run on the same host or on separate hosts. In separate-host deployments, configure the portal with the orchestrator API URL and shared API key.

---

## 2. Production Installation

### Windows

1. Run the `ETL-SQL-Enterprise-v0.9.0.msi` installer.
2. Select the workstation and server features required for the host.
3. The installer registers these Windows services when the server features are selected:
   - `ETL-SQL-Orchestrator`
   - `ETL-SQL-Portal`
4. Review the service accounts before production use. The installer default is `LocalSystem`; use a least-privilege domain or local service account when the service needs access to network shares, database drivers, certificates, or controlled script roots.

### Linux

Install the package for your distribution, then enable the services you intend to run:

```bash
sudo dpkg -i etl-sql_0.9.0_amd64.deb
sudo systemctl enable etl-sql-orchestrator
sudo systemctl start etl-sql-orchestrator
sudo systemctl enable etl-sql-portal
sudo systemctl start etl-sql-portal
```

For RPM-based systems, use the matching `.rpm` package and the same `systemctl` service names.

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

### 4.3 Orchestrator API Key

Protect the Orchestrator management endpoints with a shared API key whenever the endpoint is reachable outside a tightly controlled internal network. The portal sends the key in the `X-Orchestrator-Key` request header.

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "C:\\ETL-SQL\\scripts"
  }
}
```

If `Orchestrator:ApiKey` is empty, the management endpoints are open. Treat that as development-only or isolated-network behavior.

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
* **ImportCatalogMetadata**: Imports database comments, nullability, and primary key status dynamically from SQL Server, PostgreSQL, and MySQL catalog providers prior to exporting. Can be overridden ad-hoc in scripts using `SET LINEAGE_IMPORT_CATALOG = ON/OFF`.


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

The `etl-sql doctor` command is a built-in health check that validates the most common setup problems before you begin using the environment.

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
