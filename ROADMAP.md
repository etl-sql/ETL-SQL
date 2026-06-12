# ETL-SQL Product Roadmap

This document describes future releases and their intended implementation sequence. `TODO.md` contains
only active-release work. When development begins on a roadmap release, move its first executable phase
into `TODO.md`, break it into reviewable tasks, and leave later phases here until their prerequisites pass.

The roadmap is deliberately optimized for a product that can be built and maintained by one developer
and operated by a small team.

---

## Product Direction

ETL-SQL should grow through a progressive deployment model:

1. Start with one server, SQLite, and local storage.
2. Move the same installation to PostgreSQL and shared storage when additional application nodes are
   needed.
3. Add governance without requiring a cluster.
4. Add enterprise identity and narrowly scoped approval controls.
5. Isolate departments with repeatable dedicated deployments before considering shared multitenancy.

A customer may start at any supported level. The SQLite-to-PostgreSQL path exists for customers who grow
from a single server; it is not required for a new HA installation.

### Complexity Rules

- Certify one new database provider or storage model at a time.
- Prefer application-owned migration and administration commands over customer-written SQL.
- Preserve the SQLite/local-storage path as the simplest supported installation.
- Add an abstraction only when the next release uses it.
- Do not expose shared multitenancy until dedicated deployments prove insufficient.
- Do not add vendor-specific infrastructure where a small provider-neutral contract is sufficient.
- Treat operational tooling, backup, restore, upgrade, and diagnostics as product features.
- Make release claims only after automated certification proves them.

## Current Architectural Gaps

These are the main gaps future releases must address:

1. Portal state uses EF Core with SQLite while Orchestrator state uses a separate hand-written SQLite
   store. Scale-out needs a coherent durable control-state boundary.
2. Portal database selection is hard-coded to SQLite and has no provider-specific migration strategy.
3. Scripts, snapshots, datasets, maps, and Data Protection keys use direct filesystem paths rather than
   an artifact-storage boundary.
4. Portal execution limits, interactive sessions, rate limits, and caches are process-local.
5. Existing Orchestrator leases have owner and expiry but no fencing token to reject stale writers.
6. Backup, restore, migration, upgrade, and deployment diagnostics are primarily runbooks rather than
   application-owned commands.
7. JWT, dataset, SMTP, Orchestrator, connector, and script secrets use separate handling models.
8. Audit creation is not yet a universal transactional operation contract and remote delivery has no
   durable outbox.
9. OIDC configuration and documentation exist, but the runtime capability must be reconciled and
   certified before it is claimed as supported.
10. Mixed-version compatibility, schema readiness, migration leadership, and rollback boundaries are
    not yet defined for rolling deployments.

## Promotion Prerequisites

Before starting v0.12.0, complete or explicitly disposition the active P1 and P2 work in `TODO.md`.
In particular, establish:

- durable ownership and audit behavior;
- script-first clean-server reconstruction;
- multi-process and fault-injection test infrastructure;
- subscription delivery semantics;
- backup/restore and N-to-N+1 upgrade drills;
- operational metrics and log-hygiene guarantees.

These are not separate enterprise luxuries. They are the correctness baseline on which HA and governance
depend.

---

## Path to 1.0

Version 1.0 should define a stable product contract, not claim that every enterprise roadmap item is
complete. The recommended 1.0 scope is:

> A stable, script-first ETL and reporting platform suitable for supported single-server production
> deployments, with tested scheduling, portal hosting, backup, restore, and upgrades.

Practical HA, centralized governance, and department isolation are additive post-1.0 capabilities unless
the version-numbering decision below intentionally keeps the product in `0.x` until those releases ship.

### 1.0 Decision Timeline

Make these decisions in order.

#### Decision 1: Define the Supported Product - Before the 1.0 Release Branch

Publish one support matrix that distinguishes implemented, experimental, and supported behavior:

- operating systems and CPU architectures;
- CLI, TUI, VS Code, ReportPlayer, Orchestrator, and Report Portal support status;
- connectors grouped into certified, best-effort, and experimental tiers;
- supported authentication methods;
- supported single-server topology and recovery expectations;
- certified workload tiers and explicit non-goals;
- report formats and browser/runtime support;
- upgrade paths and versions that may be skipped.

The product must not imply that every connector or feature visible in source has the same support level.

#### Decision 2: Define the Language Contract - Before the 1.0 Release Candidate

