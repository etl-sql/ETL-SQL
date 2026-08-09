# ETL-SQL Enterprise Platform Strategy

**Status:** Active product strategy
**Scope:** Enterprise operating model, trust boundaries, adoption path, and control objectives
**Implementation sequence:** See [`ROADMAP.md`](../../../ROADMAP.md)
**Deployment-profile architecture:** See [`DeploymentProfiles.md`](../DeploymentProfiles.md)
**Current implementation:** See [`Docs/Architecture`](..) and the administrator guides

---

## 1. Purpose

ETL-SQL begins as a script-first developer tool: one person can write a readable data workflow, run it locally, inspect the results, and place the script in source control.

The enterprise direction preserves that workflow while adding the controls required for an organization to operate ETL-SQL as shared internal infrastructure.

The target is:

> ETL-SQL grows from a secure developer tool into a centrally governed internal data automation platform without abandoning script-first workflows or requiring enterprise infrastructure for small deployments.

This strategy defines what "enterprise-ready" means for ETL-SQL. It does not make every deployment complex, and it does not require a small team to operate systems it does not need.

## 2. Product Transition

The enterprise transition is not a replacement of the original product. It is a progression:

| Stage | Primary user | Operating model | Required infrastructure |
| :--- | :--- | :--- | :--- |
| Developer workstation | Individual developer or analyst | Local scripts, interactive execution, source control | Local filesystem and SQLite where persistence is needed |
| Single-team server | Small data or reporting team | Shared Portal and Orchestrator on one server | SQLite, local storage, supported backup and recovery |
| Governed departmental service | Department with internal controls | Central policy, named secrets, identity integration, durable audit | Single server or HA deployment |
| HA enterprise deployment | Business-critical shared service | Multiple Portal and Orchestrator nodes | PostgreSQL, shared storage, load balancer, distributed leases |
| Isolated departmental fleet | Multiple departments or environments | Separate security boundaries with centralized read-only oversight | Repeatable isolated deployments and scoped aggregation |

Each stage must remain a valid supported destination. A team should not be forced to adopt HA, external identity, a vault, or centralized auditing merely to use ETL-SQL safely on one server.

## 3. Strategic Principles

### 3.1 Script First Remains Non-Negotiable

Scripts remain the source of truth for pipelines, reports, schedules, and scriptable administration. Enterprise controls govern whether and how a script may run; they do not replace scripts with an opaque designer or database-only configuration.

### 3.2 Start Small, Grow Without Rewriting

A script developed locally should remain usable when promoted to a team server or enterprise deployment. Infrastructure references, credentials, identities, and policy may change between environments, but the workflow logic should not require a redesign.

### 3.3 Secure Defaults Before Optional Enterprise Integrations

Local deployments must be secure without depending on paid or externally hosted services. Integrations such as OIDC, HTTPS vaults, SIEM collectors, load balancers, and PostgreSQL add capability, but the engine's core security boundaries remain local and mandatory.

### 3.4 Organization Policy Can Restrict, Never Weaken

Policy follows a one-way restriction model. A lower authority level may make behavior more restrictive, but it may not override a stronger organizational or engine rule.

### 3.5 Source Systems Remain Authoritative

ETL-SQL does not bypass database, filesystem, API, or operating-system permissions. Platform authorization determines whether ETL-SQL permits an action; the target system independently determines whether the assigned service identity may perform it.

### 3.6 Operational Simplicity Is a Design Constraint

The platform must be operable by a small team and maintainable by a single project developer. New infrastructure is introduced only when it solves a concrete availability, security, or governance requirement.

### 3.7 Enterprise Claims Require Evidence

Availability, isolation, audit, recovery, and policy claims must be backed by automated tests or repeatable certification drills. Documentation alone is not evidence that a control works.

## 4. Enterprise Operating Model

Enterprise use separates responsibilities without requiring every small deployment to staff each role independently.

| Role | Responsibilities | Must not implicitly receive |
| :--- | :--- | :--- |
| Script author | Develops and tests pipelines and reports | Production publication, scheduling, secrets, or policy administration |
| Publisher | Promotes reviewed scripts and reports into controlled environments | Organization policy administration or unrestricted secret access |
| Operator | Runs services, performs upgrades, backup, restore, and incident response | Permission to alter pipeline logic merely through host access |
| Security administrator | Defines locked policies, identity mappings, secret providers, and audit requirements | Routine script-authoring or self-approval privileges |
| Approver | Reviews sensitive production changes | Ability to approve their own request |
| Auditor | Reads execution, policy, approval, and administrative evidence | Script execution or configuration mutation |
| Report consumer | Views authorized reports and exports | Script source, execution credentials, publishing, or scheduling rights |
| Service account | Executes a narrowly scoped unattended workload | Interactive login or broad inherited permissions |

