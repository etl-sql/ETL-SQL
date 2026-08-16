# ETL-SQL Product Roadmap

This document tracks high-level product tracks and candidate phases. Their actionable work is
decomposed in `TODO.md`. Once an initiative is verified, record its notable outcome in
`CHANGELOG.md` and retire completed TODO and roadmap entries that no longer describe future work.
Release-specific detail belongs in the release notes under `docs/releases/`.

The stable deployment-profile topology, provider, binding, state, and authority decisions are defined
in [`docs/architecture/DeploymentProfiles.md`](docs/architecture/DeploymentProfiles.md). The
Enterprise operating model and trust hierarchy are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

### Platform — Deployment Profiles and Upgrade Certification

Build the profile, portability, and certification program defined in
[`Deployment_Profile_Strategy.md`](docs/architecture/roadmaps/Deployment_Profile_Strategy.md) within
the stable boundaries defined by
[`DeploymentProfiles.md`](docs/architecture/DeploymentProfiles.md).
Treat **Solo / Workstation**, **Team / SME**, **Enterprise / Corporate**, and
**SaaS / Multi-Organization** as cumulative support profiles rather than editions.

#### Delivery sequence — one product, progressively stronger envelopes

The four profiles are architecture destinations, not four parallel products or simultaneous release
commitments. ETL-SQL has one parser, evaluator, connector contract, artifact model, checkpoint format,
and promotion model. Profiles replace hosting providers and add authority; they do not fork language
semantics or require profile-specific versions of a script.

| Stage | Product role | Delivery boundary |
| :--- | :--- | :--- |
| **Solo / Workstation** | Semantic reference and lowest-friction adoption | In-process execution, local artifacts/state, one trusted operator |
| **Team / SME** | Lightweight shared configuration, not a separate architecture | Single-node Portal/Orchestrator and durable shared providers where needed |
| **Enterprise / Corporate** | Operational and self-hosting foundation | External identity, scoped authorization, policy, secrets, audit, PostgreSQL/shared artifacts, HA, backup/restore, and promotion |
| **Managed Dedicated SaaS** | First hosted offering | Automated tenant-specific Enterprise-style deployment with a dedicated database/artifact/key/queue boundary and hypervisor or dedicated-worker isolation |
| **Shared SaaS** | Later density and fleet-economics phase | Shared tenant-aware control planes, Gateway Broker, hardened per-run sandboxes, fair scheduling, metering, and hostile cross-tenant certification |

Managed Dedicated and Shared are delivery topologies within the SaaS profile, not additional editions.
Dedicated topology evidence must never be presented as proof of shared control-plane or execution-plane
isolation. Team should remain a configuration of common providers; do not create Team-only language,
catalog, UI, or execution implementations.

The portability promise is intentionally precise: the same pipeline/report business logic moves
between profiles without rewriting, while each target supplies compatible governed bindings,
identities, resources, secrets, policy, capacity, and connector availability. Promotion preflight
must explain an unavailable binding or prohibited operation before activation rather than weakening
target policy to make a script appear portable.

#### Delivery gates

1. Establish and certify Enterprise identity, authorization, state, artifact, secret, policy, audit,
   recovery, and upgrade boundaries before building a separate shared-SaaS implementation of them.
2. Ship the first tenant portability/export contract before Managed Dedicated SaaS is generally
   available, including a proven SaaS → self-hosted Enterprise exit path.
3. Exercise tenant provisioning, administration, Gateway connectivity, upgrades, metering, support,
   export, and deletion against Managed Dedicated SaaS before sharing control or execution planes.
4. Introduce shared services only where demand and fleet economics justify their additional security
   and operational cost. Each shared boundary remains Red until hostile cross-tenant evidence passes.

#### P2 — Add deployment-profile certification

1. Retain commit-bound JSON and Markdown evidence under `certification-results/` with topology,
   artifact hashes, mapping decisions, continuity counts, negative isolation results, and
   rollback/restore outcomes.
2. Add the profile/transition matrix to release claims. A capability is not certified for every
   deployment merely because its Solo or Enterprise test passes; each applicable profile and
   transition needs its own current evidence.

**Definition of done.** A user can start with source-controlled artifacts on one workstation,
promote them to a shared team service, add corporate identity/policy/audit/HA, or onboard them
directly or progressively into Managed Dedicated and then Shared SaaS without rewriting pipeline or
report business logic.
Every supported profile passes N → N+1, every promotion path preserves and reconciles its declared
portable state, and each SaaS topology proves its own tenant isolation rather than inheriting a
claim from configuration or a weaker/different topology.

