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
Orchestrator the system of record for `JOB`, `SCHEDULE`, and `NOTIFICATION` moves durable, mutable,
operationally significant objects into a store whose API authenticates with a **single shared key**
(`X-Orchestrator-Key`). It has no user or group model at all.

The consequence: anyone who can reach the orchestrator connection can create, alter, disable, or drop
**anyone's** job. The only boundary is the use-ACL on the orchestrator connection in the Portal's
governed catalog, which is connection-level, not per-object. That is a real asymmetry with the
Portal, which enforces per-object RBAC — and it is a deliberate deferral, not an oversight.

**Why it is acceptable for now:** the Portal is the only client, and it authenticates as a single
principal. Per-object ACLs against one subject would be authorization theatre.

**What ships in v0.18.0 instead — attribution, not authorization.** The Portal passes the acting
user's identity through on every mutation, and the Orchestrator records `CreatedBy` / `ModifiedBy` on
the job, schedule, and notification rows. One column each, purely additive, no identity model
required. It makes "who scheduled this?" answerable — the question that will come up first — and it
makes a silent takeover (see below) visible after the fact.

**The trigger to build real authorization** is a second client, or one Orchestrator shared across
teams or tenants. At that point the Orchestrator needs an identity model, which realistically means
federating to the Portal's or directly to OIDC rather than inventing a third one. Sequence it with
the enterprise identity work in `docs/guides/administration.md`.

The work, when triggered:

1. **Federate identity** rather than duplicating it — the caller's identity arrives as a verifiable
   token, not a trusted header, which is the difference between authorization and attribution.
2. **Per-object ACLs** on `JOB`, `SCHEDULE`, `NOTIFICATION`, reusing the Portal's grant vocabulary so
   there is one permission model to reason about, not two.
3. **Ownership on the shared-name hazard.** Names are unique per orchestrator and `CREATE OR ALTER`
   is supported, so a second script importing an existing name silently takes the object over rather
   than erroring. Until ACLs exist this is mitigated socially — naming conventions, a category in
   `OPTIONS`, and the attribution columns above. Ownership makes it enforceable.
4. **Audit parity** with the Portal: every mutation attributable to a real principal, not to "the
   Portal".

**Definition of done.** A user who can reach an orchestrator cannot mutate a job they do not own, the
Orchestrator's audit records name a person rather than a service, and the permission vocabulary is
the Portal's rather than a second one.

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