One person may hold several roles in a small installation. The platform must still model the responsibilities separately so the organization can divide them later without changing the application model.

## 5. Authority Model

Enterprise controls are evaluated in the following order:

1. **Engine invariants**
   - Hard security boundaries implemented by the runtime.
   - Examples include protected script immutability, path resolution, sensitive-value redaction, and provider exception sanitization.
   - These cannot be weakened by policy, configuration, administrators, or scripts.

2. **Organization policy**
   - Signed or otherwise integrity-validated policy controlled by the organization.
   - Defines allowed connectors, destinations, filesystem roots, secret providers, remote execution behavior, audit requirements, and mandatory limits.

3. **Environment policy**
   - Applies stricter rules for development, test, production, or a department.
   - May narrow organization allowances but may not expand them.

4. **Identity and resource permissions**
   - Determines which users, groups, and service accounts may author, publish, schedule, execute, administer, approve, or view a resource.

5. **Script settings**
   - Configure a particular workflow within the allowed policy envelope.
   - A script may request stricter behavior but cannot relax a locked control.

6. **Source-system authorization**
   - The external database, API, filesystem, or service applies its own authorization using the identity assigned to the connection.

The effective permission is the intersection of all applicable layers. Passing one layer never bypasses another.

## 6. Trust Boundaries

### 6.1 Authoring Boundary

Developer workstations, the TUI, VS Code, notebooks, and local CLI execution are authoring environments. They are not automatically trusted production control planes.

Required properties:

- Scripts remain plain text and source-control friendly.
- Local linting can report organization policy violations before promotion.
- Local credentials are not assumed to be valid in production.
- Development policy caches cannot grant production authority.

### 6.2 Execution Boundary

Portal and Orchestrator hosts execute scripts with assigned service identities. They are trusted to enforce platform policy but are not trusted to invent permissions beyond those granted by policy and source systems.

Required properties:

- Policy is evaluated before execution and again at sensitive operation boundaries.
- The exact script version or hash is recorded for unattended execution.
- Execution identities and interactive user identities are distinguishable.
- A node that loses its distributed lease cannot continue writing shared state.

### 6.3 State Boundary

SQLite is the supported local state store for a single-server deployment. PostgreSQL is the shared state store for multiple Portal or Orchestrator nodes.

Required properties:

- Database migrations are versioned and testable.
- Backup and restore preserve catalog, permission, schedule, and audit consistency.
- Multi-node state uses transactional leases and fencing.
- Application nodes do not rely on local database files in HA mode.

### 6.4 Artifact Storage Boundary

Scripts, report snapshots, cached datasets, exports, and related artifacts pass through a governed storage provider.

Required properties:

- Paths are resolved and validated at the storage boundary.
- Protected scripts cannot be mutated through storage APIs.
- Shared deployments do not depend on node-local artifacts.
- Departmental deployments use separate storage credentials and roots.

### 6.5 Secret Boundary

Scripts refer to secrets by logical name. Secret values are resolved only when required for execution and are never returned through configuration export, diagnostics, logs, manifests, or browser APIs.

Required properties:

- Environment and operating-system providers support small deployments.
- HTTPS vault providers support organizations that already operate one.
- Secret access is scoped to the executing identity and environment.
- Configuration backup records references, not secret values.

### 6.6 Identity Boundary

Local identity remains available for standalone use. OIDC provides enterprise user authentication, while service accounts provide non-interactive identity.

Required properties:

- Authentication is delegated to the configured identity provider where enabled.
- Group claims map to platform groups through explicit rules.
- MFA and conditional access remain the identity provider's responsibility.
- Disabled users and changed group memberships take effect predictably.

### 6.7 Audit Boundary

The local transactional audit record is the source event. Remote SIEM delivery is an asynchronous, durable transport concern.

Required properties:

- A state mutation and its audit event commit atomically.
- Remote delivery supports retry, batching, deduplication, and backpressure.
- Policy defines whether mutations continue or fail closed when audit delivery is unavailable.
- Audit records identify actor, effective identity, action, resource, result, policy version, script hash, node, and correlation ID where applicable.