### Orchestrator — Per-Object Authorization

**Origin (2026-07-27).** Surfaced while designing the unified job/schedule/notification model
([job_schedule_notification.md](docs/architecture/decisions/job_schedule_notification.md)). Making the
Orchestrator the system of record for `JOB`, `SCHEDULE`, and `NOTIFICATION` moved durable, mutable,
operationally significant objects into a store whose API authenticated with a **single shared key**
(`X-Orchestrator-Key`) and had no user or group model at all.

The consequence at the time: anyone who could reach the orchestrator connection could create, alter,
disable, or drop **anyone's** job. The only boundary was the use-ACL on the orchestrator connection in
the Portal's governed catalog, which is connection-level, not per-object — a real asymmetry with the
Portal's per-object RBAC, accepted then as a deliberate deferral rather than an oversight.

**Largely delivered (verified 2026-08-15).** The anticipated trigger — a second client — arrived,
and the four numbered work items were built rather than deferred. Shipped: federated identity
(`OrchestratorIdentityAssertion`, HMAC and audience-bound, with the caller-controlled
`X-Orchestrator-Actor` header retired); per-object ACLs over `JOB`/`SCHEDULE`/`NOTIFICATION` reusing
the Portal's `READ`/`EXECUTE`/`OVERRIDE`/`MANAGE` vocabulary for user, group, and service principals;
ownership checks that stop `CREATE OR ALTER` silently taking over a shared name in both the HTTP and
engine paths; and audit events naming the acting principal. The definition-of-done scenarios are
proved by `OrchestratorPerObjectAuthorizationIntegrationTests` and gated in release certification as
the `per-object-authorization` hosted prerequisite.

**Remaining scope, decomposed in [TODO.md](TODO.md).** Four gaps keep the item open. A script-side
`ORCHESTRATOR` connection still authenticates with the shared key alone, so it is either refused by a
federated Orchestrator or granted blanket authority by a legacy one. Grants have no administration
surface, so setting one means hand-crafting a signed assertion. There is no ownership transfer or
adoption path, which a solo deployment needs the moment it attaches a Portal. And grants key on
mutable numeric Portal identifiers rather than stable ones.

**The control-plane decision (2026-08-15).** Identity federates to the **Portal**, which is the single
place users, groups, and audit live. The Orchestrator does not grow its own principal registry: that
would be the second permission model this item exists to prevent, and it would have no path to
Active Directory, whereas Portal groups already synchronise from OIDC/AD group claims on every login.
Team and above therefore require a Portal; Solo may run without one and has no principals or grants at
all. Clients exchange a Portal credential for a short-lived Orchestrator assertion and then call the
Orchestrator directly, which keeps the two tokens' audiences separate and avoids a Portal proxy twin
for every orchestrator endpoint.

**Definition of done.** A user who can reach an orchestrator cannot mutate a job they do not own, the
Orchestrator's audit records name a person rather than a service, and the permission vocabulary is
the Portal's rather than a second one.

### Orchestrator — Job Administration UI

**Origin (2026-08-15).** Split out of Per-Object Authorization so the security boundary is not held
hostage to a much larger UI build. The Portal's Orchestrator tab is already a working operations
surface — status, stat filters, triage, a 24-hour timeline, the jobs table with run/enable/kill/delete,
a detail panel with script flow and inline editing, run history, and resume-from-checkpoint. What it
does not cover is everything added since it was written: the unified `SCHEDULE` and `NOTIFICATION`
catalog, job metrics and data-quality results, bundle deployment, sandbox profiles, and watermark
state.

The intent is a SQL Agent-class administration surface for operators who would rather not script a
change by hand, without displacing scripting as the primary authoring path. Decomposed in
[TODO.md](TODO.md).

### SaaS Multi-Tenancy — Secure Outbound Data Gateway

