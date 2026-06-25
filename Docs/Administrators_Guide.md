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

The Orchestrator and Report Portal may run on the same host, on separate hosts, or as multiple
load-balanced nodes. Single-node deployments use SQLite by default. Practical High Availability
deployments use shared PostgreSQL state plus shared Portal artifact roots; configure the portal with
the orchestrator API URL and shared API key when the services are split. Use
[Operations/Capacity_Planning.md](Operations/Capacity_Planning.md) when deciding whether to start
shared or split the services.

---

## 2. Production Installation

### Windows

1. Run the `ETL-SQL-Enterprise-v0.12.0.msi` installer.
2. Select the workstation and server features required for the host.
3. The installer registers these Windows services when the server features are selected:
   - `ETL-SQL-Orchestrator`
   - `ETL-SQL-Portal`
4. Review the service accounts before production use. The installer default is `LocalSystem`; use a least-privilege domain or local service account when the service needs access to network shares, database drivers, certificates, or controlled script roots.

### Linux

Install the package for your distribution, then enable the services you intend to run:

```bash
sudo dpkg -i etl-sql_0.12.0_amd64.deb
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

4. **High Availability Scaling**:
   For multi-node active-active load-balanced clusters, use the HA-specific docker compose template located at [deploy/docker/docker-compose.ha.yml](../deploy/docker/docker-compose.ha.yml). This setup supports variable container scaling behind a sticky HAProxy load balancer. See [Section 6.2 Containerized HA Clustering (Docker Compose)](#62-containerized-ha-clustering-docker-compose) below for detailed instructions.

### First-Run Checklist

Before exposing the services to users:

1. Set a production JWT secret for the portal.
2. Set an orchestrator API key if the management API is reachable beyond a loopback-only or isolated internal network.
3. Configure HTTPS certificates or place the services behind a TLS-terminating reverse proxy.
4. Set script, snapshot, dataset, and map root directories to dedicated service-owned folders.
5. Confirm backup coverage for portal/orchestrator state and artifact roots: SQLite files for
   single-node deployments, or PostgreSQL backups plus shared storage snapshots for HA deployments.
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
Portal__Database__Provider=Sqlite
Portal__ScriptRootPath=C:\ETL-SQL\scripts
Portal__SnapshotDirectory=C:\ETL-SQL\snapshots
Portal__Orchestrator__ApiUrl=https://orchestrator.example.com:5003
Portal__Orchestrator__ApiKey=your-shared-secret
Portal__Storage__Provider=Local
Portal__Storage__KeyRingPath=C:\ETL-SQL\data\.portal-keys
Orchestrator__ApiKey=your-shared-secret
Orchestrator__Database__Provider=Sqlite
Orchestrator__ScriptRoot=C:\ETL-SQL\scripts
Jobs__UseProcessSpawning=true
Jobs__ExecutablePath=C:\Program Files\ETL-SQL\bin\ETL-SQL.exe
```

Use environment variables or deployment-secret tooling for values that should not be written to disk in plaintext.

### 3.1 Code Style & Formatting Configuration (`.etlsqlformat.json`)

To enforce consistent SQL formatting styles across user workstations, administrators can place a `.etlsqlformat.json` configuration file in the root of shared script repository directories or VCS workspaces. The ETL-SQL formatter (integrated into the CLI, TUI, and language server) will recursively look up parent directories from the target script file to locate and load this configuration automatically.

For the list of all formatting variables and configuration options (e.g. `keywordCasing`, `commaPlacement`, `formatMetadataTags`), see **Section 16.1 (Query Formatting Configuration)** in the [User Manual](User_Manual.md).

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

### 4.4 Governance Core

Governance Core centralizes three production controls:

- **Typed policy enforcement** — policy violations are attached to lint diagnostics and enforced again at execution boundaries.
- **Named secret references** — connector passwords and sensitive connection-string fields can use `SECRET:name` instead of raw secret values.
- **Durable audit forwarding** — Portal security and mutation audit rows are staged in a transactional outbox and can be forwarded to an HTTPS collector, with optional fail-closed behavior.

