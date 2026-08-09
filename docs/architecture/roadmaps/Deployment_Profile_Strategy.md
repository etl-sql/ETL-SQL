# ETL-SQL Deployment Profile and Portability Strategy

**Status:** Active product strategy; certification implementation is candidate work

**Scope:** Supported deployment profiles, cumulative product guarantees, migration paths, and
release evidence

**Implementation sequence:** See [`ROADMAP.md`](../../../ROADMAP.md)

**Authoritative deployment architecture:** See
[`DeploymentProfiles.md`](../DeploymentProfiles.md)

**Normative current matrix and review contract:** See
[`Deployment_Profile_Standards.md`](../standards/Deployment_Profile_Standards.md)

**Related strategy:** See
[`Enterprise_Platform_Strategy.md`](Enterprise_Platform_Strategy.md),
[`Test_Strategy.md`](Test_Strategy.md), and
[`Release_Capability_Matrix.md`](Release_Capability_Matrix.md)

**SaaS subsystem architecture:** See
[`SaaSTenantIsolation.md`](../SaaSTenantIsolation.md) and
[`TenantPortability.md`](../TenantPortability.md)

---

## 1. Purpose

ETL-SQL is intended to begin on one workstation and grow into shared or hosted infrastructure
without abandoning its script-first model. This document turns that intention into a product
contract.

It defines four deployment profiles:

1. **Solo / Workstation**
2. **Team / SME**
3. **Enterprise / Corporate**
4. **SaaS / Multi-Organization**

These are support profiles, not product editions, licenses, or pricing tiers. They describe
increasing operational and trust boundaries. A capability should remain available at the smallest
profile where it can operate safely; larger profiles add collaboration, durability, availability,
policy, and isolation rather than replacing the underlying language.

The central promise is:

> A deployment can progress from Solo / Workstation through Team / SME and Enterprise / Corporate
> to SaaS / Multi-Organization without rewriting its `.etlsql`, `.rptsql`, rules, tags, assertions,
> or report definitions.

Infrastructure references, credentials, identities, storage providers, and policy may change
between environments. Pipeline and report logic should not.

## 2. Why Profiles and Certification Are Both Required

A profile document alone can describe intent but cannot prove that the product still honors it.
A certification suite without a normative profile definition can test incidental implementation
details without protecting the user journey.

ETL-SQL therefore needs both:

- **The deployment-profile architecture** defines the stable topology, provider, resource-binding,
  state-placement, and authority boundaries.
- **This strategy** defines adoption journeys, transition workflow, migration goals, and the
  certification program.
- **The deployment-profile standard** defines the current capability/evidence matrix and acceptable
  product claims.
- **A deployment-profile certification lane** executes representative journeys, validates
  transitions between profiles, and retains evidence for release decisions.

Until the certification lane is built, the requirements in this document are targets rather than
certified release claims. Current support status belongs in
`docs/architecture/standards/Deployment_Profile_Standards.md`; this strategy retains adoption,
migration, certification, and future-phase guidance.

## 3. Profile Definitions

| Profile | Typical owner | Operating boundary | Expected topology |
| :--- | :--- | :--- | :--- |
| **Solo / Workstation** | One developer, analyst, or operator | One trusted user operating local artifacts | CLI, VS Code, Workstation Editor, and Report Player; local files and SQLite where persistence is needed; optional local Orchestrator |
| **Team / SME** | A small data or reporting team | Multiple trusted or partially separated users within one team | Shared single-node Orchestrator and optional Portal; SQLite/local storage by default; shared connections, schedules, reports, roles, backup, and notifications |
| **Enterprise / Corporate** | One organization with multiple teams or departments | Central identity, policy, audit, and production operations | OIDC/service identities, PostgreSQL/shared storage where required, HA-capable Portal and Orchestrator, centralized secrets and policy, approvals, recovery, and departmental separation |
| **SaaS / Multi-Organization** | A platform operator serving independent customer organizations | Hard tenant boundary between mutually untrusted organizations | Tenant-scoped control and data planes, delegated administration, per-tenant policy/secrets/keys, fleet operations, metering, resource isolation, safe rollout, and tenant export |

Each profile is a supported destination. A solo operator must not need Portal, OIDC, PostgreSQL, or
HA to obtain correct execution, data-quality gates, lineage, reports, and useful diagnostics.
Likewise, a SaaS operator must not rely on workstation trust assumptions at a tenant boundary.

## 4. Cumulative Product Invariants

### 4.1 Artifact Portability

The following artifacts remain plain text, diffable, and portable across all profiles:

- `.etlsql` pipeline and administration scripts
- `.rptsql` reports
- Data-quality rules and `ASSERT JOB` gates
- Tags and lineage declarations
- Connector definitions containing configuration and `SECRET:name` references, never exported
  resolved secret values
