# Production Readiness Checklist

Use this checklist before promoting the Portal to a production or customer-facing environment. Items marked **Required** will cause data loss, security exposure, or service failure if skipped. Items marked **Recommended** reduce operational risk.

## Security

- [ ] **Required** — Set `Portal__FirstRun__AdminPassword` before first startup, change the initial `admin` password after first login, then remove that bootstrap value from configuration.
- [ ] **Required** — Replace the default JWT secret. Set `Portal__Jwt__Secret` in environment variables or `appsettings.json` to a randomly generated 256-bit value. Run `etl-sql config setup-jwt --update` to generate one.
- [ ] **Required** — Set `Portal__Jwt__Issuer` and `Portal__Jwt__Audience` to values that match your deployment. Default `ETL-SQL-Portal` values are acceptable but should be documented.
- [ ] **Required** — Enable HTTPS in production. Configure a reverse proxy (nginx, Caddy, IIS) or supply a TLS certificate via Kestrel. Do not run the portal over plain HTTP with real user data.
- [ ] **Recommended** — Restrict `Security:AuthorizedHosts` in `appsettings.json` to the actual hostnames the portal will accept requests from.
- [ ] **Recommended** — Verify that connector secrets in report scripts use `ENC:` encryption with a master password, not plaintext connection strings.
- [ ] **Recommended** — Review folder-level permissions. Users should not have access to reports or datasets outside their role.

## Data and Storage

- [ ] **Required** — Confirm `Portal:DatabasePath` points to a persistent location that survives service restarts and OS reboots (not a temp directory or container ephemeral layer).
- [ ] **Required** — Confirm `Portal:SnapshotDirectory`, `Portal:DatasetRootPath`, `Portal:MapRootPath`, and `Portal:ScriptRootPath` are writable and on volumes with sufficient capacity.
- [ ] **Recommended** — Schedule regular backups of the Portal database, Orchestrator database, Data Protection key ring, and snapshot/script/dataset/map roots. For HA, back up PostgreSQL and shared storage as one coordinated recovery set.
- [ ] **Recommended** — Set `Portal:MaxSnapshotAgeDays` to automatically clean up expired snapshots.

## Reliability

- [ ] **Required** — For single-node SQLite deployments, run one active Portal process per portal database. For HA deployments, configure every Portal node to use the same PostgreSQL database and shared artifact storage roots; startup singleton work is coordinated through the database-backed cluster lock.
- [ ] **Required** — For load-balanced HA deployments, configure sticky sessions using the portal affinity cookie (`ETLSQL_PORTAL_AFFINITY` by default). Interactive report sessions are cached in memory on the node that created them.
- [ ] **Required** — Run the portal as a managed service (Windows Service or systemd unit) so it restarts automatically on host reboot or crash.
- [ ] **Required** — Treat a restart or node heartbeat lease loss as cancellation of in-flight portal executions. Polling remains durable through `PortalExecutionJobs`; abandoned `Pending`/`Running` jobs return `Cancelled` with an interruption reason and must be submitted again.
- [ ] **Required** — Set `Portal:Topology:ExpectedMode` explicitly unless this is a plain single-node install with default paths. `Auto` infers `HighAvailability` from PostgreSQL **or** a configured `Portal:Storage:KeyRingPath`, and never infers `Departmental` — so a departmental or single-node deployment that merely moved its key ring is held out of rotation with `ha-requires-portal-postgres` until it is told what it is.
- [ ] **Recommended** — Verify `/healthz` returns `Healthy` before directing user traffic. Use `/healthz` for load-balancer probes and `/health` for richer monitoring dashboards. If it returns 503, read the `findings` array: every code is listed with its cause and remedy in [HA Topology Failure Certification](../../architecture/decisions/HA_Topology_Failure_Certification.md#readiness-and-health).
- [ ] **Recommended** — If the Orchestrator is deployed separately, confirm `Portal:Orchestrator:ApiUrl` and both `ApiKey` values match. Verify the connection via the Admin → Orchestrator page.
- [ ] **Recommended** — Configure SMTP for subscriptions. Test an outbound email from Admin → Connections before creating live subscriptions.

## Observability

- [ ] **Recommended** — Enable structured logging (`Logging:LogLevel:Default` = `Information` minimum). Direct logs to a persistent file or log aggregator.
- [ ] **Recommended** — Enable the audit log (`Portal:EnableAuditLog = true`) so report view, export, and subscription events are recorded.
- [ ] **Recommended** — Set up monitoring alerts on `/health` and `/healthz` with a recovery window of ≤ 5 minutes.
- [ ] **Recommended** — Review the Report History page after first production use to confirm snapshot refresh and subscription delivery are completing without errors.

## Operational Handoff

- [ ] **Recommended** — Document the deployment: service name, host, port, backup schedule, and escalation path.
- [ ] **Recommended** — Identify who holds the admin credentials and the JWT secret, and ensure they are stored in a secrets manager (not in a shared document).
- [ ] **Recommended** — Run `etl-sql doctor` from the host machine to confirm write access, ODBC drivers, and configuration are correct before go-live.