#### Named secret providers

Configure the secret provider in `appsettings.json` or with environment variables under `Governance:Secrets:*`.
The older `Secrets:*` prefix remains accepted as a compatibility fallback, but new deployments should use
`Governance:Secrets:*`.

```json
{
  "Governance": {
    "Secrets": {
      "Provider": "Environment",
      "EnvironmentPrefix": "ETLSQL_SECRET_"
    }
  }
}
```

Supported providers:

| Provider | Required settings | Operational notes |
| :--- | :--- | :--- |
| `Environment` | Optional `EnvironmentPrefix` | Secret names are uppercased; `.` and `-` become `_`. With the prefix above, `SECRET:sales_db_password` resolves from `ETLSQL_SECRET_SALES_DB_PASSWORD`. |
| `OsSecretStore` | `OsStoreRoot` | Stores protected values under a fully qualified local directory. On Unix, secret files are written owner-read/write only. Back up the store with the host or service identity that can decrypt it. |
| `HttpsVault` | `VaultEndpoint`; optional `VaultBearerToken` | The endpoint must be HTTPS. The provider requests `<VaultEndpoint>/<secret-name>` and accepts either a raw response body or JSON `{ "value": "secret" }`. |

Environment-variable examples:

```text
Governance__Secrets__Provider=HttpsVault
Governance__Secrets__VaultEndpoint=https://vault.example.com/etl-sql/secrets
Governance__Secrets__VaultBearerToken=ENC:ENCRYPTED_TOKEN
```

Use named references in connector definitions:

```sql
CREATE CONNECTION sales AS MSSQL(
  SERVER = 'sql01',
  DATABASE = 'Sales',
  USER = 'etl_worker',
  PASSWORD = 'SECRET:sales_db_password'
);

CREATE CONNECTION warehouse AS POSTGRES(
  HOST = 'pg01',
  DATABASE = 'dw',
  USER = 'etl',
  PASSWORD = 'SECRET:dw_password'
);
```

Only sensitive connector options and sensitive connection-string fields are expanded. Missing or unreachable
secrets fail closed with an error; ETL-SQL does not silently replace a missing secret with an empty value.
Logs, diagnostics, audit rows, support bundles, result formatting, and portal/orchestrator error surfaces redact
raw secret values and `SECRET:` references before persistence or display.

#### Organization policy documents

Governance policy documents use schema version `1.0`. Policy loaders accept local OS-protected JSON files and
HTTPS endpoints, validate the document, and may use a protected offline cache only while it remains inside the
configured offline window. If the live source cannot be loaded and the cache is missing, invalid, disabled, or
expired, policy loading fails secure.

```json
{
  "schemaVersion": "1.0",
  "connectors": {
    "allowedTypes": [ "MSSQL", "POSTGRES", "FLATFILE", "SFTP" ]
  },
  "filesystem": {
    "approvedRoots": [ "C:\\ETL-SQL\\scripts", "C:\\ETL-SQL\\data" ]
  },
  "execution": {
    "allowedModes": [ "Interactive", "Batch", "Scheduled" ],
    "maxParallelDegree": 4,
    "maxFileOperationsPerScript": 100
  },
  "remoteExecution": {
    "mode": "TrustedOrchestrator",
    "allowedHosts": []
  },
  "mutationGuardrails": {
    "requireWhatIfForDestructiveStatements": true,
    "requireTransactionForMutations": true,
    "requireRemoteAuditForMutations": true
  }
}
```

Local policy files must use fully qualified paths and must not be writable by broad principals. On Windows, write
access for `Everyone`, `Users`, or `Authenticated Users` is rejected. On Unix-like systems, group-writable or
other-writable policy files are rejected. Remote policy sources must use HTTPS.

#### Durable audit outbox and remote collectors