The 1.0 language baseline must include:

- a canonical grammar, connector option reference, standard library, and report syntax reference;
- a documented deprecation period for syntax and connector options;
- rules for semantic changes that alter results without changing syntax;
- a script compatibility test corpus retained across future releases;
- a report manifest and dataset compatibility policy;
- an engine/report version command and machine-readable compatibility diagnostics;
- a migration-linter policy for future breaking releases.

Recommended policy:

- patch releases fix defects without intentionally changing valid script semantics;
- minor releases add backward-compatible capability;
- removals require at least one minor release of warning and a documented migration;
- unavoidable security fixes may override compatibility but must be called out prominently.

#### Decision 3: Productize Standalone Operations - Before 1.0

Complete the active backup, restore, upgrade, audit, ownership, and recovery work, then expose supported
operator workflows such as:

```text
etl-sql admin doctor
etl-sql admin backup
etl-sql admin restore --validate
etl-sql admin upgrade-check
etl-sql admin support-bundle
```

The support bundle must redact credentials and include version, configuration shape, migration state,
health, recent sanitized errors, storage/database diagnostics, and dependency inventory.

#### Decision 4: Define the Release Lifecycle - Before 1.0

Publish a small support policy that one maintainer can honor:

- the current minor release receives fixes;
- the previous minor receives critical security fixes for a stated transition period;
- upgrades are supported from a clearly named minimum version;
- database down-migration is not promised; rollback uses a tested backup where required;
- security reports have a private contact and acknowledgement target;
- end-of-life dates are announced before support ends.

Do not promise an enterprise SLA, 24-hour support, or long-term support branch until revenue and staffing
make those commitments realistic.

#### Decision 5: Reconcile Documentation with Runtime - Before 1.0 RC

- Trace every public capability claim to an automated test or mark it experimental.
- Resolve OIDC documentation/configuration versus runtime implementation.
- Remove stale product versions from README, packages, installers, and strategy documents.
- Verify every sample through the supported sample runner.
- Ensure all hosts use the canonical report runtime assets and language references.
- Add a release checklist item that rejects documentation claiming unsupported behavior.

#### Decision 6: Establish Distribution Trust - Before 1.0 General Availability

Use no-cost controls first:

- publish SHA-256 checksums for every release artifact;
- publish an SBOM and third-party notice inventory;
- build releases through a documented, repeatable workflow;
- retain test and certification summaries with the release;
- use repository-hosted provenance or artifact attestations when available;
- publish immutable version tags and container digests.

Commercial Windows code-signing certificates and other paid signing services are desirable, but they are
not a prerequisite for an honest 1.0 release. Add them when download volume, customer policy, or revenue
justifies the cost.

#### Decision 7: Select and Reconcile Licensing - Before Accepting Significant External Contributions

The repository currently has conflicting signals: the repository and VS Code extension carry PolyForm
Noncommercial terms while the extension package metadata says MIT. Resolve every source directory,
package manifest, installer, About page, and README in one licensing change.

This decision must occur before accepting substantial outside code. Relicensing becomes harder when
copyright is shared among contributors.

#### Decision 8: Decide Version Numbering - Before Starting the Planned HA Release

Choose one of these sequences:

- **Recommended:** finish the standalone hardening release, publish `1.0.0`, then renumber Practical HA,
  Governance, Identity, and Department Isolation as `1.1`, `1.2`, `1.3`, and `1.4`.
- **Alternative:** keep the current `0.12` through `0.15` numbering and publish `1.0` only after those
  releases, accepting that the stable standalone product remains pre-1.0 longer.

Do not let version numbering imply that a stable single-server product is unfinished merely because
optional distributed features remain on the roadmap.

### 1.0 Release Gates

Do not publish 1.0 until:

- active P1 work and release-blocking P2 work are complete or explicitly deferred with a documented
  boundary;
- representative ETL, report, scheduled job, subscription, portal, failure-recovery, backup/restore, and
  upgrade scenarios pass from a clean installation;
- the supported language and report contract is frozen and documented;
- supported connectors and deployment topology are listed explicitly;
- secrets do not appear in scripts, generated exports, logs, support bundles, or release artifacts;
- installers, archives, VSIX packages, and containers report one consistent version and license;
- licensing, contribution, security, release-lifecycle, and support policies are published;
- a new user can complete a documented script-to-scheduled-production workflow without maintainer-only
  knowledge.

