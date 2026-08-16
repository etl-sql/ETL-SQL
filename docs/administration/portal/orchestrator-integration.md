# Orchestrator Management

The portal includes a built-in **Orchestrator** tab that provides a web interface for managing ETL-SQL scheduled jobs. Access is controlled by the `OrchestratorAccess` policy: **Admin** or **OrchestratorManager** role.

## Connecting to the Orchestrator Service

The portal communicates with the Orchestrator Service over HTTP. Configure the connection in one of two ways:

**Via environment variable / `appsettings.json`** (takes effect at startup):

```json
"Portal": {
  "Orchestrator": {
    "ApiUrl": "http://orchestrator-host:5001",
    "ApiKey": "your-shared-secret",
    "IdentitySigningSecret": "a-distinct-random-secret-at-least-32-bytes",
    "SameHost": false
  }
}
```

**Via the Admin UI** (takes effect immediately — no restart required):

1. Log in as Admin.
2. Navigate to **Admin → Settings → Orchestrator Connection**.
3. Enter the **Orchestrator API URL** (e.g., `http://orchestrator-host:5001`).
4. Enter the **API Key** if one is configured on the Orchestrator side.
5. Click **Save**.

The portal writes a `portal-orchestrator.json` sidecar file next to the portal database. Values saved here override environment variables on the next request.

To verify the connection, click **Test Connection** — the button calls the `/api/orchestrator/status` endpoint using the currently saved settings and displays an Online or Offline chip.

> [!TIP]
> If you change the URL or key without saving, **Test Connection** still tests the previously saved settings. Save first, then test.

### API key and caller identity

The API key is sent as an `X-Orchestrator-Key` header on every request the portal makes to the Orchestrator. The Orchestrator must be configured with the same key:

```
Orchestrator__ApiKey=your-shared-secret
```

The API key identifies the Portal service, not the human or service account making the request. The Portal therefore also emits a short-lived, HMAC-signed `X-Orchestrator-Identity` assertion derived from the authenticated Portal/OIDC principal. Configure a separate matching secret on both hosts:

```
Portal__Orchestrator__IdentitySigningSecret=a-distinct-random-secret-at-least-32-bytes
Orchestrator__IdentitySigningSecret=a-distinct-random-secret-at-least-32-bytes
Orchestrator__RequireFederatedIdentity=true
```

The Orchestrator ignores caller-name headers and rejects missing, expired, or modified assertions. Do not reuse the API key as the signing secret. The Admin UI can rotate the URL/API key sidecar, but the signing secret is host-only configuration.

Remote report execution returns the completed report manifest through the authenticated Orchestrator
job-status API. The Portal then persists the manifest under its own `SnapshotDirectory`, so a shared
snapshot folder is not required when the two services run on separate hosts. Configure the same
non-empty API key on both services; the Orchestrator never includes report manifest data in an
unauthenticated status response. Verify the connection by executing a small report and confirming both
the snapshot manifest and CSV export are available.

The portal never echoes the stored API key back to the browser — the **Admin → Settings** page shows only whether a key is set (`HasApiKey: true/false`). To change the key, type a new value and save. To clear it, check **Clear API key** and save.

## What the Orchestrator Tab Shows

After connecting, the Orchestrator tab displays:

| Section | Description |
| :--- | :--- |
| **Stats bar** | Service status chip (Online/Offline), Active Jobs, Queued, Completed Today, Failed Today. Refreshes every 10 seconds. |
| **24-hour Gantt chart** | All jobs plotted on a timeline from 00:00 to 23:59. Each bar is positioned at the job's scheduled fire time and sized by historical average duration. Blue = enabled, grey = disabled. Click a bar to open the job detail panel. |
| **Jobs table** | All registered jobs including disabled ones. Columns: Name, Schedule, Status, Last Run, Next Run, Actions. |
| **Job detail panel** | Slides in from the right when you click a job or Gantt bar: schedule info, script content (read-only), duration trend sparkline, and a history table showing the last 20 executions. |

## Job Actions

| Action | What it does |
| :--- | :--- |
| **Run / Trigger** | Fires the job immediately, outside its normal schedule. The job still runs at its next scheduled time afterwards. |
| **Disable** | Sets `IsEnabled = false`. The job is still visible (dimmed) and its history is preserved. Re-enable at any time. |
| **Enable** | Sets `IsEnabled = true`. The scheduler picks the job up at its next fire time. |
| **Kill** | Cancels a currently-running execution. Only available when the job has a `RUNNING` history entry. |
| **Delete** | Permanently removes the job definition and all its history. This is equivalent to `DROP JOB` and cannot be undone. |

