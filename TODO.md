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

- [ ] **P1.1 Define the isolated-environment topology** for single-node and HA deployments,
  including per-environment Portal database, Orchestrator database, artifact root, Data Protection
  key ring, service identity, network boundary, and encryption keys.
- [ ] **P1.2 Build Docker Compose templates** for isolated departmental environments, with separate
  project names, ports, PostgreSQL volumes, artifact roots, environment files, and health checks.
- [ ] **P1.3 Build Windows Service deployment templates** that install Portal and Orchestrator under
  environment-specific service names and service identities, with isolated config, logs, storage,
  and key material.
- [ ] **P1.4 Build systemd deployment templates** for Linux hosts, with environment-specific units,
  users/groups, config paths, storage roots, and restart/health behavior.
- [ ] **P1.5 Add an isolation verification runbook or script** that proves one environment's service
  identity cannot read or mutate another environment's database, artifact storage, logs, or keys.

### Phase 2 - Environment Portability

- [ ] **P1.6 Define the portable environment package format** for reports, jobs, folders,
  permissions, subscriptions, datasets, alerts, and config metadata, excluding secrets and raw
  connection strings.
- [ ] **P1.7 Add export/import commands** for moving reports, jobs, and configuration between
  isolated environments, with dry-run validation and deterministic idempotency.
- [ ] **P1.8 Strip or externalize environment-specific secrets** during export, emitting named-secret
  requirements instead of credential values.
- [ ] **P1.9 Add promotion tests** for dev-to-test/prod movement that verify imported assets keep
  logical identity while rebinding environment-specific secrets, roots, schedules, and service
  accounts.

### Phase 3 - Fleet Aggregation

- [ ] **P2.1 Define the fleet aggregator trust boundary** before implementation: read-only,
  scoped service-account access to each environment, no script execution, no writes, and no raw data
  blending.
- [ ] **P2.2 Build read-only fleet health aggregation** for environment status, queue depth,
  active executions, failed jobs, audit outbox health, and storage pressure.
- [ ] **P2.3 Prove aggregator credential containment** so a compromised aggregator credential cannot
  pivot into any department database, artifact storage, encryption keys, or execution capability.