Portal audit rows are written with a durable outbox row in the same database transaction. Configure remote
forwarding under `Portal:Audit:*`:

```json
{
  "Portal": {
    "Audit": {
      "TransportEndpoint": "https://siem.example.com/etl-sql/audit",
      "TransportBearerToken": "ENC:ENCRYPTED_COLLECTOR_TOKEN",
      "TransportBatchSize": 100,
      "TransportIntervalSeconds": 30,
      "TransportTimeoutSeconds": 10,
      "TransportMaxAttempts": 8,
      "TransportLockSeconds": 120,
      "OutboxBackpressureLimit": 10000,
      "OutboxMaxBytes": 104857600,
      "OutboxDeliveredRetentionMinutes": 1440,
      "RequireRemoteDelivery": true,
      "FailClosedMaxPendingBacklog": 1000,
      "FailClosedMaxBacklogSeconds": 900
    }
  }
}
```

The collector endpoint must be HTTPS. Each POST body has an `events` array. Every event includes a stable
`EventId`, audit metadata, and a redacted JSON payload; collectors should treat `EventId` as the deduplication key
because a row may be resent after a crash or lost delivery acknowledgement. Any 2xx response marks the batch
delivered. Non-2xx responses retry with exponential backoff until `TransportMaxAttempts`, then the row is marked
`Failed`.

`RequireRemoteDelivery` changes the Portal from best-effort forwarding to fail-closed mutation behavior. When it is
enabled, security-sensitive mutations are blocked with HTTP 503 once remote audit delivery is judged unavailable:
any terminally failed outbox row, pending backlog over `FailClosedMaxPendingBacklog`, oldest pending row older than
`FailClosedMaxBacklogSeconds`, or queued payload over `OutboxMaxBytes`. Leave it disabled unless an HTTPS collector
is configured, monitored, and treated as mandatory infrastructure.

When `RequireRemoteDelivery` is disabled, the outbox transport may shed old delivered rows and then oldest queued
rows to keep local disk usage under `OutboxMaxBytes`; the durable local `AuditLog` rows remain. When
`RequireRemoteDelivery` is enabled, ETL-SQL never drops queued remote-audit rows to satisfy the cap; it blocks new
mutations until the collector drains the backlog.

Operational checks:

1. Configure the collector and verify it accepts HTTPS POSTs from every Portal node.
2. Trigger a harmless audited action and confirm the collector receives an event with a stable `EventId`.
3. Temporarily stop the collector and confirm pending outbox rows accumulate.
4. If `RequireRemoteDelivery` is enabled, confirm mutations fail with HTTP 503 after the configured backlog, age, or size threshold.
5. Restart the collector and confirm pending rows drain and mutations resume.

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

## 6. Portal State and Data Roots

The Report Portal constrains filesystem access to configured roots. Set these to service-owned directories rather than broad user folders:

| Setting | Purpose | Default in code |
| :--- | :--- | :--- |
| `Portal:DatabasePath` | Portal SQLite database | `./portal.db` |
| `Portal:Database:Provider` | Portal state provider: `Sqlite` or `Postgres` | `Sqlite` |
| `Portal:Database:ConnectionString` | Portal PostgreSQL connection string when provider is `Postgres` | *(required for Postgres)* |
| `Portal:ScriptRootPath` | Report and job script browser root | `./Reports` |
| `Portal:SnapshotDirectory` | Report snapshot output | `./Snapshots` |
| `Portal:DatasetRootPath` | Dataset files managed by the portal | `./data/datasets` |
| `Portal:MapRootPath` | Map assets used by reports | `./data/maps` |
| `Portal:Storage:Provider` | Artifact provider: `Local` or `Smb`/`Unc` | `Local` |
| `Portal:Storage:KeyRingPath` | ASP.NET Data Protection key ring and Keys artifact root | `.portal-keys` beside the portal DB |
| `Orchestrator:Database:Provider` | Orchestrator state provider: `Sqlite` or `Postgres` | `Sqlite` |
| `Orchestrator:Database:ConnectionString` | Orchestrator PostgreSQL connection string when provider is `Postgres` | *(required for Postgres)* |