- Job, refresh, and report definitions expressed in canonical language syntax

Profile promotion must not translate these artifacts into an opaque host-only representation.

### 4.2 Additive Capability

Moving to a larger profile may add:

- Shared persistence and scheduling
- Multiple identities and role separation
- Central secret resolution and policy
- Durable remote audit
- Approval workflows
- HA and disaster-recovery controls
- Tenant isolation and fleet operations

It must not silently remove valid language features or require a second version of a script.
Host policy may correctly reject an unsafe operation, but the diagnostic must identify the policy
boundary and the configuration or authorization needed to promote the workload.

### 4.3 One Semantic Model

Parser behavior, execution semantics, quality calculations, lineage, tags, report rendering, and
governance scoring must not change by profile. Portal and SaaS surfaces present the same engine and
catalog evidence available through CLI/Orchestrator read models; they do not recalculate more
favorable results in browser or tenant-specific code.

### 4.4 Smallest Safe Surface

Enterprise features should have a useful small-scale form whenever their security model permits it.
Examples include:

- Data-quality rules failing a CLI process before any Orchestrator is installed
- Local SQLite history and scheduled notification without Portal
- `SHOW ... INTO #table` governance inspection feeding a local `.rptsql` scorecard
- Local encrypted secrets or `SECRET:name` references before a centralized vault is configured
- File-based/source-controlled policy before a remote policy authority is introduced

Collaboration features such as multi-party approval cannot be meaningfully reduced to one person;
the smaller profile should expose an explicit not-applicable state rather than simulate separation
of duties.

### 4.5 Evidence Before Claims

A profile is not certified because its configuration keys exist. Certification requires a
representative end-to-end journey, failure coverage, retained evidence tied to the source commit,
and an upgrade or migration result where applicable.

## 5. Cross-Profile Capability Matrix

This matrix defines the expected progression. Detailed feature strategies remain authoritative for
their individual domains.

| Concern | Solo / Workstation | Team / SME | Enterprise / Corporate | SaaS / Multi-Organization |
| :--- | :--- | :--- | :--- | :--- |
| Authoring | Local source-controlled scripts | Shared repository and team promotion | Controlled environments and reviewed promotion | Tenant-authorized catalog or controlled ingress |
| Execution | Interactive CLI/editor or local automation | Shared schedules and operators | Governed service identities and distributed ownership | Tenant-scoped execution with quotas and isolation |
| State | Memory, files, SQLite where needed | Single-node SQLite/local artifact roots | PostgreSQL/shared storage for HA; governed backups | Tenant-partitioned durable state and artifacts |
| Identity | Local OS/process identity | Local accounts or basic shared identity | OIDC, groups, service accounts, approvals | Platform identity plus tenant identity and delegated admin |
| Secrets | Local protected values and references | Shared protected catalog | External providers, rotation, policy, audit | Tenant-scoped providers/keys with platform separation |
| Quality and stewardship | Rules, assertions, local inspection/reports | Durable history, baselines, schedules, notifications | Queues, assignments, policy, audit, approvals | Tenant-isolated evidence and delegated governance |
| Reporting | Report Player/local output | Shared reports and optional Portal | Governed catalog, HA refresh, access control | Tenant catalog, isolation, metering, safe embeds |
| Availability | Restart and local recovery | Supported backup/restore | HA, lease fencing, health probes, DR runbooks | Fleet rollout, tenant-aware recovery, noisy-neighbor control |
| Audit | Local execution and security evidence | Durable shared history | Remote durable audit and fail-closed policy | Tenant-complete audit plus platform-operator audit |
| Administration | One operator may hold all roles | Roles can be assigned within a team | Separation of duties and scoped administration | Platform admin cannot become tenant user implicitly |

## 6. Required Journey Stories

Certification should organize around user journeys rather than a list of configuration options.
Every journey must state which profiles apply and what stronger boundary is added at each step.

### DP-01 — Author and Run a Portable Pipeline

A solo user writes and runs a source-controlled pipeline locally. The same artifact is promoted to a
team schedule, then to governed enterprise execution, then into a SaaS tenant without changing its
business or transformation logic.

### DP-02 — Configure Connections and Secrets

A user promotes connection definitions without exporting resolved credentials. Each target
environment binds the same logical connection identity to environment-owned secret references and
authorized network destinations.

### DP-03 — Schedule, Observe, and Notify

A local script can return a meaningful exit code. A team can persist history and send SMTP/WEBHOOK
notifications. Enterprise adds service ownership, escalation, audit, and HA execution. SaaS keeps
history, notifications, quotas, and operator visibility tenant-scoped.

### DP-04 — Inspect Data Quality and Stewardship