> [!CAUTION]
> Use **Disable** to pause a job temporarily. Use **Delete** only to retire a job permanently.

## Creating a Job

Click **New Job** to open the Create Job modal.

| Field | Description |
| :--- | :--- |
| **Job Name** | Unique identifier — no spaces, use underscores |
| **Script** | Pick from the Orchestrator's script browser (files in `Orchestrator:ScriptRoot`) or enter a path manually |
| **Every / Unit** | Schedule interval: a number and `SECONDS`, `MINUTES`, `HOURS`, or `DAYS` |
| **At Time** | Optional `HH:MM` wall-clock time, used with `DAYS` to pin to a specific time of day |
| **Max Retries** | How many times to retry on failure (0 = no retries) |
| **Retry Delay** | Initial delay in seconds between retries (doubles on each subsequent attempt) |
| **Hash Policy** | `Warn` (log a warning if the script changed since creation), `Block` (refuse to run if the script changed), or `Off` |

> [!NOTE]
> The job stores the script content at creation time. If the `.etlsql` file changes on disk later, the stored copy is not updated automatically. Re-create or re-save the job to pick up the change.

## Service Control

When the Orchestrator is online, two buttons appear next to the status chip:

- **Stop** — gracefully shuts down the Orchestrator process. If it is registered as a Windows Service or systemd unit, the OS supervisor restarts it automatically. The portal polls the health endpoint every 3 seconds and updates the status chip when the service comes back.
- **Restart** — functionally identical to Stop; the portal waits for the service to come back online and shows a polling indicator.

When the Orchestrator is offline:
- An **Offline** banner is shown across the top of the page.
- If `Portal:Orchestrator:SameHost = true` is configured, a **Start** button appears that uses the Windows `ServiceController` API to start the local service.
- On separate-server deployments the portal displays: *"Orchestrator is offline — start the service on its host machine."*

## Performance Metrics

The job detail panel's history table includes per-execution performance data:

| Column | Source |
| :--- | :--- |
| **Duration** | Wall-clock time from `StartTime` to `EndTime` |
| **Rows Processed** | Row count reported by the script |
| **Peak RAM** | Peak memory in bytes during execution (recorded at job completion) |
| **CPU Time** | Cumulative CPU seconds (recorded at job completion) |

> [!NOTE]
> RAM and CPU columns are only populated for completed runs. A currently-running job shows elapsed wall-clock time only — live resource counters are not available.

## Configuration Reference

| Key | Location | Description |
| :--- | :--- | :--- |
| `Portal:Orchestrator:ApiUrl` | Portal `appsettings.json` / env var | Base URL of the Orchestrator Service HTTP API |
| `Portal:Orchestrator:ApiKey` | Portal `appsettings.json` / env var | Shared secret sent as `X-Orchestrator-Key` header |
| `Portal:Orchestrator:IdentitySigningSecret` | Portal protected configuration / env var | Signs short-lived caller assertions; must match the Orchestrator value |
| `Portal:Orchestrator:SameHost` | Portal `appsettings.json` / env var | `true` enables the **Start** button using Windows `ServiceController` |
| `Portal:Orchestrator:DatabasePath` | Portal `appsettings.json` / env var | Location of the Orchestrator's SQLite DB from Portal context (used to query job status/history locally). Defaults to `../Orchestrator/etlsql.db` relative to the Portal's database directory. |
| `portal-orchestrator.json` | Sidecar file next to portal database | Overrides for URL/key saved via the Admin UI; takes precedence over env vars |
| `Orchestrator:DatabasePath` | Orchestrator `appsettings.json` / env var | Path to the Orchestrator's SQLite database. Defaults to `%LocalAppData%/ETL-SQL/etlsql.db` if unset. |
| `Orchestrator:ApiKey` | Orchestrator `appsettings.json` / env var | Key the Orchestrator validates against incoming `X-Orchestrator-Key` headers |
| `Orchestrator:IdentitySigningSecret` | Orchestrator protected configuration / env var | Verifies Portal caller assertions; use a distinct value of at least 32 bytes |
| `Orchestrator:RequireFederatedIdentity` | Orchestrator `appsettings.json` / env var | Requires a valid signed caller assertion; defaults on for non-loopback listeners. `false` is [Solo-only legacy mode](../orchestration/orchestrator-portal.md#legacy-mode-solo-only) |
| `Orchestrator:ScriptRoot` | Orchestrator `appsettings.json` / env var | Root directory for the script file browser exposed to the portal |

---