The portal rejects script, snapshot, map, and dataset paths that resolve outside their configured roots.

### 6.1 Practical High Availability Configuration

For a load-balanced HA deployment, every Portal and Orchestrator node must point at the same
PostgreSQL database deployment. Every Portal node must also point at the same shared artifact roots and
the same Data Protection key ring. The supported shared filesystem provider is `Smb`/UNC.

Example Portal node configuration:

```json
{
  "Portal": {
    "Database": {
      "Provider": "Postgres",
      "ConnectionString": "Host=pg-ha.internal;Database=etlsql_portal;Username=etl_portal;Password=..."
    },
    "Storage": {
      "Provider": "Smb",
      "KeyRingPath": "\\\\fileserver\\etlsql\\keys"
    },
    "ScriptRootPath": "\\\\fileserver\\etlsql\\reports",
    "SnapshotDirectory": "\\\\fileserver\\etlsql\\snapshots",
    "DatasetRootPath": "\\\\fileserver\\etlsql\\datasets",
    "MapRootPath": "\\\\fileserver\\etlsql\\maps",
    "LoadBalancer": {
      "SessionAffinityEnabled": true,
      "SessionAffinityCookieName": "ETLSQL_PORTAL_AFFINITY",
      "SessionAffinityCookieMinutes": 480
    },
    "Orchestrator": {
      "ApiUrl": "https://orchestrator-vip.example.com:5003",
      "ApiKey": "your-shared-secret"
    }
  }
}
```

Example Orchestrator node configuration:

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "Database": {
      "Provider": "Postgres",
      "ConnectionString": "Host=pg-ha.internal;Database=etlsql_orchestrator;Username=etl_orch;Password=..."
    },
    "ScriptRoot": "\\\\fileserver\\etlsql\\scripts"
  },
  "Jobs": {
    "UseProcessSpawning": true,
    "ExecutablePath": "C:\\Program Files\\ETL-SQL\\bin\\ETL-SQL.exe"
  },
  "Scheduler": {
    "QuarantineFailureThreshold": 5
  },
  "Cluster": {
    "NodeHeartbeatSeconds": 30
  }
}
```

Operational requirements:

- Use sticky routing on the `ETLSQL_PORTAL_AFFINITY` cookie, or the configured
  `Portal:LoadBalancer:SessionAffinityCookieName`, because interactive sessions are node-local.
- Point load balancer health checks at `GET /healthz`. It returns HTTP 200 only when the Portal can
  reach PostgreSQL, shared snapshot storage, and the node-registry/lease store. Use `GET /health` for
  richer monitoring.
- Keep `Portal:Jwt:Secret`, `Portal:Dataset:AtRestKey`, `Portal:Storage:KeyRingPath`, and
  `Portal:Orchestrator:ApiKey` identical across Portal nodes.
- Run Portal and Orchestrator under service identities that can read/write the configured PostgreSQL
  databases and shared storage roots. For SMB/UNC roots, use a domain identity or managed service
  account with explicit share and NTFS permissions.
- Back up PostgreSQL and the shared artifact roots as one coordinated recovery set. The HA state is no
  longer represented by only `portal.db` and `etlsql.db` files.

### 6.2 Containerized HA Clustering (Docker Compose)

For container-native deployments (such as Docker engines, overlay networks, or Swarm environments), ETL-SQL provides a clustered, multi-node Compose template under [`deploy/docker/`](../deploy/docker) designed to run an active-active clustered environment with dynamic scaling.

The HA container configuration utilizes:
- **Shared PostgreSQL Database**: Centralized PostgreSQL container (configured via `docker-compose.ha.yml`) that replaces local SQLite database files. Both Portal and Orchestrator nodes communicate with this shared instance.
- **Shared Host Volume Binding**: Mapped to `ENV_DATA_ROOT`. This directory hosts the reports, snapshots, datasets, maps, and the `.portal-keys` Data Protection key ring. Since all scaled Portal containers mount this same directory structure, they automatically share the Data Protection keys needed to decrypt and validate session tokens and cookies.
- **Dynamic Load Balancing**: An HAProxy load balancer handles ingress routing on host ports `5000` (Portal) and `5001` (Orchestrator API).
- **Session Affinity**: Because Report Portal interactive sessions are stored in process-local memory caches, the load balancer routes client requests stickily based on the `ETLSQL_PORTAL_AFFINITY` cookie. Stateless Orchestrator jobs are round-robin balanced.

#### Deploying and Scaling the HA Stack

1. Navigate to the deployment folder:
   ```bash
   cd deploy/docker
   ```

2. Generate your unique environment configuration:
   ```bash
   cp environment-ha.env.example production-ha.env
   # Edit production-ha.env to supply unique JWT secrets, API keys, database credentials, and ports
   ```

3. Spin up the stack with your chosen scale (e.g., 3 Portals and 2 Orchestrators):
   ```bash
   docker compose --env-file production-ha.env -f docker-compose.ha.yml up -d --scale portal=3 --scale orchestrator=2
   ```

To dynamically scale containers up or down, execute the `up` command again with updated `--scale` flags. HAProxy dynamically queries Docker's internal DNS (`127.0.0.11`) to discover new container instances and mark decommissioned instances as down.

---

## 7. Resource Controls

Use resource settings to keep one report or job from consuming the whole host.

### Orchestrator Lockbox Bundles

Published Orchestrator bundles are stored in the configured Orchestrator database as immutable versions.
Back up the database together with any configured lockbox key material.

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
    "MaxConcurrentExecutionsPerUser": 2,
    "MaxConcurrentExecutionsPerGroup": 0,
    "InteractiveExecutionWeight": 2,
    "RefreshExecutionWeight": 1,
    "ExecutionTimeoutSeconds": 300,
    "SessionCacheMaxSize": 50,
    "SessionCacheTtlMinutes": 30
  },
  "LoadBalancer": {
    "SessionAffinityEnabled": true,
    "SessionAffinityCookieName": "ETLSQL_PORTAL_AFFINITY",
    "SessionAffinityCookieMinutes": 480
  }
}
```