Rules, assertions, tags, lineage, and their calculations remain identical across profiles. A solo
operator can run health and stewardship reports without Portal; larger profiles add shared history,
workflow, assignment, approval, and isolation.

### DP-05 — Author, Publish, and Consume a Report

A `.rptsql` report runs locally, can be shared by a team, can enter an enterprise catalog with
access control, and can be imported into a tenant catalog without changing its datasets, visuals,
or parameter semantics.

### DP-06 — Add Users Without Rebuilding Assets

Moving beyond Solo introduces identities and permissions around existing resources. Ownership and
role mappings are added during promotion; resources are not recreated solely to acquire access
control.

### DP-07 — Back Up, Restore, and Recover

Each persistent profile documents its state, artifacts, keys, and restore order. Restore
certification verifies usability, not only file presence. Enterprise and SaaS add distributed-state
and tenant-scope recovery requirements.

### DP-08 — Promote Between Environments

Development, test, and production can bind different configuration, secrets, and policy to the same
versioned artifact. Promotion records the artifact hash and rejects unresolved environment
requirements before execution.

### DP-09 — Upgrade Product Version

Each profile passes an N → N+1 upgrade drill covering schema migration, artifacts, configuration,
job continuity, and rollback or documented restore. Multi-node profiles additionally cover rolling
compatibility and fencing.

### DP-10 — Grow the Topology

A single-node deployment can move its supported state to shared providers and add nodes without
changing job/report definitions. Health, affinity, leases, key rings, and shared artifact roots are
validated before traffic moves.

### DP-11 — Enter or Leave a SaaS Tenant

An organization can be imported into a tenant with explicit ownership and identity mappings.
The tenant can export its portable artifacts and permitted metadata without receiving platform
secrets, other tenants' records, or provider internals.

### DP-12 — Prove Isolation and Failure Containment

Enterprise departmental boundaries and SaaS tenant boundaries are tested with negative discovery,
authorization, cache, storage, artifact, secret, job, audit, and resource-exhaustion cases. A
successful happy path alone is insufficient evidence.

## 7. Promotion and Upgrade Contract

“Upgradeable” means more than the target version starts. Every supported transition follows a
repeatable contract.

### 7.1 State Classes

Promotion tooling must classify state before moving it:

| State class | Examples | Required treatment |
| :--- | :--- | :--- |
| Portable source artifact | Scripts, reports, rules, tags, declarative jobs | Preserve content and stable identity; do not rewrite logic |
| Exportable catalog state | Schedules, folders, connection metadata, ownership, policy references | Versioned export/import with validation and collision reporting |
| Environment binding | Hostnames, paths, secret-provider bindings, service identities | Explicit target mapping; never assume the source value is valid |
| Protected material | Resolved passwords, tokens, encryption keys | Never export as ordinary configuration; rebind or transfer through an approved protected mechanism |
| Operational evidence | Job history, lineage catalog, quality metrics, audit records | Allow policy-controlled retention/import; preserve provenance and timestamps |
| Ephemeral state | Interactive sessions, leases, caches, in-flight work | Drain, expire, or reconstruct; never promote as durable ownership |

### 7.2 Transition Workflow

Every supported promotion or topology upgrade should provide:

1. **Inventory and preflight** — identify artifacts, state, required mappings, unsupported
   features, name collisions, capacity, and target policy conflicts without mutation.
2. **Backup/export** — create a versioned, checksummed export and record the source product/schema
   version.
3. **Target binding** — map identities, owners, paths, connector endpoints, and secret references.
4. **Import/migration** — apply idempotently or fail before partial activation; preserve stable
   resource identity where possible.
5. **Validation** — parse/lint artifacts, resolve non-secret dependencies, compare counts, and run
   representative read-only or `WHAT_IF` checks.
6. **Cutover** — stop or fence the prior scheduler, activate target ownership, and prevent duplicate
   execution.
7. **Post-cutover proof** — run representative pipelines/reports, verify history/lineage/quality
   continuity, notifications, authorization, and audit.
8. **Rollback or restore** — document the last reversible point and prove recovery when the
   migration class permits it.

### 7.3 Required Transitions

The certification program must cover:

- Solo / Workstation → Team / SME
- Team / SME → Enterprise / Corporate
- Enterprise / Corporate → SaaS / Multi-Organization
- Solo / Workstation → SaaS / Multi-Organization onboarding
- N → N+1 within every profile

The SaaS transitions may be export/import onboarding rather than an in-place infrastructure
upgrade. The portability guarantee applies to customer artifacts and eligible metadata, not to
turning a customer's workstation database directly into shared SaaS control-plane state.

## 8. SaaS Is a Distinct Trust Boundary