### 6.8 Connected-System Boundary

Every connector crosses into an independently controlled system.

Required properties:

- Connector credentials are narrowly scoped.
- Provider errors are sanitized before crossing back into the engine.
- Network destinations can be constrained by policy.
- ETL-SQL authorization never substitutes for permissions on the connected system.

## 7. Enterprise Control Capabilities

### 7.1 Central Policy Enforcement

Policy must operate on parsed statements and normalized connector configuration, not fragile text matching.

The policy system should support:

- Forbidden operations
- Allowed operations
- Constrained values or ranges
- Locked settings that scripts cannot override
- Allowed connector types and destinations
- Allowed filesystem and storage roots
- Required encryption and transport settings
- Execution limits and timeout ceilings
- Rules that differ by environment or identity

Policy is evaluated during linting for early feedback and during execution for enforcement. Lint success is not an authorization grant.

### 7.2 Named Secret References

Scripts should identify a secret by purpose rather than embed environment-specific values.

Conceptual example:

```sql
CREATE CONNECTION sales AS POSTGRES(
    HOST = 'sales-db.internal',
    DATABASE = 'Sales',
    USER = 'etl_service',
    PASSWORD = 'SECRET:sales_db_password'
);
```

The exact syntax must not ship until parser, runtime, policy, documentation, and migration behavior are defined together.

### 7.3 Identity and Authorization

Enterprise identity must answer two separate questions:

1. Who is the human or service making the request?
2. What may that identity do to this platform resource?

OIDC authenticates people. Scoped service accounts authenticate unattended workloads. Platform roles and group-based resource permissions authorize actions.

### 7.4 Promotion and Approval

Development, test, and production are distinct environments rather than flags on the same shared records.

Promotion should:

- Export versioned, reviewable configuration and script artifacts.
- Remove environment-specific credentials and secret values.
- Validate target policy before mutation.
- Support `SET WHAT_IF ON` or an equivalent dry-run.
- Record who requested, reviewed, approved, and applied the change.

High-risk actions may require approval. A requester cannot approve their own change, and approval becomes invalid if the script, permissions, policy, or target configuration changes.

### 7.5 Script Integrity

Unattended jobs and published reports record the hash of the reviewed script version.

At execution time the platform compares the current script hash with the pinned hash and applies policy:

- Warn and record the mismatch.
- Block until the changed version is reviewed and promoted.

Hash pinning detects drift. It does not claim signer identity and does not replace source-control review.

### 7.6 Durable Auditing

Administrative changes, publication, scheduling, execution, approvals, policy decisions, secret-resolution attempts, and access to sensitive exports must produce structured audit events.

The audit design must distinguish:

- Request accepted versus operation completed
- Human identity versus execution service identity
- Policy denial versus source-system denial
- Retried delivery versus duplicated business action
- Configuration change versus data-processing activity

### 7.7 Recovery and Operational Support

Enterprise safety includes recoverability, not only prevention.

Supported operator workflows must include:

- Environment diagnostics
- Redacted support bundles
- Configuration export and reconstruction
- Database and artifact backup
- Split-custody key backup
- Restore validation
- Upgrade validation from the prior supported release
- Documented rollback or forward-recovery behavior

### 7.8 High Availability

HA removes single-node service dependency. It does not by itself provide governance, isolation, or disaster recovery.

The practical HA design uses:

- PostgreSQL for shared application state
- Shared governed artifact storage
- Database-backed heartbeats, leases, and fencing tokens
- Load-balanced stateless Portal nodes
- Lease-aware Orchestrator workers
- Health checks that cover database, storage, and lease connectivity
- Rolling deployment migration rules

SQLite remains the correct default for a single-server installation. PostgreSQL may also be selected from the first deployment when the organization already requires multiple nodes or managed database operations. SQLite-to-PostgreSQL migration exists for teams that grow later; it is not a mandatory starting path.

### 7.9 Departmental Isolation

The initial enterprise isolation model is multiple independent deployments, not shared-table multitenancy.

Each department or environment receives:

- Separate database
- Separate artifact storage root and credentials
- Separate encryption and Data Protection keys
- Separate service identity
- Separate policy scope
- Separate network access

A future fleet view may aggregate health and audit metadata through read-only, scoped service accounts. It must not blend department data, permissions, secrets, or execution authority.

## 8. Availability and Failure Posture

