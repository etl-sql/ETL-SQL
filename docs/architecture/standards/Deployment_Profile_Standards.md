# ETL-SQL Deployment Profile Standards

This standard defines the current support and evidence contract for Solo / Workstation, Team / SME, Enterprise / Corporate, and SaaS / Multi-Organization. These are cumulative operating profiles, not editions. Canonical `.etlsql`, `.rptsql`, rules, tags, assertions, and declarative job/report definitions remain portable; larger profiles add stronger operational and trust boundaries.

## Status legend

- **Green** — representative implementation and focused evidence exist for the profile.
- **Yellow** — useful implementation exists, but the complete profile journey is not certified.
- **Red** — a known gap or missing boundary prevents a support claim.
- **N/A** — genuinely inapplicable, with the reason stated in the cell.

The status is current implementation evidence, not a roadmap promise. The deployment-profile certification lane may move a cell to Green only with commit-bound evidence.

## Capability matrix

| Concern | Solo / Workstation | Team / SME | Enterprise / Corporate | SaaS / Multi-Organization |
| :--- | :--- | :--- | :--- | :--- |
| Authoring | [**Green** — script-first CLI/editor artifacts](../../guides/onboarding/getting-started.md) | [**Yellow** — portable repository workflow exists; team promotion is not certified](../roadmaps/Deployment_Profile_Strategy.md#dp-01--author-and-run-a-portable-pipeline) | [**Yellow** — governed catalog/promotion exists; approval journey remains active work](../roadmaps/Enterprise_Platform_Strategy.md) | [**Red** — controlled tenant ingress and tenant authoring boundary are not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| Execution | [**Green** — CLI and local automation](../../reference/cli/run.md) | [**Green** — shared Orchestrator execution](../../administration/orchestration/README.md) | [**Green** — governed Portal/Orchestrator hosts](../../administration/README.md) | [**Red** — tenant-scoped quotas and failure containment are not certified](../roadmaps/Deployment_Profile_Strategy.md#dp-12--prove-isolation-and-failure-containment) |
| Scheduling | [**Green** — OS scheduler or optional local SQLite Orchestrator](../../guides/feature-guides/data-quality.md#running-unattended-without-portal) | [**Green** — durable schedules/jobs](../../reference/orchestrator-jobs/schedule.md) | [**Green** — lease-fenced distributed scheduling](../../administration/README.md) | [**Red** — tenant queue/schedule isolation is not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| Connections and secrets | [**Green** — local protected store and `SECRET:name`](../../administration/README.md) | [**Green** — shared managed catalog with reference-only credentials](../../administration/README.md) | [**Green** — provider-backed secrets, policy, ACLs, rotation, and audit](../roadmaps/Enterprise_Platform_Strategy.md) | [**Red** — tenant/provider/key separation and export proof are not certified](../roadmaps/Deployment_Profile_Strategy.md#dp-02--configure-connections-and-secrets) |
| Reports | [**Green** — Report Player and portable `.rptsql`](../../guides/feature-guides/report-sql.md) | [**Green** — shared reports and optional Portal](../../guides/patterns/one-person-quality-loop.md#4-open-the-reports) | [**Green** — governed Portal catalog and refresh](../../administration/portal/README.md) | [**Yellow** — report contracts exist; tenant catalog/embed isolation is not certified](../roadmaps/Deployment_Profile_Strategy.md#dp-05--author-publish-and-consume-a-report) |
| Quality and stewardship | [**Green** — CLI gates, scanner, catalogs, and reports](../../guides/patterns/one-person-quality-loop.md) | [**Green** — SQLite history, baselines, notifications, and reports](../../guides/feature-guides/data-quality.md#running-unattended-without-portal) | [**Green** — identical remote catalogs plus governance policy/audit](../../reference/eng/stewardship-score.md) | [**Red** — tenant lineage, scan, quality, cache, and outbox isolation is not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| Identity | **N/A** — one trusted local operator; process/OS authority is the boundary | [**Yellow** — local roles exist; complete shared-identity journey is not certified](../../administration/portal/README.md) | [**Yellow** — OIDC/groups ship; service accounts and approvals remain active work](../../administration/README.md) | [**Red** — platform/tenant identity and delegated administration are not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| Policy | [**Green** — source-controlled workspace policy](../../guides/patterns/one-person-quality-loop.md) | [**Yellow** — workspace policy applies; shared authority journey is not certified](../roadmaps/Deployment_Profile_Strategy.md#4-cumulative-product-invariants) | [**Green** — typed organization policy and enforcement boundaries](../roadmaps/Enterprise_Platform_Strategy.md) | [**Red** — tenant-specific policy authority and platform separation are not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| Audit | [**Green** — local execution/security evidence](../../guides/feature-guides/data-quality.md) | [**Green** — durable shared history and security outbox](../../administration/README.md) | [**Green** — remote durable audit with optional fail-closed mutations](../roadmaps/Enterprise_Platform_Strategy.md) | [**Red** — tenant-complete plus separately audited platform access is not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| Backup and recovery | [**Yellow** — evidence export exists; full Solo N→N+1 drill is not certified](../../reference/cli/admin-backup.md) | [**Green** — supported backup/restore and validation workflow](../../administration/README.md) | [**Yellow** — shared-state procedures exist; profile-wide DR evidence is not current](../../administration/README.md) | [**Red** — tenant-scoped backup/export/restore isolation is not certified](../roadmaps/Deployment_Profile_Strategy.md#dp-07--back-up-restore-and-recover) |
| Observability | [**Green** — `doctor`, logs, quality JSON, and `eng.*`](../../reference/cli/doctor.md) | [**Green** — health, job history, host metrics, and notifications](../../administration/README.md) | [**Green** — probes, fleet metrics, audit collectors, and support evidence](../../administration/README.md) | [**Red** — tenant telemetry/support-access separation is not certified](../roadmaps/Deployment_Profile_Strategy.md#8-saas-is-a-distinct-trust-boundary) |
| High availability | **N/A** — a single workstation has no multi-node availability contract | **N/A** — the supported Team default is single-node; use the Enterprise profile when HA is required | [**Green** — PostgreSQL/shared storage, leases, fencing, heartbeats, affinity, and probes](../../administration/README.md) | [**Red** — tenant-aware fleet rollout and noisy-neighbor containment are not certified](../roadmaps/Deployment_Profile_Strategy.md#dp-10--grow-the-topology) |
| Tenant isolation | **N/A** — one trusted local operator and no tenant boundary | **N/A** — one team/organization and no mutually untrusted tenant boundary | [**Yellow** — organization/department controls exist; hard multi-tenant proof is out of profile](../roadmaps/Enterprise_Platform_Strategy.md) | [**Green** (implementation) — host-fixed boundaries and negative database, artifact, cache, queue, audit, PII, lineage/quality, path, and quota tests are in the SaaS certification lane; release claims still require clean commit-bound evidence](../roadmaps/Deployment_Profile_Strategy.md#dp-12--prove-isolation-and-failure-containment) |

## Smallest safe capability form

| Concern | Smallest safe form | Additive larger-profile boundary |
| :--- | :--- | :--- |
| Authoring | Plain-text source-controlled scripts in CLI/editor | Shared review, controlled promotion, tenant-authorized ingress |
| Execution | CLI process with a deterministic exit code | Durable services, service identity, leases, quotas, and tenant isolation |
| Scheduling | OS scheduler; optional local SQLite Orchestrator | Shared scheduler, fencing, HA ownership, tenant queues |
| Connections/secrets | Machine-protected secret store and `SECRET:name`; never raw export | Shared catalog ACLs, external providers, rotation/audit, tenant key separation |
| Reports | Local Report Player over the same `.rptsql` | Shared catalog, access control, refresh, metering, safe embeds |
| Quality/stewardship | `@expect`, `ASSERT JOB`, workspace policy, `eng.*`, scanner, local reports | Durable workflow, assignments, organization policy, tenant-isolated evidence |
| Identity | OS/process identity for one trusted operator | Local roles, OIDC/groups/service accounts, delegated tenant administration |
| Policy | Checked-in `etlsql-policy.json` | Signed organization authority, approvals, tenant-specific policy |
| Audit | Local counts-only execution/security evidence | Durable remote outbox, fail-closed mutation, tenant/platform dual audit |
| Backup/recovery | Export evidence and copy protected local state with documented key custody | Validated restore, shared-state order, DR, tenant-scoped recovery |
| Observability | `doctor`, logs, JSON evidence, and local `eng.*` | Health probes, metrics/traces, fleet views, isolated tenant telemetry |
| HA | Not simulated on one node | Shared providers, leases/fencing, affinity, rolling compatibility |
| Tenant isolation | Explicitly not applicable to a single trusted boundary | Server-derived tenant context at every data, cache, queue, key, and resource boundary |

Multi-party approvals and mutually untrusted tenant isolation have no honest one-person substitute. Smaller profiles expose N/A instead of simulating a security boundary that does not exist.

## Deployment overlays

Overlays add constraints and evidence to a profile; they never create another edition or change script semantics.

| Overlay | Additional required evidence |
| :--- | :--- |
| Regulated / high assurance | Data classification, least privilege, approval/separation where applicable, immutable retention, collector delivery, restore drill, and control mapping. |
| Air-gapped / disconnected | Offline dependency/package provenance, local secret/policy operation, clock/update procedure, removable-media controls, and offline recovery. |
| High volume / large data | Scale-tier evidence, capacity admission, spill/disk limits, backpressure, recovery time, and representative data-shape tests. |
| High availability | Shared state/artifacts/keys, affinity, probes, lease fencing, failover, rolling compatibility, and split-brain negative tests. |
| Disaster recovery | RPO/RTO, custody, off-site copies, ordered restore, validation, scheduler fencing, and documented last reversible point. |
| Data residency / regional | Region-bound data/artifact/key/audit/telemetry placement, controlled support access, backup locality, and negative cross-region proof. |

## Feature-design portability review

Every feature design or significant change must answer:

- What is the smallest safe profile where the capability applies?
- Can canonical scripts, reports, rules, tags, assertions, and declarative definitions move upward unchanged?
- Which configuration, identity, secret, storage, or endpoint bindings must be explicit at promotion?
- Does a larger profile add controls without recalculating or hiding local evidence?
- Are N/A cells genuine, or is Portal/Enterprise being made an unnecessary prerequisite?
- Which overlays apply, and what extra evidence do they require?
- What profile and transition tests, upgrade/rollback proof, and retained evidence support the claim?

## Release review

A release claim must name the profile and transition it actually proves. Review the matrix for changed cells, link current evidence, and never infer SaaS or HA support from a Solo/Enterprise happy path.

Completed reviews:

- [v0.18.0](../decisions/v0.18.0-deployment-profile-review.md) — Portal and Enterprise weighted; no cell moved to Green, SaaS unchanged and still Red for every concern touched.

## References

- [Deployment Profile Architecture](../DeploymentProfiles.md)
- [SaaS Tenant Isolation Architecture](../SaaSTenantIsolation.md)
- [Tenant Portability Architecture](../TenantPortability.md)
- [Deployment Profile and Portability Strategy](../roadmaps/Deployment_Profile_Strategy.md)
- [Release Checklist](../../releases/release-checklist.md)
- [Enterprise Platform Strategy](../roadmaps/Enterprise_Platform_Strategy.md)