The authoritative subsystem design is
[`SaaSTenantIsolation.md`](../SaaSTenantIsolation.md); tenant migration and exit are defined in
[`TenantPortability.md`](../TenantPortability.md). This section defines the certification strategy.

Workstation through Enterprise is primarily an additive topology and control progression. SaaS adds
mutually untrusted organizations and therefore requires more than additional servers and
configuration.

Before the SaaS profile can be certified, tenant identity must be carried and enforced across:

- Authentication, authorization, and delegated administration
- Database rows, queries, migrations, backups, and restores
- Artifact paths and object storage
- Connections, secrets, encryption keys, and caches
- Jobs, queues, leases, schedules, results, and notifications
- Reports, embeds, exports, lineage, quality evidence, and audit
- Logs, metrics, traces, support tooling, and platform-operator access
- Resource limits, concurrency, capacity admission, and noisy-neighbor containment

Tenant context must be server-derived from authenticated authority, never trusted from a caller's
unverified resource identifier. Platform administration must be separately audited and must not
implicitly grant the ability to impersonate a tenant user.

## 9. Certification Model

### 9.1 Coverage Status

Use the existing release-capability meanings:

- **Green** — representative end-to-end proof plus focused tests and retained current evidence.
- **Yellow** — implementation or focused tests exist, but the profile journey or transition is not
  proven end to end.
- **Red** — known gap, unsafe assumption, or no evidence.
- **N/A** — the capability genuinely does not apply; include a reason rather than treating it as a
  pass.

The matrix should score each journey separately for each profile and each required transition.
One green profile must not make the entire capability green.

### 9.2 Planned Executable Lane

Add a cross-platform entry point:

```powershell
.\scripts\Test-DeploymentProfileCertification.ps1 -Profile Solo
.\scripts\Test-DeploymentProfileCertification.ps1 -Profile Team
.\scripts\Test-DeploymentProfileCertification.ps1 -Profile Enterprise
.\scripts\Test-DeploymentProfileCertification.ps1 -Profile SaaS
.\scripts\Test-DeploymentProfileCertification.ps1 -Transition SoloToTeam
```

The lane should orchestrate existing focused tests and add missing end-to-end fixtures rather than
duplicate the scale, connector, enterprise-hardening, HA, and pre-release suites. Expensive external
topologies may remain operator/manual certification, but their evidence format and freshness rules
must be machine-verifiable.

### 9.3 Evidence

Retained JSON and Markdown evidence should record:

- Source commit, product version, schema version, platform, and timestamp
- Profile or transition under test and exact topology
- Scenario identifiers and terminal result
- Artifact hashes before and after promotion
- Imported/skipped/failed resource counts and mapping decisions
- Data-quality, lineage, job-history, and report continuity checks
- Negative authorization/isolation results where applicable
- Backup, rollback, or restore result
- References to existing certification artifacts used as supporting evidence

Release notes and product documentation must not claim a profile or transition beyond its current
evidence status.

## 10. Deployment Overlays

The following are overlays, not additional profile sizes:

- Regulated or high-assurance operation
- Air-gapped or disconnected operation
- High-volume/large-data workloads
- High availability
- Disaster recovery
- Region or data-residency constraints

An overlay may apply to Team, Enterprise, or SaaS and adds certification requirements to that
profile. For example, HA is typical at Enterprise scale but should not redefine the meaning of
Enterprise; a single-node governed corporate deployment can still be valid when its availability
requirements permit it.

## 11. Non-Goals

This strategy does not:

- Define packaging, price, license, or commercial editions
- Require every enterprise integration in smaller profiles
- Claim that all current profiles or transitions are already certified
- Treat direct database copying as a supported migration mechanism
- Promise that resolved secrets or in-flight sessions can be migrated
- Guarantee arbitrary down-migration from a larger topology into a smaller one
- Replace connector, scale, security, HA, or enterprise certification

## 12. Definition of Done

This strategy is implemented when:

1. The four profiles have a maintained capability and journey matrix with no ambiguous
   “enterprise only” features that could safely serve smaller deployments.
2. Canonical scripts, reports, rules, tags, and assertions run unchanged across applicable
   profiles.
3. Versioned inventory, preflight, export/import, mapping, validation, cutover, and recovery tooling
   covers every required transition.
4. N → N+1 upgrade drills pass within all four profiles.
5. Workstation → Team → Enterprise and direct Workstation → SaaS promotion preserve portable
   artifacts and prove the documented continuity fields.
6. SaaS certification proves tenant isolation and failure containment across every state and
   execution boundary listed above.
7. `Test-DeploymentProfileCertification.ps1` produces retained, commit-bound evidence and is
   composed into the appropriate release gates.
8. Administration and onboarding guides provide supported instructions for entering, operating,
   upgrading, exporting, and recovering each profile.
