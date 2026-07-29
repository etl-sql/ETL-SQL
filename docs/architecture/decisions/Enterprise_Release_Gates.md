# Enterprise Release Gates

This document is the release-gate checklist for the enterprise policy, monitoring, HA, recovery, and
documentation workstream. It records the completed enterprise prioritization gates and the evidence
that must be reviewed before a release or customer deployment claim is made.

Use the [Enterprise Security Review Packet](Enterprise_Security_Review_Packet.md) for the senior
security review record and the
[Enterprise Release Evidence Checklist](Enterprise_Release_Evidence_Checklist.md) for full-suite
evidence capture. Those artifacts are support material; they do not replace enterprise reviewer
signoff or v0.16.0 release-suite evidence collection for the candidate build.

## Workstream Prioritization

| Rank | Workstream | Administrative pain | Deployment scale | Security impact | External dependency | Gate |
| :---: | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Recovery and restore evidence | High | All deployments | High | Low for SQLite, high for HA PostgreSQL/storage | Backup, restore, recovery report, and clone-safety docs/tests must pass before HA claims. |
| 2 | Policy authority and governance enforcement | High | Departmental and enterprise | Critical | Medium: certificate/identity infrastructure | Enterprise hardening certification must pass on Windows and Linux. |
| 3 | HA topology and readiness | Medium | Multi-node deployments | High | High: PostgreSQL, load balancer, shared storage, DNS, certificates | HA readiness, failure matrix, and soak evidence must be current before HA claims. |
| 4 | Alerting and service objectives | Medium | Shared services | Medium | Medium: observability stack | Operational metrics, Prometheus alerts, and runbooks must be current. |
| 5 | Documentation hub and operator self-service | Medium | All deployments | Medium | Low | Portal-hosted docs search must exclude local configuration/secrets and respect module fencing. |
| 6 | Future remote fleet operations | Potentially high | Departmental fleets | Critical | High: fleet network and approval systems | Not enabled by default; requires a new threat model, authorization design, approval workflow, and audit contract. |

Workstreams are ranked by measured administrative pain, expected customer deployment scale, security
impact, and dependency on infrastructure outside ETL-SQL. A higher rank does not mean larger code; it
means the release should not advertise downstream capability until the upstream gate is satisfied.

## Fleet Aggregation Boundary

Fleet aggregation is read-only by default. The supported fleet credential is scoped to
`GET /api/fleet/status`; it must not run scripts, mutate catalog state, read reports, read secrets,
rotate credentials, upgrade nodes, or write policy. The current evidence is:

- `FleetContainmentTests` proves the `FleetReader` role can read only fleet status and cannot pivot
  into admin, publish, or execution surfaces.
- `FleetHealthAggregator` fans out with GET requests only and tolerates unreachable environments.
- `docs/architecture/decisions/Departmental_Isolation.md` defines the fleet trust boundary.

Any future remote mutation, remote upgrade, remote restart, policy push, secret rotation, or job-run
command requires a separate approved design containing:

- Threat model and abuse cases for compromised fleet credentials.
- Authorization model with least privilege, tenant/environment scoping, and break-glass rules.
- Human approval workflow for important or destructive actions.
- Durable audit contract covering requester, approver, target environment, before/after state, and
  correlation IDs.
- Rollback and emergency stop behavior.

Until that design exists and is reviewed, fleet features remain read-only.

## Threat Model and Security Review

Enterprise changes that touch policy, identity, secrets, fleet visibility, HA ownership, restore,
remote audit/security delivery, or executable/process boundaries require threat-model review before
being called complete.

The review must record:

- Assets and trust boundaries.
- Actors and credentials.
- Abuse cases, including local administrator bypass, stolen service account tokens, cloned machine
  identities, stale backups, and cross-environment restores.
- Mitigations, residual risk, and owners.
- Test or certification evidence for each high-severity mitigation.

Completion criteria:

- No open high-severity findings.
- Medium findings have owners and target releases.
- Documentation states residual risk plainly.
- Release notes do not claim stronger containment than the code, OS, and deployment guide provide.

The signed review record belongs in
[Enterprise Security Review Packet](Enterprise_Security_Review_Packet.md).

## Certification Suite Matrix

Before enterprise release claims are made, collect current evidence from these suites:

| Gate | Command or evidence | Purpose |
| :--- | :--- | :--- |
| Functional regression | `.\scripts\test-lane.ps1 -Lane fast -NoRestore`, `.\scripts\test-lane.ps1 -Lane engine -NoRestore`, plus `.\scripts\test-lane.ps1 -Lane portal -NoRestore` for Portal-facing changes | Quick smoke/LSP feedback, broad parser/engine/reporting/governance/local orchestration coverage, Portal API, and browser-side smoke coverage. |
| Migration/upgrade | `.\scripts\Test-PreRelease.ps1 -IncludeSlt -Explain` plus the N to N+1 upgrade-path phase | Forward migration and release packaging confidence. |
| Enterprise hardening | `.\scripts\Test-EnterpriseHardeningCertification.ps1` on Windows and Linux | Enrollment, signed policy, operation-boundary enforcement, standalone behavior, and security-event delivery. |
| Recovery | `etl-sql admin restore --validate --report recovery-report.json` and `BackupRestoreDrillTests` | Archive integrity, key coverage, clone-safety actions, service-account/audit/job continuity. |
| HA failure certification | Bounded run ordering: `fault-plan` → `fault-run` → `evidence` → `validate` | PostgreSQL/shared-storage failover, node loss, duplicate ownership, orphaned work, and recovery evidence. |
| Scale/performance | `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke`; Standard tier when advertising scale claims | Bounded-memory and spill behavior. |
| Standalone regression | `StandaloneRegressionTests` | Unenrolled standalone hosts remain unrestricted by enterprise policy. |
| Security boundary docs | `SecurityBoundaryDocTests` | Documentation does not overclaim OS-level containment and mandates WDAC/AppLocker or equivalent where required. |

Long-running Docker, HA, Standard-scale, and Gate F runs are operator-run evidence. They are not
replaced by fast CI tests; fast tests prove contracts while release evidence proves deployment claims.
Record candidate-build evidence using the
[Enterprise Release Evidence Checklist](Enterprise_Release_Evidence_Checklist.md).

## OS Boundary Wording

ETL-SQL policy enforcement is an application control. It does not provide OS-level containment against
local administrators, users who can run arbitrary alternate binaries, or service accounts with broader
filesystem/network permissions than the deployment guide allows.

When an organization needs mandatory host-level enforcement, deploy Windows Defender Application
Control, AppLocker, Linux MAC policy, container sandboxing, or equivalent controls outside ETL-SQL.
Documentation, release notes, and examples must not claim that ETL-SQL alone prevents a determined
administrator from running another executable or modifying the host.