---

## Licensing and Sustainability

This section records product direction, not legal or tax advice. Avoid writing a custom license without
professional review. Prefer an established, unmodified license whose meaning is already understood.

### What True Open Source Means

A true open-source license permits commercial use. It cannot require ordinary commercial users to buy a
runtime license. Financial benefit must therefore come from services, trusted distribution, convenience,
relationships, hosting, or features kept outside the open-source project.

Open source does not prevent the maintainer from charging money. It prevents exclusive control over who
may run or redistribute the open-source code.

### Available Models

#### Model A: Fully Open Source - Recommended While Maintainer Resources Are Limited

Release the full repository under Apache License 2.0.

Why Apache-2.0:

- it is a standard OSI-approved license;
- it permits commercial use, modification, and redistribution;
- it includes an explicit contributor patent grant;
- companies commonly understand and approve it;
- it requires no licensing server, activation code, or customer compliance investigation.

Keep the ETL-SQL project name, logos, release signing identity, websites, and service marks under a
separate trademark policy. The code license should not imply permission to impersonate official builds
or support.

Financial paths:

- paid installation, architecture, migration, and production-readiness engagements;
- annual support and maintenance subscriptions;
- priority issue triage and upgrade assistance;
- custom connector, report, governance, and integration development;
- training, workshops, documentation packages, and team onboarding;
- sponsored roadmap features with scope and delivery terms;
- certified builds, extended maintenance branches, and deployment validation;
- GitHub Sponsors or Open Collective funding;
- a hosted or managed ETL-SQL service later, if operating one becomes practical.

The official distribution can remain valuable even when the code is free: customers pay for trust,
accountability, response, expertise, and reduced operational risk.

Risks:

- another company may use or resell the code;
- license fees cannot be required for normal commercial use;
- support revenue depends on adoption and reputation;
- a hosted competitor is legally possible.

Mitigations are product velocity, documentation, community, trademark clarity, official release trust,
and customer relationships rather than legal restriction.

#### Model B: Open Core

License the language, engine, CLI, TUI, VS Code extension, ReportPlayer, and local report tooling under
Apache-2.0. Keep selected server-side enterprise modules under a commercial or source-available license.

Potential paid modules:

- centralized governance administration;
- enterprise identity administration;
- HA operations tooling;
- department fleet management;
- advanced audit and support tooling.

This preserves a license-revenue path, but it adds permanent costs:

- strict source and package boundaries;
- separate builds, documentation, tests, and release artifacts;
- decisions about which fixes belong in each edition;
- commercial terms and customer license administration;
- community concern when useful capabilities are moved behind a paid boundary.

Do not select this model until there is evidence that customers will pay for licenses rather than support
or implementation. If selected, use professional legal review before selling the first commercial
license.

#### Model C: Source-Available Noncommercial

Continue using PolyForm Noncommercial and sell commercial permission.

Advantages:

- preserves direct commercial-license leverage;
- allows source inspection and noncommercial use;
- requires no technical activation system.

Costs:

- the project cannot accurately be described as open source;
- commercial evaluation and internal developer use may require legal review;
- adoption, packaging, Linux distribution, and outside contribution may be harder;
- determining what is commercial can create friction;
- direct license sales still require terms, invoicing, support boundaries, and customer trust.

This model is valid, but it is not automatically easier to monetize than open source. A restriction has
value only when enough users adopt the product and are willing to purchase removal of that restriction.

### Recommended Sustainability Sequence

#### Stage 1: Adoption - Now Through 1.0

- Choose and apply one consistent license.
- Recommended default: Apache-2.0 for the full project.
- Publish a simple trademark statement.
- Add GitHub Sponsors when eligible; consider Open Collective only if community funding and transparent
  project expenses become meaningful.
- Publish a page inviting paid implementation, migration, connector, and training work.
- Do not build license activation or billing infrastructure.
- Keep telemetry opt-in and avoid requiring an account to use local software.

#### Stage 2: First Revenue - After Real Production Users Exist

Offer clearly bounded services:

- fixed-price installation and production-readiness review;
- paid migration or report-conversion packages;
- hourly or scoped custom development;
- annual support covering upgrade guidance, priority triage, and scheduled advisory sessions;
- sponsored features delivered to the open-source project.

