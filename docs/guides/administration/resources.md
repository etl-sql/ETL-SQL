# Resource Controls

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
* **ImportCatalogMetadata**: Reads native column metadata — comments/descriptions, data type, nullability, primary-key status — from the SQL Server, PostgreSQL, and MySQL catalog providers and folds it into lineage. A column's database comment becomes the column's **lineage description**, so it **inherits onto derived columns** (e.g. `SUM(Amount) AS total` carries Amount's comment) and surfaces in the portal's structure/lineage views.

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

User snippets with the same trigger as a built-in override the built-in. The directory is loaded once at startup; restart the application to pick up changes. The path can be a UNC share for team-wide deployment (`\\fileserver\etlsql\snippets`). See [Getting Started](../getting-started.md) for the full authoring reference.

---

