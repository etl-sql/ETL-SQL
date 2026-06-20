# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Departmental Isolation

> Status: **active.**
> Goal: support multiple isolated environments (dev/test/prod, or separate departments) without
> introducing shared-table multitenancy.
>
> Priority convention: **P1** the deployment and portability path needed for supported isolated
> environments; **P2** fleet-level visibility and hardening once isolation is proven.

### Phase 1 - Repeatable Deployment Templates

- [x] **P1.1 Define the isolated-environment topology** for single-node and HA deployments,
  including per-environment Portal database, Orchestrator database, artifact root, Data Protection
  key ring, service identity, network boundary, and encryption keys.
  *(done)* `Docs/Operations/Departmental_Isolation.md`: isolation model, per-environment resource
  table (single-node + HA), naming/port conventions (`<env>` token, `PORT_BASE`+{0,1,2,3,32}), config
  surface, and the verification runbook.
- [x] **P1.2 Build Docker Compose templates** for isolated departmental environments, with separate
  project names, ports, PostgreSQL volumes, artifact roots, environment files, and health checks.
  *(done)* `deploy/docker/`: parameterized `docker-compose.environment.yml` (own PostgreSQL + named
  volume, separate Portal/Orchestrator databases, artifact root, keys, ports, health checks),
  `environment.env.example`, and an orchestrator-db init hook. Validated with `docker compose config`.
- [x] **P1.3 Build Windows Service deployment templates** that install Portal and Orchestrator under
  environment-specific service names and service identities, with isolated config, logs, storage,
  and key material.
  *(done)* `deploy/windows/Install-Environment.ps1`: registers `ETL-SQL-{Portal,Orchestrator}-<env>`
  under a dedicated account, ACL-locks the data root (inheritance removed), and injects per-service
  config via the service's `Environment` registry value.
- [x] **P1.4 Build systemd deployment templates** for Linux hosts, with environment-specific units,
  users/groups, config paths, storage roots, and restart/health behavior.
  *(done)* `deploy/systemd/`: templated `etl-sql-{portal,orchestrator}@<env>` units (per-instance
  user, `EnvironmentFile`, `ReadWritePaths` hardening) + `install-environment.sh` (dedicated user,
  `0700` data root, per-instance env file).
- [x] **P1.5 Add an isolation verification runbook or script** that proves one environment's service
  identity cannot read or mutate another environment's database, artifact storage, logs, or keys.
  *(done)* `deploy/verify/` `Test-Isolation.ps1` / `verify-isolation.sh` fail on any shared database,
  root, key ring, port, account, or key (secrets masked, HA-node-aware; `-CheckAcls` adds a
  cross-account ACL probe), plus the runbook in the topology doc and `IsolationVerifierTests`.

### Phase 2 - Environment Portability

- [x] **P1.6 Define the portable environment package format** for reports, jobs, folders,
  permissions, subscriptions, datasets, alerts, and config metadata, excluding secrets and raw
  connection strings.
  *(done, pre-existing)* `ConfigurationExportService` emits a logical-name bootstrap script (groups →
  users → memberships → folders → ACLs → SMTP → reports → datasets → subscriptions → alerts → refresh
  jobs) plus a `RequiredSecrets` list and a content manifest; secrets/connection strings excluded.
- [x] **P1.7 Add export/import commands** for moving reports, jobs, and configuration between
  isolated environments, with dry-run validation and deterministic idempotency.
  *(done, pre-existing)* `EXPORT PORTAL CONFIGURATION` exports; import replays through the ReportPortal
  connector (`ReportPortalDataSource.ExecuteAdminStatementAsync`), which is create-or-skip idempotent
  (natural-key updates) and supports dry-run via `SET WHAT_IF ON`. Covered by ScriptedPortalImport /
  ConfigurationRoundTrip tests.
- [x] **P1.8 Strip or externalize environment-specific secrets** during export, emitting named-secret
  requirements instead of credential values.
  *(done, pre-existing)* Every credential is emitted as a `${...}` placeholder with a RequiredSecrets
  list; unsubstituted placeholders fail closed at import. Covered by ConfigurationExportSecretExclusion
  and MissingSecret_FailsClosed tests.
- [x] **P1.9 Add promotion tests** for dev-to-test/prod movement that verify imported assets keep
  logical identity while rebinding environment-specific secrets, roots, schedules, and service
  accounts.
  *(done)* `ConfigurationPromotionTests` promotes a dev portal into a separate prod portal and asserts
  logical identity preserved (group/folder/ACL/report/subscription) while secrets (SMTP password
  decrypts to the prod value; dev cipher/hash never carried), the report script root, and the refresh
  job's orchestrator alias are all rebound to prod; re-promotion is idempotent.

### Phase 3 - Fleet Aggregation

- [x] **P2.1 Define the fleet aggregator trust boundary** before implementation: read-only,
  scoped service-account access to each environment, no script execution, no writes, and no raw data
  blending.
  *(done)* `Docs/Operations/Departmental_Isolation.md` §7: the aggregator only issues
  `GET /api/fleet/status`; each environment provisions a distinct `FleetReader` credential that
  authorizes that endpoint and nothing else; only aggregate operational counts cross the boundary —
  no report data, scripts, identities, secrets, or keys.
- [x] **P2.2 Build read-only fleet health aggregation** for environment status, queue depth,
  active executions, failed jobs, audit outbox health, and storage pressure.
  *(done)* Added the `FleetReader` role, `GET /api/fleet/status` (`FleetStatusController`) returning
  status + queue depth + active executions + failed refreshes + audit-outbox pending/failed + storage
  availability (with `ExecutionJobService.GetWorkloadCounts`), and `FleetHealthAggregator` which fans
  out to each environment's endpoint with its scoped token and tolerates unreachable environments.
- [x] **P2.3 Prove aggregator credential containment** so a compromised aggregator credential cannot
  pivot into any department database, artifact storage, encryption keys, or execution capability.
  *(done)* `FleetContainmentTests` certify a FleetReader token reads only `/api/fleet/status` (200)
  and is `403` on the admin/identity surface and report publish/execute; unauthenticated access is
  `401`; `FleetHealthAggregatorTests` certifies fan-out + unreachable tolerance.
