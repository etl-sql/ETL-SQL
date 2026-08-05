# Orchestrator Management Portal

The **Orchestrator Management Portal** is a browser-based dashboard embedded in the ETL-SQL Portal that gives administrators full visibility and control over scheduled jobs without needing the CLI or a SQLite viewer.

## Prerequisites

The management portal is hosted inside the Portal (`ETL-SQL-Portal`). The Orchestrator Service (`ETL-SQL-Service`) must be running and reachable from the machine that runs the portal. The two services communicate over HTTP using a shared API key.

## Enabling the API Key

By default the Orchestrator's management endpoints are open (no authentication). For production, set a shared secret on both sides.

**On the Orchestrator Service** (`appsettings.json` or environment variable):

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "/opt/etl/scripts"
  }
}
```

Or via environment variable:
```
Orchestrator__ApiKey=your-shared-secret
Orchestrator__ScriptRoot=/opt/etl/scripts
```

**On the Portal** — two options:

*Option A — `appsettings.json` / environment variable (applied at startup):*
```json
{
  "Portal": {
    "Orchestrator": {
      "ApiUrl": "http://orchestrator-host:5001",
      "ApiKey": "your-shared-secret"
    }
  }
}
```
Or:
```
Portal__Orchestrator__ApiUrl=http://orchestrator-host:5001
Portal__Orchestrator__ApiKey=your-shared-secret
```

*Option B — Admin UI (applied immediately, no restart needed):*
Log in as Admin, navigate to **Admin → Settings → Orchestrator Connection**, enter the URL and API key, and click **Save**. Settings are written to a `portal-orchestrator.json` sidecar file and take effect on the very next request. UI-saved settings take precedence over environment variables.

## Script Root

The portal's **Create Job** modal lets users pick a script file from a browser rather than typing a raw path. The Orchestrator Service exposes the file browser under a configured root directory:

```json
{
  "Orchestrator": {
    "ScriptRoot": "C:\\ETL\\Scripts"
  }
}
```

If `ScriptRoot` is not set it defaults to the Orchestrator's working directory. The file browser only surfaces `.etlsql` files and prevents path traversal outside the root.

## Granting Portal Access

Two roles can access the Orchestrator tab in the portal:

| Role | Access |
| :--- | :--- |
| **Admin** | Full access — Orchestrator tab is always visible |
| **OrchestratorManager** | Orchestrator tab only — cannot access the Admin panel |

Assign the `OrchestratorManager` role to operations staff who need to manage jobs but should not be able to create users or manage reports. See the [Portal Administrator's Guide](../portal/README.md#orchestrator-manager-role) for role assignment instructions.

## Dashboard Features

Navigate to the Orchestrator tab in the portal after logging in with an eligible role.

**Stats bar** — five chips that auto-refresh every 10 seconds: service status (Online/Offline badge), Active Jobs, Queued, Completed Today, Failed Today.

**24-hour Gantt chart** — a timeline from 00:00 to 23:59 showing each job as a horizontal bar at its scheduled firing time, sized by historical average duration. Blue bars are enabled jobs; grey bars are disabled. Click any bar to open the job detail panel.

**Jobs table** — all jobs including disabled ones (disabled rows are visually dimmed). Columns: Name, Schedule, Status, Last Run, Next Run, Actions. Actions per row:

| Action | What it does |
| :--- | :--- |
| **Run** | Triggers an immediate out-of-schedule execution |
| **Enable / Disable** | Toggles `IsEnabled` — the job stays in the database and remains visible when disabled |
| **Kill** | Cancels a currently-running execution (requires the job to have a `RUNNING` history entry) |
| **Delete** | Permanently removes the job and all its history (equivalent to `DROP JOB`) |

**Job detail panel** — slides in from the right when you click a job or Gantt bar. Shows:
- Schedule definition and next fire time
- Script content as of the last save (read-only, monospace)
- Duration trend sparkline (last 30 runs)
- History table: last 20 executions with Status, Start Time, Duration, Rows Processed, Peak RAM, CPU Time, and any error message

**Create Job modal** — opens via the **New Job** button. Fields:
- Job name
- Script — dropdown of files from the Orchestrator's script root, with a manual path fallback
- Schedule: Every N `SECONDS / MINUTES / HOURS / DAYS`, optional `AT HH:MM` for day-level jobs
- Max Retries and Retry Delay
- Hash Policy (`Warn`, `Block`, or `Off`)

> [!NOTE]
> Jobs created through the portal store the selected script target in the Orchestrator catalog. If the `.etlsql` file on disk is edited later, update the job through the portal or with `ALTER JOB <name> SET TARGET = '...'` / `CREATE OR REPLACE JOB <name> FOR SCRIPT '...'`.

## Service Control

When the Orchestrator is **online**, two service-control buttons appear next to the Online chip:

- **Stop** — sends `POST /management/stop` to the Orchestrator, which calls `IHostApplicationLifetime.StopApplication()`. If the Orchestrator is registered as a Windows Service or systemd unit, the OS supervisor restarts it automatically. The portal polls `/health` every 3 seconds and updates the status chip as soon as the service comes back.
- **Restart** — equivalent to Stop; the portal waits for the service to come back online.

When the Orchestrator is **offline**, the portal displays a banner: *"Orchestrator is offline."* If `Portal:Orchestrator:SameHost = true` is configured, a **Start** button also appears that uses the Windows `ServiceController` API to start the local service. On separate-server deployments, start the service manually on its host.

## Metrics and Scraping

The Orchestrator Service exposes three unauthenticated operations endpoints:

| Endpoint | Format | Purpose |
| :--- | :--- | :--- |
| `GET /health` | JSON | Liveness probe for supervisors and load balancers. |
| `GET /metrics` | JSON | Existing Portal/UI metrics payload with active jobs, queued jobs, maximum jobs, available slots, and active child processes. |
| `GET /metrics/prometheus` | Prometheus text | Scrape-friendly gauges for the same non-secret scheduler and process counts. |

`/metrics/prometheus` emits stable low-cardinality labels: `environment`, `node`, and
`component="orchestrator"`. It does not emit job names, script paths, script text, parameters,
connection metadata, credentials, or error details. Expose it only on a trusted management network or
behind your standard monitoring ingress controls.
In addition to live active/queued/max/available-slot gauges, the endpoint exports one-hour completed
job count, failed/interrupted count, average execution duration, rows processed, peak memory, CPU time,
and latest local-node host headroom samples for memory, CPU, state-disk free bytes, and spill-disk
free bytes when host metrics are available. Portal and Orchestrator Prometheus endpoints also export
component-labeled runtime gauges for process working set, private memory, managed heap bytes, and GC
collection counts, plus database-reachable gauges for the backing state store used to compose each
scrape.

These labels follow the shared `ETL_SQL.Core.Observability.ObservabilityConventions` contract used by
Portal metrics and traces. Prometheus labels drop the `etlsql.` prefix and replace dots with
underscores.

The built-in observability emits `System.Diagnostics` spans and metrics only. ETL-SQL does not install
or start an OpenTelemetry exporter or collector by default; attach your own listener/exporter in the
host when central telemetry is required. Trace-only expensive context, such as in-process script hashes
and policy hashes, is computed only when a trace listener is active.

Ad-hoc jobs submitted through `POST /jobs` also emit `System.Diagnostics.ActivitySource` spans from
`ETL-SQL.Orchestrator.Service` and `System.Diagnostics.Metrics` instruments from the same-named
meter. The `orchestrator.job` span carries job id, request correlation id, environment, node,
component, workload kind, execution mode, status, rows, peak memory, and CPU time for trace
correlation. Metrics cover completed job count, duration, rows processed, peak memory, and CPU time
with low-cardinality tags only: environment, node, component, workload kind, execution mode, and status.
Scheduled and manually triggered persisted jobs emit the same pattern from `ETL-SQL.Orchestrator`:
the `orchestrator.scheduled_job` span carries the durable history id as `etlsql.job.id`, script
hash, attempt number, status, rows, peak memory, and CPU time, while metrics keep job id and script
hash out of labels and include environment, node, component, workload kind, execution mode, and status.
Scheduled-job metrics also record queue-wait milliseconds and attempt-number histograms using those
same bounded labels.
Enterprise policy refresh attempts emit spans from `ETL-SQL.Orchestrator.Policy` and metrics from the
same-named meter. The `orchestrator.policy_refresh` span carries terminal status plus policy version
and policy hash when a refresh succeeds; metrics report refresh count and duration with only
environment, node, component, workload kind, and status labels.
In-process ETL-SQL engine executions emit spans from `ETL-SQL.Engine` and metrics from the same-named
meter. The `engine.execution` span carries script hash, optional job id, inherited request correlation
id when a parent ETL-SQL span has one, rows, peak memory, CPU time, spill bytes, and spill-read bytes;
the metrics report execution count, duration, rows, memory, CPU, spill writes, and spill reads with
environment, node, component, execution mode, workload kind, and status labels only.
Registered connectors emit spans from `ETL-SQL.Connectors` and metrics from the same-named meter for
version, catalog, and data-source creation operations. Connector telemetry uses connector type,
operation, status, environment, node, and component labels only; it intentionally omits connection
strings, hosts, table names, SQL text, paths, and credentials.
Dataset registry operations emit spans from `ETL-SQL.Datasets` and metrics from the same-named meter
for register, lookup, list, authorization, refresh-job registration, audit, delete, and path-build
operations. Dataset ids are trace-only; metrics use operation, status, environment, node, and component
labels and omit dataset names, caller permission strings, paths, and credentials.
Portal admin background services emit spans from `ETL-SQL.BackgroundServices` and metrics from the
same-named meter for native failure-digest, backup-report, and capacity-report runs. These metrics
use stable service name, operation, workload kind, status, environment, node, and component labels;
they do not include notification recipients, SMTP aliases, message bodies, run details, paths, or
error text.
The Portal orchestrator poller uses the same background-service meter/source for each poll cycle,
with statuses for degraded, idle, success, and failure. Poller labels intentionally omit
Orchestrator database paths, job names, subscription ids, report script paths, and error text.
Operational metrics digest sends also use `ETL-SQL.BackgroundServices`, with sent, skipped, and
failed statuses. Digest telemetry omits recipients, SMTP aliases, alert text, metric snapshot
content, message bodies, and delivery errors from labels.
Refresh-token maintenance emits purge-cycle spans and metrics from `ETL-SQL.BackgroundServices`.
The span carries the deleted-row count, while metrics keep only service name, operation, status,
workload kind, environment, node, and component labels and never include token hashes or usernames.
Audit-retention purges follow the same pattern: deleted-row counts are trace-only, and labels omit
audit actions, resource ids, details, actor names, and any retained or purged audit payload text.
Audit outbox transport drain and prune cycles emit background-service spans/metrics with delivered,
failed, empty, saturated, and success statuses as applicable. Row counts are trace-only; metric
labels omit event ids, audit actions, resource ids, payload JSON, bearer tokens, endpoints, and
transport error text.
Node heartbeat renewals emit background-service spans/metrics from `ETL-SQL.BackgroundServices`
with the host role as component and `node-heartbeat` as service name. Labels omit node ids,
machine names, heartbeat metadata JSON, capacity values, and lease-loss reasons.
Portal snapshot startup migration emits background-service spans/metrics with success, skipped,
cancelled, or failure status. Migrated snapshot counts are trace-only; labels omit manifest paths,
snapshot artifact keys, report names, and manifest payload values.
Portal startup validators for JWT secrets, dataset at-rest keys, OIDC configuration, and session-cache
lifecycle emit background-service spans/metrics with bounded service/operation/status labels only.
Secret values, validation messages, script paths, report ids, user ids, and session keys are omitted.
The Orchestrator host start/stop wrapper emits the same background-service span/metric contract with
component, service, operation, and status labels only; scheduler configuration and database paths are
not exported as labels.

Every Orchestrator HTTP response includes `X-Correlation-ID`, matching ASP.NET Core's request trace
identifier. Request logs are scoped with that correlation id and the active trace id so API calls,
job logs, and external monitoring traces can be joined during incident review.

## Differences from `DROP JOB`

| Action | Effect |
| :--- | :--- |
| **Disable** (portal) | Sets `IsEnabled = 0`. Job stays in the database; history is preserved; the job is still visible in the portal greyed-out. Re-enable at any time. |
| **Delete** (portal) | Equivalent to `DROP JOB` — permanently removes the job definition and all history. Cannot be undone. |

Use **Disable** when you want to pause a recurring job temporarily. Use **Delete** only when you are retiring a job permanently.

---

*For the scheduling internals, see [Orchestrator Architecture](../../architecture/Orchestrator.md).*  
*For the full `CREATE JOB` syntax and all scheduling options, see [Job Scheduling](../../reference/orchestrator-jobs/schedule.md).*  
*For complete function and connector references, see [Standard Library](../../reference/functions/README.md) and [Data Connectors](../../reference/connectors/README.md).*