In load-balanced Portal deployments, configure sticky sessions on the cookie named by
`SessionAffinityCookieName`. Interactive report sessions are held in the node-local session cache,
so requests for a live session should continue routing to the same Portal process.

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

- **`manifest.json`** — bundle metadata (generated time, tool version, OS, .NET runtime; host and local paths are redacted).
- **`doctor-health.json`** — a full `doctor` health snapshot in machine-readable form.
- **`config-redacted.json`** — your `appsettings.json` with **all credentials redacted**.
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

Restore validates before it writes, and **fails closed** on any mismatch:

```bash
# Verify integrity, key versions, and version compatibility WITHOUT writing anything
etl-sql admin restore --from data.zip --keys keys.zip --validate

# Restore into a clean directory once validation passes
etl-sql admin restore --from data.zip --keys keys.zip --to D:\restore-target
```

Validation checks that the two archives are a matching pair (same backup id), that the data archive's
at-rest key version is present in the keys archive, that every file matches its recorded checksum, and
that the backup was **not** produced by a newer release than the restoring binary. Restore
reconstructs the on-disk layout and re-injects the secrets into the restored `appsettings.json`; on the
next portal start, pending migrations apply automatically. Dataset caches referenced by **absolute**
path in the catalog must be restored to their original `DatasetRootPath` (or re-materialized) — see
[§6.5](ReportPortal_Administrators_Guide.md#versioned-upgrades-and-rollback).

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
[ReportPortal_Administrators_Guide.md §6.5 → "Versioned Upgrades and Rollback"](ReportPortal_Administrators_Guide.md#versioned-upgrades-and-rollback).

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