Charge for work and response commitments, not artificial feature friction. Do not promise response times
that one maintainer cannot reliably meet.

#### Stage 3: Repeatable Product Revenue - After Demand Is Proven

Evaluate:

- certified release subscriptions;
- extended-maintenance releases;
- managed hosting;
- enterprise fleet-management convenience;
- commercially supported connector packs;
- training and certification.

At this point, revenue should fund legal review, business formation, insurance, code signing, hosted
infrastructure, and additional maintainers.

### Contribution Policy

For a fully open-source project, use the Developer Certificate of Origin and require signed-off commits.
This records that contributors have the right to submit work under the project license without creating
a custom agreement.

The DCO does not give the maintainer a unilateral right to relicense contributions under proprietary
terms. If future dual licensing is a serious objective, decide that before accepting substantial
contributions and obtain advice on a contributor agreement. Otherwise, choose the open-source path and
avoid preserving a hypothetical relicensing option at the cost of present-day adoption.

### Low-Cost Business Decisions

Make these only when triggered:

- **Business entity:** before signing material customer contracts or when revenue/liability justifies it.
- **Paid legal review:** before selling a proprietary license, promising an SLA, processing sensitive
  hosted data, or accepting a major enterprise contract.
- **Code-signing certificate:** when customer policy or download warnings materially block adoption.
- **Insurance:** when contracts, hosting, or revenue create meaningful liability.
- **Licensing server:** avoid unless license abuse creates a demonstrated business problem.
- **Hosted service:** only after self-hosted operations are stable and recurring demand exists.

### Sustainability Decision Gate

Before the 1.0 release branch, choose:

1. full Apache-2.0 open source with services/support revenue;
2. carefully separated open core; or
3. continued PolyForm source-available commercial licensing.

The current recommendation is option 1. It has the lowest legal, engineering, administrative, and
adoption burden for one maintainer. Revisit open core only after actual users request paid operational
capabilities and demonstrate willingness to fund them.

### Reference Material

- [Open Source Initiative FAQ](https://opensource.org/faq): commercial use, open-source business models,
  contributor agreements, and the distinction between source availability and open source.
- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0): official license text and patent
  grant.