Enterprise behavior must be explicit when a dependency is unavailable.

| Dependency failure | Default posture | Policy choices |
| :--- | :--- | :--- |
| Organization policy unavailable but valid cache exists | Continue until cache expiry | Shorter cache or immediate fail closed |
| Organization policy unavailable and cache expired | Deny governed execution | No fail-open mode for locked production policy |
| Secret provider unavailable | Fail the operation requiring the secret | Retry may be allowed within bounded limits |
| Identity provider unavailable | Existing sessions follow configured token lifetime; new login fails | Local break-glass account may be configured and audited |
| Audit collector unavailable | Queue transactionally in the local/shared outbox | Policy may block mutations after age or size threshold |
| PostgreSQL unavailable in HA mode | Stop accepting state mutations and stop claiming work | Read-only cached report access may be separately defined |
| Shared artifact storage unavailable | Do not execute work requiring inaccessible artifacts | Cached read-only content may be served if integrity is known |
| Node loses lease or fencing authority | Cancel local work and reject stale writes | No continue-running option |
| Source system unavailable | Apply script retry/error policy | Platform policy may impose stricter retry limits |

Every fail-open exception must be explicit, bounded, observable, and documented. Security-critical authorization and stale-writer protection fail closed.

## 9. Deployment and Adoption Paths

### 9.1 Path A: Individual to Team Server

1. Develop scripts locally using CLI, TUI, VS Code, or notebooks.
2. Place scripts in source control.
3. Deploy Portal and Orchestrator on one server.
4. Use SQLite and local governed storage.
5. Configure backup, restore, diagnostics, and script-first reconstruction.

This is a complete supported deployment, not a trial version of HA.

### 9.2 Path B: Team Server to Governed Service

1. Define organization and environment policy.
2. Replace embedded environment credentials with named secret references.
3. Integrate OIDC where available.
4. Create scoped service accounts for schedules.
5. Enable durable audit delivery.
6. Add approval requirements for selected production actions.

Governance does not require multiple application servers.

### 9.3 Path C: Single Server to HA

1. Validate backup and script-first reconstruction.
2. Provision PostgreSQL and shared storage.
3. Dry-run and verify SQLite-to-PostgreSQL migration.
4. Cut over state and artifacts.
5. Add additional Portal and Orchestrator nodes.
6. Enable distributed leases, fencing, health checks, and load balancing.
7. Run failover and rolling-upgrade certification.

Application scripts should not change during this transition.

### 9.4 Path D: HA From the First Deployment

Organizations may begin with PostgreSQL, shared storage, and multiple nodes. The migration tooling is only for deployments that began with SQLite.

### 9.5 Path E: Departmental Fleet

1. Establish an isolated deployment per department or environment.
2. Assign distinct identities, databases, storage, keys, and policies.
3. Promote artifacts through export, validation, approval, and import.
4. Add read-only fleet aggregation only after isolation tests prove it cannot become a pivot path.

## 10. Relationship to the Product Roadmap

This strategy defines the destination. [`ROADMAP.md`](../../../ROADMAP.md) defines the delivery order:
[`docs/architecture/decisions/Enterprise_Release_Gates.md`](../decisions/Enterprise_Release_Gates.md) defines the
release-gate evidence required before enterprise claims are made.

1. Standalone correctness and script-first reconstruction
2. Operator tooling, backup, restore, and upgrade validation
3. Practical HA with PostgreSQL and shared storage
4. Governance core with policy, secrets, and durable auditing
5. Enterprise identity and approvals
6. Departmental isolation and fleet visibility
7. Stable v1.0 release gates

The order is intentional:

- Recovery precedes clustering because an unrecoverable cluster is not production-ready.
- Shared state precedes multi-node operation.
- Governance builds on durable state and audit transactions.
- Approval workflows depend on reliable identity and governance records.
- Fleet aggregation follows proven departmental isolation.

Individual capabilities may be delivered earlier when they have low coupling, but later phases must not be used to conceal missing foundations.

## 11. Architecture Handoff

This file remains a strategy document. As capabilities ship, stable implementation details move into architecture documents.

Expected architecture documents include:

- `Docs/Architecture/DistributedDeployment.md`
- `Docs/Architecture/PolicyEnforcement.md`
- `Docs/Architecture/IdentityAndAuthorization.md`
- `Docs/Architecture/Auditing.md`
- `Docs/Architecture/ArtifactStorage.md`

