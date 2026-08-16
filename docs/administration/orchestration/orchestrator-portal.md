# Orchestrator Management Portal

The **Orchestrator Management Portal** is a browser-based dashboard embedded in the ETL-SQL Portal that gives administrators full visibility and control over scheduled jobs without needing the CLI or a SQLite viewer.

## Prerequisites

The management portal is hosted inside the Portal (`ETL-SQL-Portal`). The Orchestrator Service (`ETL-SQL-Service`) must be running and reachable from the machine that runs the portal. Requests use both a shared API key for service-to-service authentication and a short-lived Portal-signed identity assertion for caller authorization.

## Configuring Service and Caller Authentication

For production, generate two independent secrets: an API key and an identity-signing secret of at least 32 bytes. Configure the same value for each secret on both services. A network-reachable Orchestrator requires caller assertions by default; `RequireFederatedIdentity=false` is a Solo-only escape hatch, described under [Legacy mode](#legacy-mode-solo-only), and must not be used for a shared service.

**On the Orchestrator Service** (`appsettings.json` or environment variable):

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-api-key",
    "IdentitySigningSecret": "a-distinct-random-secret-at-least-32-bytes",
    "RequireFederatedIdentity": true,
    "ScriptRoot": "/opt/etl/scripts"
  }
}
```

Or via environment variable:
```
Orchestrator__ApiKey=your-shared-api-key
Orchestrator__IdentitySigningSecret=a-distinct-random-secret-at-least-32-bytes
Orchestrator__RequireFederatedIdentity=true
Orchestrator__ScriptRoot=/opt/etl/scripts
```

**On the Portal** — two options:

*Option A — `appsettings.json` / environment variable (applied at startup):*
```json
{
  "Portal": {
    "Orchestrator": {
      "ApiUrl": "http://orchestrator-host:5001",
      "ApiKey": "your-shared-api-key",
      "IdentitySigningSecret": "a-distinct-random-secret-at-least-32-bytes"
    }
  }
}
```
Or:
```
Portal__Orchestrator__ApiUrl=http://orchestrator-host:5001
Portal__Orchestrator__ApiKey=your-shared-api-key
Portal__Orchestrator__IdentitySigningSecret=a-distinct-random-secret-at-least-32-bytes
```

*Option B — Admin UI (applied immediately, no restart needed):*
Log in as Admin, navigate to **Admin → Settings → Orchestrator Connection**, enter the URL and API key, and click **Save**. Settings are written to a `portal-orchestrator.json` sidecar file and take effect on the very next request. UI-saved URL/API-key settings take precedence over environment variables. The identity-signing secret remains host configuration and is never accepted from or returned to the browser.

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
| **Admin** | Full object-authority bypass — Orchestrator tab is always visible |
| **OrchestratorManager** | May create objects and manage objects they own or have been granted; cannot access the Admin panel |

Assign the `OrchestratorManager` role to operations staff who need to manage jobs but should not be able to create users or manage reports. See the [Portal Administrator's Guide](../portal/README.md#orchestrator-manager-role) for role assignment instructions.

### Object ownership and grants

`JOB`, `SCHEDULE`, and `NOTIFICATION` names are shared catalog identities. The principal that creates an object owns it; `CREATE OR ALTER` and `CREATE OR REPLACE` preserve that owner and cannot be used to take over another principal's name. This is enforced in both HTTP endpoints and ETL-SQL statement handlers.

An object with no recorded owner is reachable only by an administrator until an owner is assigned. Editing it does not assign one — ownership decides who may manage the object, so it changes only by an explicit, audited reassignment. Administrators can list unowned objects, assign an owner to one, or assign one to all of them at once, from the Orchestrator tab's **Unowned objects** button or with `etl-sql admin orchestrator unowned | set-owner | adopt`. Reassignment is administrator-only: an owner may manage their own object, so an owner who could hand ownership on could widen access to it without anyone administering it. A user or a service account may own an object; a group may be granted permissions but cannot own one, because ownership names a single accountable principal.

Names are re-usable and identity is not: dropping an object deletes its grants, and an object later created under the same name starts with none.

Owners and administrators can grant `READ`, `EXECUTE`, `OVERRIDE`, or `MANAGE` to a Portal user, group, or service account. `MANAGE` includes all lower permissions; `OVERRIDE` includes execute/read; `EXECUTE` includes read. Variable-overridden triggers require `OVERRIDE`, because an override may widen the job's data scope. Plain triggers and named-checkpoint resumes require `EXECUTE`.

The management API uses `/api/authorization/{kind}/{name}` to list grants and `/api/authorization/{kind}/{name}/{principalKind}/{principalId}` to set or revoke them. Valid kinds are `JOB`, `SCHEDULE`, and `NOTIFICATION`; valid principal kinds are `USER`, `GROUP`, and `SERVICE`. Job history and data-quality APIs filter rows using the same `READ` decision. Ad-hoc run status and cancellation are visible only to that run's submitting principal or an administrator. Kinds, principal kinds, and permissions are named in responses exactly as they are accepted in requests. The Portal proxies the same routes under `/api/orchestrator/authorization/...` for the Access panel and for `etl-sql admin orchestrator show|grant|revoke`, so no operator needs the Orchestrator's signing secret on their machine.

## Legacy mode (Solo only)

`Orchestrator:RequireFederatedIdentity=false` runs the service in **legacy mode**: requests are authenticated by the shared API key and nothing else. There are no principals, so there are no grants and no ownership decisions — the API key is a root key over every job, schedule, and notification on the host. This is supported for a **Solo** deployment: one person, one box, no Portal. Team and above require a Portal, because the Portal is where principals, groups, and audit live.

The setting is usually not set at all. When it is absent, the service infers it: a non-loopback listener requires caller assertions and a loopback listener does not. That guess is wrong in one common case — a shared Orchestrator behind a reverse proxy binds loopback and so looks exactly like a laptop.

### Which mode am I in?

Two places answer, without guessing from the configuration:

- **The startup log** names the mode on every start. If the deployment does not look Solo — a non-loopback listener, a configured identity-signing secret, a configured tenant, or requests arriving with `X-Forwarded-For`/`Forwarded` — the line is a warning that names the specific contradiction and the way out.
- **`GET /health`** reports `authorizationMode` (`federated` or `legacy`), `requiresCallerIdentity`, and `legacyModeOnSharedDeployment`. The endpoint is unauthenticated, so it names the mode and no more; the evidence behind the warning stays in the log.

### What legacy mode refuses

The entire per-object authorization surface — `/api/authorization/...` for grants, ownership, unowned objects, and adoption — answers `409 Conflict` while the service is in legacy mode, in the UI, the CLI, and direct API calls alike. A grant written there would name a principal that exists nowhere and restrict a caller that already passes every check, while looking exactly like access control. Everything else — creating, editing, running, killing, and inspecting jobs — works normally.

### Promoting a Solo host to a team

Legacy mode is reversible, and the objects the host already has do not carry an owner. Take them in this order:

1. Stand up a Portal and pair it — same `ApiKey` and same `IdentitySigningSecret` on both services (see [Configuring Service and Caller Authentication](#configuring-service-and-caller-authentication)).
2. Set `Orchestrator:RequireFederatedIdentity=true` on the Orchestrator and restart it. Confirm `GET /health` reports `federated`.
3. Everything created before this point has no owner, so it is administrator-only until one is assigned. Assign one to all of it at once:

   ```powershell
   etl-sql admin orchestrator unowned --portal-url https://portal.example.com
   etl-sql admin orchestrator adopt --portal-url https://portal.example.com --principal-kind USER --principal <stable-key>
   ```

   The Orchestrator tab's **Unowned objects** button does the same thing from the browser. Adoption is audited per object, not per batch, so "who became accountable for this, and when" is answerable afterwards.
4. Grant the rest of the team what they need with `etl-sql admin orchestrator grant`, or from the job detail panel's **Access** tab.

`etl-sql admin promotion preflight --to-profile Team` reports finding **DP009** for orchestrator objects that still have no owner, so a Solo → Team promotion catches step 3 before cutover rather than after — see [Deployment promotion](../platform/deployment-promotion.md).

## Dashboard Features

Navigate to the Orchestrator tab in the portal after logging in with an eligible role.

**Stats bar** — five chips that auto-refresh every 10 seconds: service status (Online/Offline badge), Active Jobs, Queued, Completed Today, Failed Today.

**24-hour Gantt chart** — a timeline from 00:00 to 23:59 showing each job as a horizontal bar at its scheduled firing time, sized by historical average duration. Blue bars are enabled jobs; grey bars are disabled. Click any bar to open the job detail panel.

**Jobs table** — all jobs including disabled ones (disabled rows are visually dimmed). Columns: Name, Owner, Schedule, Status, Last Run, Next Run, Actions. The owner is the principal that created the job; a job with no recorded owner is reachable only by an administrator until an owner is assigned. Actions per row:

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
- **Access** — the job's owner and its grants, with add and revoke, plus owner reassignment for administrators. Grants are read from the Orchestrator on every open and never cached. Listing them requires `MANAGE` on the job, so a job you can reach but not administer shows the refusal rather than an empty table. Principals are named by their stable key rather than a username, because a name can be reassigned and a grant that followed one would move with it.

**Create Job modal** — opens via the **New Job** button. Fields:
- Job name
- Script — dropdown of files from the Orchestrator's script root, with a manual path fallback
- Schedule: Every N `SECONDS / MINUTES / HOURS / DAYS`, optional `AT HH:MM` for day-level jobs
- Max Retries and Retry Delay
- Hash Policy (`Warn`, `Block`, or `Off`)

> [!NOTE]
> Jobs created through the portal store the selected script target in the Orchestrator catalog. If the `.etlsql` file on disk is edited later, update the job through the portal or with `ALTER JOB <name> SET TARGET = '...'` / `CREATE OR REPLACE JOB <name> FOR SCRIPT '...'`.

## Operations Triage and Run Evidence

The Orchestrator tab groups recent failed runs by normalized error signature and separately calls out
running work and enabled jobs whose scheduled occurrence passed without being claimed. Expanding an
incident shows its individual runs. Use **Evidence** on a run to join three durable sources in one
view:

- **Script integrity** — the runtime script hash and whether it matched the registered hash.
- **Quality failures** — normalized target, column, rule, action, owner, and failure count. Failed
  sample values are not stored or displayed.
- **Statement timeline** — normalized statement text, duration, rows, queue/lock wait, spill volume,
  and the statement that failed. SQL literals are normalized before persistence.

An empty rail means that evidence was not retained for that run; it does not assert that no statement
ran or no rule was evaluated. Loading and read failures are shown explicitly. Run evidence uses the
same `OrchestratorAccess` policy as the triage board and remains readable from the shared history store
when the Orchestrator service itself is offline.

Use **Run** (or **Run now** on a missed occurrence) to open the one-run execution form. Optional
variable overrides use the same names and value text as CLI `--var`; the script should declare each
input with a scheduled-run default. Overrides apply to that execution and its retries only—the saved
job and its schedule are unchanged. The Portal and Orchestrator audit the override count and variable
names, but never the values. Up to 32 overrides are accepted, with a 4,096-character limit per value.
If the same job is already running, the trigger returns `409 Conflict`; retry after that execution
finishes so the requested override set cannot be silently coalesced or discarded.

### Resume from a named checkpoint

Run history shows **Resume · `<label>`** only for a failed or cancelled run whose persistent session
reached an author-declared top-level label. Selecting it restores that run's opaque session state and
queues the current saved script with its session id and `--resume`. The same contract is honored by
in-process execution, one-shot child processes, warm runners, and custom argument templates.

Resume is opt-in at script-authoring time: enable persistence with `SET PERSIST ON` and place a
top-level label at a boundary that is safe to replay. Existing runs cannot become resumable
retroactively. A disabled action states whether the run has no named checkpoint or has a terminal
status that does not permit recovery; a `409 Conflict` after selection means the checkpoint expired,
the current script no longer contains the label, or the same job is already running.

The engine resumes only at the last completed named label. It does not accept a statement number or
instruction offset: variables, `#temp` tables, connection state, and transactions make an arbitrary
mid-script jump unsafe. Work following the label can run again, so external writes after a checkpoint
must be idempotent or duplicate-safe. Resume is audited at both the Portal and Orchestrator security
event boundaries without exposing the opaque session handle.

## Service Control

When the Orchestrator is **online**, two service-control buttons appear next to the Online chip:

- **Stop** — sends `POST /management/stop` to the Orchestrator, which calls `IHostApplicationLifetime.StopApplication()`. If the Orchestrator is registered as a Windows Service or systemd unit, the OS supervisor restarts it automatically. The portal polls `/health` every 3 seconds and updates the status chip as soon as the service comes back.
- **Restart** — equivalent to Stop; the portal waits for the service to come back online.

When the Orchestrator is **offline**, the portal displays a banner: *"Orchestrator is offline."* If `Portal:Orchestrator:SameHost = true` is configured, a **Start** button also appears that uses the Windows `ServiceController` API to start the local service. On separate-server deployments, start the service manually on its host.

## Metrics and Scraping

The Orchestrator Service exposes three unauthenticated operations endpoints:

| Endpoint | Format | Purpose |
| :--- | :--- | :--- |
| `GET /health` | JSON | Liveness probe for supervisors and load balancers. Also reports `authorizationMode` (`federated` or `legacy`), `requiresCallerIdentity`, and `legacyModeOnSharedDeployment` — see [Legacy mode](#legacy-mode-solo-only). |
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