- [Developer Certificate of Origin](https://developercertificate.org/): contributor sign-off text.
- [GitHub Sponsors](https://docs.github.com/en/sponsors/getting-started-with-github-sponsors/about-github-sponsors):
  individual and organization sponsorships.
- [Open Collective](https://opencollective.com/how-it-works): optional fiscal hosting and transparent
  project funding.

---

## v0.12.0 - Practical HA

### Goal

Allow the product to begin as a simple SQLite installation and grow into a supported multi-node
deployment without reinstalling or hand-recreating Portal and Orchestrator state.

### Supported Deployment Levels

**Level 1: Standalone**

- One active Portal and one Orchestrator service.
- SQLite control state.
- Local artifact storage.
- Existing exclusive instance/storage-root protection remains.
- Supervisor-based restart plus tested backup/restore provides practical single-server recovery.

**Level 2: Distributed**

- Two or more Portal nodes and two or more Orchestrator nodes behind a load balancer.
- PostgreSQL as the shared control database.
- SMB/UNC shared artifact storage under a dedicated service identity.
- Shared Data Protection keys and compatible JWT key configuration.
- Load-balancer affinity for interactive sessions in the first release.

PostgreSQL is the only distributed database certified in v0.12.0. SQL Server is a later provider
extension. Local and SMB/UNC storage are the only storage models certified in this release.

### Phase 1: Durable State Boundaries

- Inventory Portal and Orchestrator control state and assign one owning component for each record.
- Introduce provider-neutral stores for jobs, executions, leases, cancellation, delivery state, and
  system events.
- Use one logical deployment control database. Separate schemas or table prefixes may preserve component
  ownership, but operators should not manage unrelated Portal and Orchestrator databases.
- Add configured database-provider selection and startup validation.
- Preserve SQLite behavior while removing assumptions that leak SQLite-specific SQL into shared
  contracts.
- Add PostgreSQL EF Core migrations and provider-specific implementations only where semantics differ.

### Phase 2: Application-Owned Migration

Add a command such as:

```text
etl-sql admin migrate-database --from sqlite --to postgres --dry-run
```

The migration workflow must:

- verify source version, target connectivity, free space, required keys, and maintenance mode;
- create the target schema through versioned application migrations;
- copy records in dependency order while preserving stable IDs and security relationships;
- validate row counts, foreign keys, hashes, key versions, and migration history;
- write a resumable checkpoint without permitting both databases to become active;
- produce an artifact-storage migration plan;
- perform an explicit cutover and retain a documented rollback boundary.

Administrators must not write provider-specific DDL or data-copy scripts.

### Phase 3: Artifact Storage Boundary

Introduce an artifact-focused storage contract for:

- report scripts and immutable bundles;
- snapshots and exports;
- cached datasets;
- subscription trigger artifacts;
- shared Data Protection keys.

The first implementations are local filesystem and SMB/UNC. Required operations are staged write,
atomic publish/replace, read, existence, metadata/version check, list, delete, and temporary cleanup.
The boundary must preserve path traversal checks and script immutability rules.

Do not place SQLite databases on SMB or NFS. Do not emulate object storage in this release.

### Phase 4: Distributed Coordination

Add database-backed:

- node identity and heartbeat;
- execution and scheduled-job leases;
- monotonically increasing fencing tokens;
- atomic due-job and refresh claims;
- durable cancellation and execution status;
- leader leases for migration and singleton maintenance;
- a small system-event/outbox table for cache and security-state invalidation.

Lease-protected publication and completion must verify the current fencing token. A paused or partitioned
former owner must not publish output after another node takes over.

Keep global execution limits simple in v0.12.0. Per-user fairness remains an active-baseline concern, but
do not create a general distributed quota platform unless evidence requires it.

### Phase 5: Stateless and Recoverable Nodes

- Keep authentication, catalog reads, job polling, cancellation, and completed snapshots available from
  any Portal node.
- Use load-balancer affinity for active interactive sessions.
- Document that an interactive session may be rebuilt after node loss; durable job and snapshot state
  must not be lost.
- A node that cannot reach required database or storage dependencies becomes unready.
- A node losing a lease cancels local work and cannot mark it complete.
- Database or storage partitions fail closed rather than falling back to process-local ownership.

### Phase 6: Operations

Add application-owned commands or equivalent supported workflows:

```text
etl-sql admin doctor
etl-sql admin backup
etl-sql admin restore --validate
etl-sql admin migrate-database
```

Define:

- coordinated backup and restore for database, artifacts, configuration, and keys;
- node drain and graceful shutdown;
- schema version readiness;
- one migration leader;
- expand/migrate/contract migration rules;
- mixed-version compatibility windows;
- rollback by restore when down-migration is unsafe;
- node, lease, queue, takeover, database, and storage metrics.

### Certification Gates

Do not claim distributed HA until automated tests prove:

- clean creation, upgrade, backup/restore, and SQLite-to-PostgreSQL migration;
- two Portal and two Orchestrator processes operate against PostgreSQL and shared storage;
- simultaneous due-job and refresh claims produce one active owner;
- process kill, lease expiry, and owner loss recover without stuck work;
- stale owners are fenced from publishing or completing;
- any Portal node can poll, cancel, and serve completed durable results;
- security-state invalidation reaches every node within a defined interval;
- temporary database and shared-storage outages follow documented recovery behavior;
- rolling N-to-N+1 deployment preserves jobs, permissions, subscriptions, datasets, and audit history.

### Explicit Deferrals

- Microsoft SQL Server as an application-state provider
- S3-compatible artifact storage
- Redis or another external coordination service
- fully shared interactive session state
- tenant columns and shared multitenancy
- database row-level security

---

## v0.13.0 - Governance Core

### Goal

Give a small enterprise team one enforceable policy and audit model across CLI, IDE, Portal,
ReportPlayer, and Orchestrator without requiring every script to carry a shared password.

Governance must work on both standalone SQLite and distributed PostgreSQL deployments.

### Trust Boundary

Support:

- **Standalone mode:** local configuration and audit for individually managed installations.
- **Managed mode:** organization policy and remote audit endpoints are selected through protected machine
  or service configuration.

The product can prevent ordinary users from weakening policy when they do not control the machine.
It cannot guarantee enforcement against a local administrator/root user who can replace binaries,
configuration, and trust stores. Stronger assurance requires managed workstations or execution on
centrally controlled servers.

### Phase 1: Typed Policy Registry

Create one registry of governable engine and host settings with:

- normalized identifier;
- value type and default;
- valid values or range;
- security sensitivity;
- supported host scopes;
- whether policy may lock or constrain it.

Policy decisions are:

- **Allowed:** scripts may set any otherwise valid value.
- **Constrained:** scripts may choose only values within policy bounds.
- **Locked:** the organization value applies and scripts cannot override it.
- **Forbidden:** the capability or statement is rejected.

Enforce policy against parsed statements and normalized configuration, not text blacklists. Run the same
evaluation during analysis for feedback and again at runtime as the security boundary.

### Phase 2: Versioned Organization Policy

Use one versioned organization policy document with deterministic precedence:

1. built-in zero-trust rules cannot be weakened;
2. organization policy may tighten them;
3. scripts may choose only values allowed by the resulting policy.

The policy initially controls:

- security-sensitive `SET` statements and operation limits;
- allowed connector types and network destinations;
- approved filesystem roots;
- remote `EXECUTE` capability;
- script hash-pinning requirements;
- required secret-reference providers;
- required audit destination and failure behavior.

Managed nodes retrieve policy from local OS-protected configuration or an authenticated HTTPS endpoint.
Cache the last valid version for a bounded offline period. Invalid, incompatible, expired, or revoked
policy must never fall back to unrestricted mode.

Do not add a tenant hierarchy or full PKI signing in this release.

### Phase 3: Named Secret References

Introduce an `ISecretProvider` boundary and a script/configuration form that stores a logical secret name,
not its value.

Initial providers:

- environment or existing secure configuration;
- OS-protected local secret file/configuration;
- one generic authenticated HTTPS provider.

Policy limits which provider and namespace an execution identity may use. Secret values must not appear
in diagnostics, logs, audit payloads, command history, exports, generated bootstrap scripts, or retained
caches. Vendor-specific Vault/KMS adapters remain deferred.

### Phase 4: Durable Remote Audit

Extend the transactional audit baseline with a durable delivery outbox. Record:

- stable event and correlation IDs;
- actor, service, and node identity;
- operation and execution identity;
- artifact hash and policy version;
- decision and applicable policy rule;
- sanitized result and delivery status.

Implement one remote transport first: authenticated HTTPS with batching, retry, acknowledgement, and
event-ID deduplication. Add a transport interface, but do not implement syslog, OTLP, and proprietary SIEM
clients simultaneously.

Define backlog limits, retention, alerts, and whether selected governed operations fail closed when audit
delivery remains unavailable. A mutable local log alone is not tamper-proof.

### Phase 5: Administration and Recovery

Provide script-first and API workflows to:

- validate and preview a policy;
- activate a new version;
- inspect active versions by node;
- roll back to the previous valid version;
- export policy and audit-delivery status;
- issue a narrow, attributed, expiring exception that cannot disable built-in guardrails.

### Certification Gates

- Every host produces the same decision for the same policy and script.
- Locked and forbidden settings cannot be bypassed through aliases, casing, included scripts, queued
  work, alternate APIs, or direct runtime invocation.
- Hash-pinning policy is enforced at publication, scheduling, and execution.
- Secrets do not appear in scripts, exports, diagnostics, audit payloads, logs, or crash output.
- SQLite and PostgreSQL deployments both enforce and report policy correctly.
- Multiple nodes activate and revoke policy within a defined interval.
- Policy endpoint and audit collector outages follow documented offline and fail-closed behavior.
- Process crash and delivery retry preserve committed audit events with bounded duplicates.
- Rolling upgrades reject incompatible policy before execution.

### Explicit Deferrals

- script publisher certificates and PKI signing
- hash-chained or signed audit batches
- tenant policy overlays
- vendor-specific Vault, AWS KMS, Azure Key Vault, or HSM integrations
- multiple remote-audit transports in the first release

---

## v0.14.0 - Enterprise Identity and Authorization

### Goal

Integrate with enterprise identity while keeping authorization understandable for a small operations team.

### Scope

- Reconcile existing OIDC configuration/documentation with the runtime and implement a certified OIDC
  login flow.
- Use the identity provider for MFA, conditional access, password recovery, and primary account
  lifecycle. Do not build a separate MFA system.
- Map stable identity-provider group or role claims to Portal groups.
- Define just-in-time provisioning, account linking, claim-change behavior, and disabled-user handling.
- Add service accounts for non-interactive jobs and API clients with explicit scopes and rotation.
- Add direct user grants only where group-only access is operationally insufficient.
- Add effective-permission preview for administrators.
- Add optional approval for an intentionally small initial set of operations: report publication and
  scheduled-job activation or modification.
- Reauthorize queued and scheduled work when identity, group, account, or permission state changes.

SAML, explicit deny, deeply nested inheritance, and a general workflow engine are deferred.

### Certification Gates

- OIDC login, logout, token expiry, claim refresh, group change, and account disable are tested against a
  representative provider.
- MFA and conditional-access challenges are correctly delegated to the provider.
- Service accounts cannot use interactive-only flows or exceed their assigned scope.
- Effective-permission preview matches runtime authorization.
- Permission and identity changes invalidate access across all nodes within a defined interval.
- An approver cannot approve their own gated change, and rejected/expired proposals never become active.
- Identity and approval events are included in centralized audit when governance is enabled.

---

## v0.15.0 - Department Isolation

### Goal

Let one small enterprise team operate strongly isolated departmental environments without accepting the
security and maintenance cost of shared-table multitenancy.

### Deployment Model

Each department receives:

- its own SQLite or PostgreSQL control database;
- its own artifact root;
- its own service identity, secrets, keys, and identity-group mappings;
- its own Portal and Orchestrator configuration;
- independent backup, restore, retention, and upgrade scheduling.

The same binaries and deployment templates are reused. PostgreSQL installations may use separate
databases or schemas only when the chosen isolation and backup model remains operationally clear.

### Scope

- Provide repeatable deployment templates for Windows services, systemd, and the supported container
  topology.
- Add a deployment/environment identifier to health, audit, backup, and diagnostic output.
- Add export/import tooling for moving reports, jobs, configuration, and portable content between
  environments without copying secrets.
- Add central inventory or health aggregation that does not merge departmental authorization or data.
- Support centralized governance policy with environment-specific tightening.
- Document department creation, suspension, migration, restore, offboarding, and secret rotation.
- Prove that one department's service identity cannot read another department's database, artifact root,
  keys, or audit queue.

This release intentionally uses deployment isolation rather than `TenantId` columns and tenant switching.

### Certification Gates

- Two departmental deployments can use duplicate logical names without conflict.
- Backup, restore, upgrade, and migration operate independently.
- Export/import does not copy credentials, tokens, password hashes, or non-portable encrypted content.
- Central health and audit collection cannot grant cross-department data access.
- Failure, overload, key rotation, or maintenance in one department does not corrupt another.
- Deployment templates can be operated by a small team without custom database scripts.

---

## Demand-Driven Extensions

The following are valid future directions, but they are not scheduled commitments.

### Microsoft SQL Server Application State

Add only after PostgreSQL HA is stable and customer demand justifies another provider-specific migration,
locking, concurrency, backup, failover, and upgrade certification matrix.

### S3-Compatible Artifact Storage

Add an object-store implementation of the artifact contract only when cloud or cross-platform deployments
require it. Preserve object-store semantics rather than presenting it as a normal filesystem.

### Shared Multitenancy

Consider only when dedicated departmental deployments are operationally insufficient. It requires tenant
identity in every durable row, key, cache, job, lease, audit event, artifact, API, and administrative
operation, plus structural cross-tenant isolation tests. This is a separate major project, not an
incremental flag.

### Advanced Key Management

Certificate signing, KMS/vault envelope encryption, per-environment keys, and HSM integration are useful
for regulated deployments. Add provider contracts first and implement a vendor adapter only with a real
deployment requirement.

### Additional Identity and Authorization

SAML, explicit deny, nested-group expansion, inherited ACLs, and broader four-eyes workflows should be
added only when OIDC, group mapping, direct grants, and the initial approval scope no longer meet actual
customer needs.

### Fully Shared Interactive Sessions

Replace load-balancer affinity only if session-loss behavior becomes a material operational problem.
Durable execution and completed snapshots must remain node-independent regardless.

---

## Moving a Release into TODO.md

When a release becomes active:

1. Reconcile the roadmap against current source and documentation.
2. Move only the first incomplete phase into `TODO.md`.
3. Break the phase into independently testable P1/P2 tasks with named owners and acceptance tests.
4. Keep explicit deferrals in this document.
5. Promote the next phase only after the previous phase's tests and migration path pass.
6. Update the release capability matrix before making a public support claim.