An architecture document must describe the code and behavior that currently exist. It must not present this target strategy as already implemented.

Standards documents should capture mandatory invariants that contributors must follow, such as policy evaluation contracts, audit event requirements, secret-provider behavior, and distributed lease fencing.

## 12. Single-Developer Scope Guardrails

The enterprise strategy is intentionally constrained so one developer can implement and maintain it incrementally.

### Build

- PostgreSQL as the only initial shared-state provider
- Local and SMB/UNC as the initial artifact storage providers
- OIDC through standard ASP.NET Core integration
- Environment, OS-protected, and HTTPS secret providers
- Transactional outbox with one HTTPS audit transporter
- Isolated deployments instead of shared-table multitenancy
- Scripted and CLI-driven administration
- Small, testable interfaces at existing ownership boundaries

### Defer Until Demand Exists

- Oracle or MySQL application-state providers
- Kubernetes operators
- Built-in HSM or cloud-specific KMS integrations
- A general workflow designer
- Shared-table multitenancy
- Cross-region active-active execution
- A custom identity provider
- A custom SIEM platform
- A marketplace or hosted control plane

The platform should integrate with enterprise systems, not attempt to recreate them.

## 13. Enterprise Readiness Criteria

ETL-SQL can credibly describe a deployment as enterprise-governed when:

- Every production execution has an attributable human or service identity.
- Policy is enforced consistently across CLI, IDE, Portal, and Orchestrator execution.
- Scripts cannot weaken locked organization controls.
- Secrets are referenced, scoped, redacted, and excluded from exports.
- Production publication and scheduling permissions are separate from authoring.
- Sensitive changes can require non-self approval.
- Mutations and audit events commit atomically.
- The exact script version or hash is recorded for unattended work.
- Backup, restore, reconstruction, and prior-version upgrade drills pass.
- HA deployments reject stale writers and recover claimed work safely.
- Departmental deployments do not share databases, storage credentials, keys, or service identities.
- Support bundles and diagnostics are automatically redacted.
- Failure behavior for policy, identity, secrets, audit, state, and storage is documented and tested.

## 14. Non-Goals

- ETL-SQL will not become a visual-designer-first product.
- Enterprise controls will not require rewriting script logic into proprietary workflow objects.
- HA will not be required for a secure single-server deployment.
- Governance will not require customers to purchase a specific vault, SIEM, identity provider, or database service.
- ETL-SQL will not bypass connected-system authorization.
- The initial design will not implement shared-table multitenancy.
- The platform will not claim full protection after the host operating system or administrator account is compromised.
- Script hash pinning will not be presented as cryptographic signer attribution.
- ETL-SQL will not recreate source control, identity providers, secret vaults, or SIEM products.

## 15. Decisions and Open Questions

### Decisions

| Question | Decision |
| :--- | :--- |
| Is enterprise capability a separate product mode? | No. It is a progressive deployment and governance model for the same script-first product. |
| Is SQLite still supported? | Yes, for complete single-server deployments. |
| Can an organization start directly with HA? | Yes. Migration is only needed when growing from SQLite. |
| Initial shared-state provider | PostgreSQL |
| Initial isolation model | Separate departmental/environment deployments |
| Initial policy enforcement model | Parsed AST and normalized connector settings, checked at lint and execution time |
| Initial script integrity model | Hash pinning plus audit evidence |
| Initial remote audit transport | Durable transactional outbox with HTTPS delivery |
| Is Kubernetes required? | No |
| Are enterprise integrations mandatory for local use? | No |

### Decisions Required During Implementation

| Decision | Must be resolved before |
| :--- | :--- |
| Policy document signing or integrity-validation mechanism | Governance policy schema is finalized |
| Exact named-secret syntax and resolution lifecycle | `ISecretProvider` becomes public |
| Default audit fail-closed thresholds | Remote audit delivery is enabled in production |
| PostgreSQL migration cutover and rollback guarantees | Database migration CLI is declared stable |
| Session-affinity versus distributed interactive session state | Multi-node Portal certification |
| Break-glass account storage, restrictions, and audit behavior | OIDC is recommended for production |
| Approval scope and invalidation rules | Approval workflows are exposed publicly |
| Fleet aggregator metadata contract | Departmental fleet implementation begins |

---

This strategy should be reviewed whenever the roadmap changes the enterprise deployment model, introduces a new authority layer, or expands the trust granted to a host, identity, storage provider, or remote service.
