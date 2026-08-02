# Backup, Monitoring, and Health

## 8. Backup & Maintenance

### Databases

| Database | Typical path | Backup guidance |
| :--- | :--- | :--- |
| Portal SQLite DB | `Portal:DatabasePath` | Stop the portal or use SQLite online backup / `VACUUM INTO`. |
| Orchestrator SQLite DB | `Orchestrator:DatabasePath` | Stop the orchestrator or use SQLite online backup / `VACUUM INTO`. |

Back up the portal sidecar files, script roots, snapshots, datasets, map roots, and service configuration alongside the databases. A portal restore without the script root or snapshot directory is incomplete.

For PostgreSQL (HA) deployments, database backup is the responsibility of your PostgreSQL tooling
(`pg_dump` / continuous archiving / managed snapshots) — back up PostgreSQL and the shared artifact
roots as one coordinated recovery set. ETL-SQL does not back up PostgreSQL for you.

### Scheduling backups and restore drills

`etl-sql admin backup` (§11.3) is a one-shot command, not a scheduled service — **you schedule it**:

- **Schedule it externally** with the OS scheduler (Windows Task Scheduler / cron), not as an internal
  job, so a backup still runs when the orchestrator itself is down. Capture the command's exit code and
  alert on non-zero (wire it into the job-failure alerting in §9.1).
- **Test the restore.** An unverified backup is a hope, not a recovery plan. Periodically restore into a
  scratch directory with `etl-sql admin restore --validate --report recovery-report.json` (it validates
  integrity, key versions, and version compatibility before writing anything — see §11.3) and confirm
  the Portal starts and a report renders. Schedule this drill on a cadence that matches your recovery
  objectives.
- Restored clones must not silently reuse machine identity or credentials in another environment —
  re-enroll and rotate as covered in §11.3.

For supported RPO/RTO targets, restore-drill evidence, cross-environment clone safety, and regional
failure procedures, see [`docs/architecture/decisions/Disaster_Recovery_Objectives.md`](../../architecture/decisions/Disaster_Recovery_Objectives.md).

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

For report catalog, user, group, ACL, subscription, snapshot, and export operations, continue in [Portal Admin Guide](../portal/README.md).

### 9.1 External monitoring and alerting

ETL-SQL exposes health and history for you to monitor, but **it does not page you** — wire the signals
into your own monitoring stack.

**Liveness (dead-man's-switch).** Point an out-of-process monitor (your uptime service, Nagios/Zabbix,
a cloud health check, or a small cron script on a *different* host) at `GET /healthz` on each Portal
node and at the Orchestrator. Alert when the probe fails or stops responding. This must run outside the
Portal/Orchestrator process: a job *inside* the orchestrator cannot report that its own host is down,
so an internal check alone has a blind spot exactly when it matters most.

**Job-failure alerting.** Job outcomes are recorded in job history (queryable through `eng.job_history`,
or via the Orchestrator `GET /api/history` endpoint). Two complementary patterns:

- **Per-job immediate email** — the job author adds a failure branch that sends `SEND EMAIL` on error.
  Use this for **SLA-bearing jobs** (e.g. the outbound vendor SFTP deliveries) where a next-morning
  digest is already too late. This is the most reliable signal because it fires from the job itself.
- **Daily failure digest** — a scheduled job that reads the prior day's history and emails any failures
  as a safety net beneath the per-job emails. A ready-to-adapt template ships at
  `samples/admin_operations/daily_failure_digest.etlsql` (set the SMTP connection, recipient, and
  lookback, then schedule it daily). Because this digest runs inside the orchestrator, it cannot report
  its own host being down — so pair it with the external `/healthz` dead-man's-switch above and alert
  if the expected digest does not arrive.
- **Backup outcome + alert** — `samples/admin_operations/backup_and_report.etlsql` records the outcome
  of your external `etl-sql admin backup` run (durable `SET_JOB_STATE` markers) and emails ops on
  failure. The OS scheduler runs the backup, then runs this script with the exit code:
  `etl-sql run backup_and_report.etlsql --var backup_exit_code=$LASTEXITCODE --var backup_target=nightly`.
  It never runs a backup itself. Inspect the markers from any session with
  `eng.job_state` — the cross-job read surface over everything `SET_JOB_STATE` saved.
- **Portal operational digest** — the Portal can email administrators a scheduled digest of its own
  operational metrics (active/queued executions, 24h execution and delivery failure rates, storage
  usage, and migration status), with threshold alerts. Enable it under `Portal:OperationalDigest`
  (`Enabled`, `IntervalHours`, `Recipients`, `SmtpAlias`; set `AlertOnly` to send only when a threshold
  such as `FailureRatePercentThreshold`, `QueueDepthAlertThreshold`, or a pending migration is breached).
  In an HA cluster a leader lock ensures exactly one node sends per interval.

**Capacity and saturation.** Job history records each job's own `RowsProcessed`, `PeakMemoryBytes`, and
`CpuTimeSeconds`, and the Portal exposes point-in-time operational metrics (active/queued executions,
24h failure rates, hourly load, storage bytes). These describe *workload*. To answer "am I outgrowing
this server," the orchestrator now also captures **host** metrics — memory load, whole-host and per-process
CPU %, and free disk on the state and spill volumes — sampled every node heartbeat into a `HostMetrics`
time series (retained per `Orchestrator:HostMetricsRetentionDays`, rolled up daily for long-term trend).
Read the recent window through `eng.host_metrics`, and use
`samples/admin_operations/capacity_report.etlsql` to email a daily per-node/free-disk summary. The
native Portal capacity report adds retained-history summaries for max/p95 host CPU and memory,
scheduled-job failure rate, and p95 execution duration, peak memory, and CPU time. It also adds
identifier-only workload breakdowns for scheduled jobs and Portal executions by workload kind, report
id, and owner id; it does not include report names, SQL text, paths, error details, or row data.
OS-level monitoring remains a good independent cross-check.

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
- Probes configured Portal `/health`, Orchestrator `/health`, SMTP, SFTP, and Azure Blob endpoints

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
- See the [Production Readiness Checklist](../portal/production-readiness.md#14-production-readiness-checklist) in the portal admin guide for the full go-live gate.

---