**Authoritative design:**
[`SaaSTenantIsolation.md`](docs/architecture/SaaSTenantIsolation.md#11-secure-outbound-data-gateway),
which owns the durable Gateway, resource-mapping, authority, protocol, and administration decisions.
**Delivery is tracked in [`TODO.md`](TODO.md)** under Progressive SaaS Delivery, domain 6 — Network
egress and the Gateway.

The SaaS service must reach private databases, file shares, and APIs without inbound firewall
exceptions, a general-purpose network tunnel, or any possibility that one tenant can address another
tenant's gateway or resources. The gateway is an outbound-connected, tenant-attested policy
enforcement point — not a SOCKS proxy, VPN, or remotely configurable host/port relay. Scripts name a
governed `SHARED:` alias only; the tenant catalog resolves it to a gateway and a registered resource,
and neither the physical endpoint nor the credential ever reaches the execution container.

**Delivery stage.** Tenant-admin enrollment, the resource catalog, gateway-local enforcement, and the
typed protocol ship first against Managed Dedicated SaaS. The shared Gateway Broker registry and
routing plane is Shared SaaS only: dedicated-topology success does not prove that shared broker
queues, sessions, buffers, caches, or support operations are tenant-safe.

**Certification gate.** Cross-tenant requests fail even when the caller knows another tenant's alias,
gateway, resource, operation, or broker endpoint; capabilities fail when expired, replayed, altered,
or presented after revocation; a compromised execution container cannot select an arbitrary
destination or obtain a general tunnel; and a tenant administrator can complete the whole
enrollment-to-revocation workflow without SaaS-administrator intervention, while SaaS administrators
cannot perform those tenant-owned actions. The linked design's §11 holds the full definition of done.

### SaaS Multi-Tenancy — Isolated Execution Data Plane (OCI + Hardened Sandboxes)

**Authoritative design:** [`SaaSTenantIsolation.md`](docs/architecture/SaaSTenantIsolation.md), which
owns tenant context, execution isolation, storage, checkpoint, Gateway, capacity, observability, and
support boundaries. **Delivery is tracked in [`TODO.md`](TODO.md)** under Progressive SaaS Delivery,
domains 4 (storage), 5 (scheduling, execution, capacity), and 8 (observability).

ETL-SQL must execute mutually untrusted tenant scripts without letting code, data, credentials,
resource consumption, or residual state cross tenant boundaries. OCI containers are the portable
workload package, but an ordinary shared-kernel container is not by itself a sufficient boundary for
hostile multi-tenancy: SaaS executions run in an ephemeral hypervisor-backed or equivalently hardened
sandbox. The four isolation tiers — Local, Standard, Hardened, Dedicated — are defined in
[`DeploymentProfiles.md`](docs/architecture/DeploymentProfiles.md#101-isolation-tiers). The workload
contract is identical across them, so moving to a stronger tier changes placement and cost, never
`.etlsql`/`.rptsql` syntax, connection aliases, or execution semantics.

Disposable sandboxes must preserve named-checkpoint recovery without preserving a failed process: the
engine serializes resumable logical state outside the sandbox at each completed top-level label, and
a replacement sandbox rehydrates it. It never resumes at an arbitrary statement index.

**Delivery stage.** Managed Dedicated may use a tenant-dedicated VM, worker pool, or cluster as the
hypervisor boundary, with disposable OCI tasks per run inside it. Shared SaaS requires the Hardened
per-run boundary, the provider-neutral Execution Scheduler, and shared-fleet negative certification.
The production scheduler now has an opt-in content-addressed Docker provider binding that accepts
only registered gVisor/Kata runtimes and digest-pinned images, with fixed tenant/pool placement for
Dedicated workers and fenced teardown reconciliation. Its live hardened-runtime certification and
cluster-global Shared queued-work recovery remain delivery gates; an ordinary local `runc` runtime
does not satisfy them.

**Certification gate.** Cross-tenant attempts fail across scheduling, workload identity, network,
storage, checkpoints, spill, caches, queues, secrets, connections, gateways, logs, metrics, and
support operations; a compromised sandbox reaches neither the host, runtime socket, metadata service,
control plane, another sandbox, nor broader tenant storage; warm-pool reassignment leaves no residue;
one tenant's exhaustion cannot violate another's reserved capacity; and a failed persistent job
resumes from its last valid named checkpoint in a different sandbox, while altered scripts, revoked
authority, cross-tenant IDs, and incompatible versions fail closed.

### SaaS Multi-Tenancy — Tenant Portability & Migration (Export/Import)

**Authoritative design:** [`TenantPortability.md`](docs/architecture/TenantPortability.md), which owns
the bundle, classification, rebinding, import, cutover, rollback, deletion, and customer-exit
contracts. **Delivery is tracked in [`TODO.md`](TODO.md)** under Progressive SaaS Delivery, domain 9 —
Lifecycle.

Customers must be able to enter or leave ETL-SQL SaaS without rewriting pipeline/report logic or
depending on provider-owned infrastructure. The guarantee is full-fidelity migration of portable
customer-owned artifacts and eligible tenant metadata, with explicit rebinding of environment-owned
identities, resources, secrets, keys, and infrastructure. It is deliberately not "zero-loss":
secrets, ephemeral security material, active sessions, leases, caches, and in-flight operations are
not transferable as durable tenant ownership, and saying so plainly is more defensible than a claim
the product cannot keep.

**Delivered minimum (2026-08-13).** The configuration/artifact v1 bundle and the customer-held-key
Managed Dedicated SaaS → self-hosted Enterprise journey are shipped and covered by the portability
test lane. The remaining certification below applies to broader migration operations.

**Future delivery stage.** Large evidence/content exports, incremental deltas, cross-provider scale
optimization, and Shared-source isolation mature without weakening the initial exit guarantee.
One unified bundle continues to extend the existing Portal configuration export and Orchestrator
promotion package; do not introduce a competing packaging model or represent the bundle as an opaque
database backup.

**Certification gate.** A representative tenant moves SaaS cluster A → SaaS cluster B and SaaS →
self-hosted Enterprise without changing business logic; export under concurrent activity produces a
declared consistency point and cutover creates no duplicate schedules; every eligible resource
reconciles by stable ID, count, hash, ownership, and ACL, with every exclusion visible and justified;
secret scanners prove no credential, key, capability, or other tenant's record enters the package;
tampered, replayed, cross-tenant, or oversized packages fail before target activation; and a customer
can validate and retain the export with published tooling and customer-held keys after source SaaS
access is gone.

### SaaS Multi-Tenancy — Control Plane Dashboard (Platform Admin UI)

**Candidate, not scheduled.** As the SaaS offering matures beyond initial CLI-driven onboarding and configuration, tier-1/tier-2 support and platform operations will require a dedicated visual dashboard for fleet observability, tenant lifecycle management, and resource monitoring.

**Design principles and boundaries:**

- **Physical separation from the Portal:** The SaaS Admin UI must be a completely separate application (or physically separate endpoint) from the customer-facing `ETL-SQL.Portal`. Adding a "Super Admin" tab to the existing Portal introduces the hazard of context bleed, where a platform admin might bypass tenant scopes or leak data across the deeply baked `TenantContext`.
- **Identity isolation:** It must enforce the Platform Identity Separation contract (`PlatformAccessGrant`). Platform administrators operate under a distinct identity model that cannot mint tenant sessions or assume "God Mode" within a tenant's data space.
- **Observability over mutation:** The primary goal of the UI is situational awareness—monitoring shared worker capacity, tracking Gateway Broker health, viewing tenant execution quotas, and identifying head-of-line blocking in queues. Declarative mutations (like tenant provisioning) should remain heavily CLI/API driven.

**Delivery stage.** This is a Phase 2 maturity goal. The current CLI-first approach (`admin promotion saas-onboard`, `admin tenant preflight`) correctly forces robust API design and scripting automation. The Control Plane Dashboard will be scheduled when the Shared SaaS topology reaches sufficient density to make CLI-based fleet health checks unscalable.

**Certification gate.** The Control Plane UI authenticates only platform principals (never tenant users); it physically cannot render a tenant's `.etlsql` scripts, report artifacts, or `SHARED:` gateway credentials; its telemetry scopes aggregate usage across the fleet without retrieving tenant-owned raw data; and any tenant lifecycle mutations it performs generate an attributed platform audit receipt.

### Language — Compound `@expect` Rules (AND / OR)

**Candidate, not scheduled.** A comma between `@expect` rules means AND, and there is no OR: a
disjunction must be written as `EXPR a = 'A' OR a = 'B'`, which leaves the rule vocabulary for a
raw predicate and gives up the diagnostics, autocomplete and policy legibility the named rules
exist to provide. The parenthesised mixture — `NOT NULL AND (LENGTH BETWEEN 5 AND 10 OR MATCHES
^LEGACY-)` — cannot be expressed at all.

**The numbered variants do not cover this.** `@expect_1`, `@expect_2` are independent bindings that
must *each* pass, so they compose as AND with a distinct `@fail` action per group. They solve
"different consequences for different rules", not "either of these is acceptable".

Design notes, recorded so the shape is not rediscovered when this is picked up:

- **Precedence is ordinary SQL**, which is why `BETWEEN … AND …` needs no special case: its bound
  parses at a level above the `AND` connective, so `BETWEEN 1 AND 10 AND NOT BLANK` resolves as it
  would in a `WHERE` clause. Parentheses group. This retires `FindTopLevelAnd`, which takes the
  first depth-zero `AND` and holds only while `AND` cannot also be a connective.
- **Keep the comma** as a synonym for top-level AND; it is used throughout the samples and
  reference pages, so removing it would break working scripts for no gain.
- **The existing NULL rule already fixes the semantics.** "NULL skips every rule except NOT NULL,
  and the row passes" is three-valued logic with UNKNOWN treated as passing — the SQL `CHECK`
  convention the reference page cites. SQL's truth tables therefore apply unchanged, and a pure-AND
  list keeps today's behaviour, which is the backward-compatibility argument.
- **Failure reporting is the hard part, not the grammar.** Evaluation records a failure per failing
  rule, and `__dq_rule`, the capped sample values, the per-rule counts on job history and
  `eng.data_quality_rules` all assume a flat list of independent rules. Under an `OR` a false term
  is not a failure — only the whole expression is. Settle what `__dq_rule` carries for a compound
  expression before writing the parser, or the metrics surface will report sub-terms as failures
  that never failed a row.
- Language change, so the **Syntax Addition Checklist** in `CONTRIBUTING.md` applies.

### Platform — Native Object Storage for HA Artifacts

**Candidate, not scheduled.** Current High Availability (HA) deployments rely on shared network file systems (SMB/UNC) for Portal and Orchestrator artifact roots. As the SaaS offering scales, SMB becomes a significant latency bottleneck and a single point of failure (SPOF) due to file-locking contention.

**Delivery stage.** This work introduces native S3 / Azure Blob Storage provider bindings for the Artifact store. It replaces the reliance on durable POSIX file semantics in HA and SaaS environments, ensuring execution checkpoints, datasets, and script histories can scale horizontally without network filesystem lock contention.

### Identity — Service Accounts and M2M API Workflows

**Candidate, not scheduled.** While Enterprise OIDC identity is actively maturing for human users, automated deployments (CI/CD) and integration workflows currently lack hardened Machine-to-Machine (M2M) capabilities.

**Delivery stage.** This introduces formal Service Accounts, long-lived but tightly scoped API tokens, and approval workflows. It enables headless systems to securely publish `.rptsql` artifacts or trigger Orchestrator jobs without assuming a human identity or relying on legacy basic-auth patterns.

### Execution — Lean Worker Profiles and Binary Trimming

**Candidate, not scheduled.** The unified `ETL-SQL.exe` binary provides an excellent developer experience (DX) by bundling the Portal, Admin CLI, and Orchestrator into one drop-in executable. However, loading the entire DI graph and assemblies for these control-plane features adds unnecessary memory footprint and cold-start latency to ephemeral sandboxes.

**Delivery stage.** This work leverages .NET trimming and feature flags to produce a dedicated `ETL-SQL-Engine` binary artifact. It strips out all administrative, portal, and orchestration-hosting code, leaving only the pure script evaluator and connectors. This minimizes the compute cost and attack surface inside Shared SaaS OCI sandboxes.

### SaaS Testing — API Load and Soak Testing

**Candidate, not scheduled.** Current UI, SLT, and fuzzy testing validate functional correctness but do not stress the concurrency mechanisms required for SaaS density.

**Delivery stage.** This work introduces formal load-testing pipelines (e.g., k6 or JMeter) targeting the Orchestrator and Portal APIs. It will simulate hundreds of concurrent workflows to expose connection pool exhaustion, memory leaks, and head-of-line blocking in the Shared SaaS infrastructure under sustained load.

### SaaS Testing — Chaos Engineering (Fault Injection)

**Candidate, not scheduled.** Shared SaaS High Availability relies on the Orchestrator to handle node and network failures gracefully, but this is difficult to prove without deliberately destructive testing.

**Delivery stage.** This phase integrates automated fault injection (e.g., Chaos Mesh) into the testing pipeline. It ensures the platform survives abrupt worker node reboots, dropped SMB packets, and database disconnects, proving that failed jobs correctly fence themselves and resume from the last valid named checkpoint.

### SaaS Testing — Synthetic Monitoring (Production Canaries)

**Candidate, not scheduled.** Internal staging environments cannot always replicate the specific friction points of production traffic and latency. 

**Delivery stage.** This initiative deploys a persistent "Canary Tenant" in the live production environment. The canary will execute a comprehensive synthetic end-to-end workflow at strict intervals. Any deviation in correctness or latency triggers proactive alerts to platform operations before real tenants experience degradation.

### Developer Experience & CI/CD — Native Unit Testing & Table Assertions

**Candidate, not scheduled.** While `SET WHAT_IF ON` provides pre-flight dry runs against real data sources and `ASSERT JOB` evaluates operational run metrics, testing discrete transformation logic against synthetic edge cases currently requires ad-hoc scripts or manual `EXCEPT` queries.

**Delivery stage.** This work introduces native unit testing primitives for ETL-SQL:
- **Table Comparison Assertions:** Native syntax (`ASSERT TABLE #actual MATCHES #expected`) that compares two staging tables by schema and content, outputting detailed row/column diffs and mismatch summaries on failure.
- **Script Test Harness:** A dedicated CLI test runner (`etlsql test MyPipeline.test.etlsql`) that executes scripts in isolated test environments using `MOCKDB` and synthetic `#temp` tables, asserting deterministic output states for pull request validation and AI agent self-verification.

### Tooling & Authoring — Visual Report Builder Round-Trip Fidelity & Trivia Preservation

**Candidate, not scheduled.** The Visual Report Builder provides bi-directional synchronization between the 12-column visual grid and `.rptsql` scripts. To ensure zero loss of developer comments and formatting during visual editing, the authoring surface requires enhanced AST serialization controls.

**Delivery stage.** This phase hardens round-trip fidelity in VS Code, Portal Studio, and Report Player:
- **Surgical Statement Patching:** Modifying or moving a visual card replaces only the exact source character span of that specific `CREATE VISUAL` or `CREATE PAGE ... STRUCTURE` statement, leaving preceding data prep SQL, CTEs, and whitespace untouched.
- **Comment & Trivia Preservation:** The AST parser and serializer retain all leading/trailing comments (`-- ...`, `/* ... */`) and formatting trivia when writing back changes.
- **Fault-Tolerant Canvas State:** Syntax errors introduced during split-screen text editing trigger inline visual warning badges rather than destroying or resetting canvas layout state.

### Language & Execution — Declarative Incremental Watermarking Syntax

**Candidate, not scheduled.** ETL-SQL currently supports atomic, job-scoped incremental state through `GET_JOB_STATE` and `SET_JOB_STATE`. While functional and safe, setting up delta loads requires manual variable declaration and explicit update calls in the success path.

**Delivery stage.** This work introduces declarative syntax sugar for incremental ingestion:
- **Declarative Watermark Clause:** Direct support on source queries (e.g. `FROM src.Orders WITH (WATERMARK = 'OrderId', INITIAL = '0')`).
- **Engine-Managed State Lifecycle:** The engine automatically retrieves the last recorded high-water mark, applies the filtering predicate, and stages the maximum observed key value to atomically update `eng.job_state` upon successful script completion.

### Reporting & Presentation — Extensible Visual Runtimes (Data-Bound HTML/SVG & Vega-Lite)

**Candidate, not scheduled.** While Apache ECharts provides exceptional performance and comprehensive chart-type coverage for the vast majority of analytical workloads, modern reporting often demands specialized escape hatches for bespoke infographic KPI cards, micro-visuals, and complex layered statistical graphics without sacrificing the script-first, Zero-Trust execution model.

**Design principles and boundaries:**
- **Zero-Trust rendering:** Custom visual extensions must remain declarative and safe across CLI Player, VS Code, and Portal runtimes. Untrusted, arbitrary JavaScript execution is prohibited.
- **Diffable, script-first contracts:** Visual definitions remain standard `.rptsql` statements rather than compiled binary blobs or external web component dependencies.

**Delivery stages:**
- **Tier 2: Data-Bound HTML / SVG Visuals (`TYPE = HTML` / `TYPE = SVG`):**
  - Enables embedding sanitized HTML and SVG templates directly into `CREATE VISUAL` definitions bound to engine `#dataset` queries.
  - Solves the "Last 5%" presentation gap for multi-metric KPI cards, status indicators, badges, trend delta arrows, and inline sparkline grids using standard CSS and SVG markup.
  - Renders through an AST-based HTML sanitizer to guarantee script isolation and prevent DOM/XSS injection.
- **Tier 3: Declarative Grammar-of-Graphics (`TYPE = VEGALITE`):**
  - Integrates the permissive BSD-3-Clause **Vega-Lite** runtime as an alternative declarative visual compiler for specialized domain visualizations.
  - Allows embedding raw Vega-Lite JSON specifications (`SPEC = '{ ... }'`) directly within `.rptsql` scripts.
  - Supports composite marks, faceted distributions, error bands, and layered statistical charts while preserving deterministic client-side rendering and headless PDF/image export.

