# ETL-SQL Product Roadmap

This document tracks high-level product tracks and candidate phases. Their actionable work is
decomposed in `TODO.md`. Once an initiative is verified, record its notable outcome in
`CHANGELOG.md` and mark its `TODO.md` entry complete without deleting it. Product-level roadmap
entries may be retired when they no longer describe future work. Release-specific detail belongs
in the release notes under `docs/releases/`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

### Platform — Deployment Profiles and Upgrade Certification

Build the profile, portability, and certification program defined in
[`Deployment_Profile_Strategy.md`](docs/architecture/roadmaps/Deployment_Profile_Strategy.md).
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

### Portal — Accessibility and visual-system completion

- Consolidate the remaining duplicated page headers and per-page focus-management code into the
  shared Portal shell and component vocabulary, with browser coverage for each migrated dialog.

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

### Portal — Quarantine Row Access

**Problem.** `DataQualityController.GetQuarantineRows` runs `SELECT * FROM {target}` inside a fresh
in-process `ExecutionSession`. That session is constructed with an empty connection dictionary and
never calls `Evaluator.LoadSessionState`, so it restores nothing from the producing run: no
connections, no temp tables, no session variables. Every real capture target therefore fails —
a connection-qualified target (`warehouse.dbo.quarantine_users`) raises `Unknown source: warehouse`,
and a `#temp` target is silently auto-created as an empty in-memory table, which is worse: the
steward reads "no rows" as "nothing was quarantined". Pre-projection capture plus in-Portal editing
is the strongest part of the remediation workflow, and it is unavailable exactly where quarantine
data actually lives.

The current queue marks these targets **View only**, explains why, and provides review SQL to run
where the connection exists. The remaining product gap is governed, in-Portal access to durable
catalog-backed targets.

**Chosen direction: catalog-backed preview.** Resolve the target through the shared connection
catalog rather than widening the Portal's reach generally.

| Option | Verdict |
| :--- | :--- |
| Rehydrate the producing job's `SessionState` into the preview session | Rejected — restores *every* connection an arbitrary job held, with no bound tied to the manifest, and the state may no longer exist. |
| Resolve the target's connection from the catalog as `SHARED:alias` | **Chosen** — governed path; flows through `SharedConnectionExpander` → `ConnectionSecretResolver` → `ConnectorPolicyAuthorizer`, so policy, secret resolution, and redaction all apply unchanged. |
| Round-trip the read through the orchestrator as a job and return its result set | Deferred fallback — covers ad-hoc script connections the catalog does not know, but needs a result-returning job path and turns an interactive read into an async one. |

Slices:

1. **Manifest provenance.** Add nullable `TargetConnectionAlias`, `TargetConnectorType`, and
   `TargetIsCatalogBacked` to `QuarantineReplayManifest`, written at capture time. Backward
   compatible in the same way the replay-mode fields were: absent means "unknown", which classifies
   as view-only.
2. **Readability consults the catalog.** `QuarantineTargetReadability` gains an
   `IConnectionCatalogProvider` and the caller's `ExecutionIdentity`, and reports readable only when
   the alias resolves, is enabled, and the caller is authorized for it. Every other case keeps its
   existing reason string, so the interim UI needs no change.
3. **Preview session bootstrap.** Prepend
   `CREATE CONNECTION {alias} AS {type}('SHARED:{alias}');` to the preview script. The alias comes
   from the manifest, never from the request, and the statement is still only
   `SELECT * FROM {manifest target}` — not arbitrary SQL. Keep the 15s timeout,
   `MAX_LAST_RESULT_ROWS`, the RLS execution identity, and `SecretRedactor` on the error path.
4. **Kill switch and audit.** Gate the whole path behind `Portal:DataQuality:AllowConnectionPreview`
   (default **off**, so an upgrade does not silently start opening production connections from the
   web tier), and audit each preview read the way dispositions are audited today — reading raw
   quarantined source rows is an access event, not a page view.
5. **Tests.** A **happy-path** read is the first requirement, not the last: every existing
   `quarantine/rows` test asserts a rejection, so the catalog-backed path needs positive coverage
   before it can be considered functional. Then: catalog miss, disabled entry, feature switch off,
   unauthorized identity, and a redaction assertion on the failure path.
6. **Docs + sandbox.** Administration guide: which connections become previewable, what the switch
   does, and what is audited. Flip the sandbox's view-only fixture to a readable catalog-backed
   target so both states stay developable
   (`tools/ui-sandbox/stories/data-quality-queue.story.js`).

Open decision for the sprint: whether a steward reviewing rows through a catalog connection should
be limited to connections their own role can already reach, or whether `DataQualityStewardAccess`
plus a manifest-bound target is authority enough. This changes slice 2's authorization check.

### Portal — Data Quality Follow-through

These lower-level data-quality findings support the comprehensive update above. Ordered by how much
each affects day-to-day use.

1. **Every preview spins a full engine.** Each request lexes, parses, lints, and evaluates through a
   new `ExecutionSession`. Acceptable at current volume; revisit before this becomes a polled or
   dashboard-refreshed surface.


### SaaS Multi-Tenancy — Secure Outbound Data Gateway

The SaaS service must reach private databases, file shares, and APIs without inbound firewall
exceptions, a general-purpose network tunnel, or any possibility that one tenant can address another
tenant's gateway or resources. The gateway is an outbound-connected, tenant-attested policy
enforcement point. It is not a SOCKS proxy, VPN, or remotely configurable arbitrary host/port relay.

Deliver the tenant-admin enrollment, resource catalog, local enforcement, and typed protocol first
against Managed Dedicated SaaS. Add the shared Gateway Broker registry/routing plane only with Shared
SaaS certification; dedicated-topology success does not prove that shared broker queues, sessions,
buffers, caches, or support operations are tenant-safe.

#### Security and Ownership Invariants

- The authenticated tenant context is derived by the server and is never accepted from a script,
  request parameter, container, gateway name, or resource identifier supplied by a caller.
- Gateway enrollment, resource approval, connection mapping, access grants, disablement, and
  revocation are managed by a **tenant administrator** in the Portal. A SaaS platform administrator
  operates shared infrastructure but cannot create tenant mappings, select on-premises destinations,
  inspect local credentials, or grant themselves resource access.
- A separately assigned on-premises gateway administrator controls which physical destinations and
  local credential references the gateway will expose. Cloud administrators cannot make a gateway
  connect to a destination that was not registered and enabled locally.
- Any exceptional support access is tenant-approved, time-limited, least-privileged, and recorded in
  an immutable audit trail. It does not confer access to local credentials or arbitrary destinations.
- Authorization is enforced independently by the SaaS control plane, Gateway Broker, and on-premises
  gateway. Knowledge of a gateway ID, resource ID, alias, or job ID never grants access.

#### Logical Connection and Resource Mapping

Scripts remain portable and do not select a gateway or physical network address. They reference the
existing governed connection catalog:

```sql
CREATE CONNECTION sales AS MSSQL('SHARED:sales_prod');
```

`SHARED:sales_prod` is resolved inside the server-derived tenant context. Its environment-specific
binding is a discriminated connection record:

- **Direct binding** — connector type, target/options, and `SECRET:name` references for Solo or
  Enterprise execution where the engine can reach the resource directly.
- **Gateway binding** — connector type plus immutable `GatewayId` and `ResourceId`; it contains no
  cloud-side hostname, path, password, or connection string. The referenced gateway resource owns
  the physical endpoint, local secret reference, and permitted operation policy.

There is no `ROUTE='GATEWAY:name'` script option and no automatic local bypass. Promotion between
development, test, and SaaS changes the catalog binding for the same logical alias, not the script.
Connector type compatibility is validated when a binding is saved and again when it is used.

For example, `myserver` is mapped as follows:

```text
Tenant connection alias:  SHARED:sales_prod
  -> tenant gateway:       hq-gateway
  -> registered resource:  corp-sql-sales
  -> gateway-local target: MSSQL myserver:1433 / Sales
  -> gateway-local secret: sales-etl-credential
```

At runtime the script knows only `SHARED:sales_prod`. The tenant catalog resolves it to the tenant's
gateway/resource IDs, the broker routes the authorized operation to that authenticated gateway, and
the gateway resolves `myserver` and the credential locally. Neither physical endpoint nor credential
is sent to or stored in the execution container.

#### Tenant Administration Workflow

1. **Enroll a gateway** — A tenant administrator creates a one-time enrollment in **Admin >
   Gateways**. The on-premises administrator installs `etl-sql-gateway`, consumes the enrollment once,
   and establishes a unique asymmetric workload identity. The Portal shows tenant-scoped identity,
   version, health, last contact, and certificate-rotation status.
2. **Register resources locally** — The on-premises administrator registers an explicit typed resource
   such as MSSQL `myserver:1433` database `Sales`, an approved file root, or an API origin. The record
   includes a stable resource ID, a gateway-local secret reference, allowed operations, and limits.
   The gateway publishes non-secret metadata and health to the tenant catalog. Discovery may propose
   candidates but never makes them usable automatically.
3. **Approve and map** — In **Admin > Connections**, a tenant administrator selects transport
   `Gateway`, a tenant-owned online gateway, and one of its registered resources, then maps it to a
   `SHARED:` alias and grants tenant groups or service accounts access. SaaS connections are
   deny-by-default; an alias with no grants is not globally usable.
4. **Execute and audit** — Each run records the tenant, actor/service account, logical alias, gateway
   and resource IDs, operation class, policy version, byte/row counts, result, and correlation ID. Raw
   secrets, connection strings, query parameters marked sensitive, and file contents are never logged.
5. **Disable or revoke** — A tenant administrator can disable an alias/resource or revoke a gateway.
   The broker and gateway reject new work immediately and invalidate cached bindings and capabilities;
   policy defines whether active reads are cancelled and how in-flight transactions are handled.

The Portal should extend the current tenant-scoped Connections administration surface rather than
create a parallel connection model. Gateways may be a dedicated page or a Connections tab, but all
queries and mutations must be tenant-filtered server-side. Platform operations receive aggregate
service health only; they do not receive tenant resource administration privileges.

#### Runtime Architecture

1. **On-Premises Gateway (`etl-sql-gateway`)**
   - Runs as a hardened Windows service or Linux systemd daemon with a minimal service identity.
   - Initiates outbound-only mutually authenticated TLS over HTTPS port 443. Each gateway has its own
     short-lived, automatically rotated certificate; enrollment credentials are single-use.
   - Executes bounded connector operations and resolves credentials locally. It does not evaluate
     ETL-SQL scripts and does not expose a raw TCP, filesystem, or shell tunnel.
   - Enforces an allowlist by immutable resource ID, operation type, database/catalog or path boundary,
     row/byte/time/concurrency limits, and current local policy. DNS resolution and canonical paths are
     revalidated at connection time to prevent rebinding and traversal.
2. **Gateway Broker**
   - Is a dedicated data-plane service, separate from Portal and Orchestrator control-plane duties.
   - Terminates authenticated gateway sessions, maintains the tenant/gateway session registry, routes
     typed operations, meters traffic, applies backpressure, and isolates queues, buffers, temporary
     data, caches, metrics, and logs by tenant and operation.
   - Routes only when the gateway certificate tenant, server-derived execution tenant, catalog binding,
     resource ownership, and capability claims all agree.
3. **SaaS Control Plane and Execution Containers**
   - The Orchestrator authorizes the run and requests a short-lived, audience-restricted operation
     capability containing tenant, gateway, resource, job/run, operation, limits, expiry, nonce,
     actor/service account, and policy version.
   - The broker and gateway both validate the capability. Capabilities are bound to one operation and
     cannot be replayed for a different gateway, resource, tenant, or run.
   - Containers receive a typed gateway connection handle, never a reusable tunnel or authority to
     choose a gateway, hostname, port, UNC path, or local credential.
4. **Typed Streaming Protocol**
   - Prefer bidirectional gRPC streaming over HTTPS, with a typed WebSocket transport only if required
     for restrictive proxies. Both transports implement the same versioned operation protocol.
   - Database operations carry connector-specific, bounded query/parameter requests and stream typed
     row batches; file operations use registered roots and bounded read/write streams. Arbitrary socket,
     shell, path, or protocol forwarding is prohibited.
   - Deadlines, cancellation, bounded buffering, flow control, maximum request/response sizes, and
     concurrency quotas are mandatory. Reconnect uses operation IDs and a durable outcome ledger;
     ambiguous writes are never retried blindly and must use transactions or explicit idempotency.

#### Availability and Lifecycle

- Support multiple gateways and explicit primary/failover resource bindings without allowing failover
  to change tenant or physical resource policy. Writes require connector-aware failover semantics.
- Define gateway/protocol compatibility windows, signed upgrades, staged tenant rollouts, minimum
  supported versions, drain-before-upgrade behavior, and certificate/key compromise recovery.
- Health must distinguish broker reachability, gateway identity, resource policy status, and an
  optional tenant-triggered connectivity test without exposing sensitive diagnostics.
- Capacity controls must cover per-tenant, per-gateway, per-resource, and per-operation concurrency,
  throughput, bytes, rows, duration, queue depth, and spill usage.

#### Security Certification and Definition of Done

The feature is not complete until automated certification proves all of the following:

- Cross-tenant requests fail even when an attacker knows another tenant's alias, gateway ID, resource
  ID, operation ID, or broker endpoint; mismatched certificate tenants are rejected.
- Capabilities fail when expired, replayed, altered, used for another operation/resource/gateway/run,
  or presented after gateway, resource, connection, principal, or policy revocation.
- A compromised execution container cannot select an arbitrary gateway, host, port, database, file
  path, API origin, or general tunnel operation.
- Gateway enforcement blocks DNS rebinding, path traversal and symlink escapes, unregistered databases
  or shares, unauthorized operation types, and limit overruns.
- Tenant data cannot leak through broker registries, queues, buffers, cache keys, temporary/spill files,
  retry ledgers, logs, traces, metrics, diagnostics, health responses, or support tooling.
- Disconnect/reconnect, cancellation, timeout, broker restart, gateway restart, and failover tests do
  not duplicate writes or report an ambiguous write as safely failed.
- Tenant administrators can complete enrollment, resource approval, alias mapping, access grants,
  connectivity verification, audit review, disablement, and revocation without SaaS administrator
  intervention, while SaaS administrators cannot perform those tenant-owned actions.

### SaaS Multi-Tenancy — Isolated Execution Data Plane (OCI + Hardened Sandboxes)

ETL-SQL must execute mutually untrusted tenant scripts without allowing code, data, credentials,
resource consumption, or residual state to cross tenant boundaries. OCI containers are the portable
workload package, but an ordinary shared-kernel container is not by itself a sufficient security
boundary for hostile SaaS multi-tenancy. SaaS executions run in an ephemeral hypervisor-backed or
equivalently hardened sandbox, with defense in depth across the scheduler, identity, network,
storage, secrets, connectors, telemetry, and control plane.

Managed Dedicated SaaS may initially use a tenant-dedicated VM, worker pool, or cluster as the
hypervisor boundary, with disposable OCI tasks per run inside it. Shared SaaS requires the Hardened
per-run boundary and shared-fleet negative certification below. Both implement the same execution
contract, so increasing density changes placement rather than scripts.

#### Execution Provider and Isolation Contract

- Define one provider-neutral execution request/result contract containing the server-derived tenant,
  immutable artifact hash, job/run/session IDs, actor or service account, policy version, resource
  capabilities, limits, deadline, checkpoint reference, and expected runtime compatibility.
- Keep orchestration independent of a cloud vendor or scheduler. Supported providers can include an
  in-process local provider, an ordinary OCI provider for trusted internal workloads, a Kubernetes or
  equivalent fleet provider, a hardened sandbox provider, and a self-hosted Enterprise worker.
- Publish signed, minimal, multi-architecture OCI execution images through a standards-compatible
  registry. Provider-specific infrastructure configuration must remain outside scripts and portable
  tenant metadata.
- Portal and Orchestrator authorize work but do not directly manage containers or warm pools. A
  dedicated Execution Scheduler owns admission, placement, sandbox lifecycle, quotas, outcome
  reconciliation, and cleanup as a separate data-plane responsibility.

#### Supported Isolation Tiers

| Tier | Minimum boundary | Intended profile |
| :--- | :--- | :--- |
| **Local** | In-process engine under the current OS identity | Solo and trusted development |
| **Standard** | Ordinary OCI container with OS namespace and resource isolation | Trusted Team/Enterprise workloads where policy permits |
| **Hardened** | OCI workload inside a microVM, Hyper-V-isolated container, userspace-kernel sandbox, or independently certified equivalent | Default and minimum for mutually untrusted SaaS tenants |
| **Dedicated** | Tenant-dedicated hardened worker pool, nodes, or cluster | Regulated, high-assurance, or large-tenant isolation |

The workload contract is identical across tiers. Moving to a stronger tier changes placement and
cost, not `.etlsql`/`.rptsql` syntax, connection aliases, or execution semantics. Tenant-authored
external commands and native extensions, if permitted at all, require Hardened or Dedicated
isolation and are disabled by default in SaaS.

#### Per-Run Sandbox Lifecycle

1. The control plane authenticates the actor, derives the tenant context server-side, resolves the
   immutable artifact and governed connection aliases, and authorizes the requested run.
2. The Execution Scheduler admits the run against tenant and fleet capacity, creates a short-lived
   workload identity, and assigns a pristine sandbox satisfying the requested isolation tier.
3. The sandbox receives only the exact artifact and short-lived, audience-restricted capabilities
   required for that run. It receives no reusable platform credential or authority to choose another
   tenant, gateway, secret, path, host, queue, or storage prefix.
4. ETL-SQL executes with bounded scratch space and streams authorized connector operations through
   typed provider or Gateway Broker boundaries. Results are committed only to tenant-scoped targets.
5. The scheduler records the terminal outcome, reconciles ambiguous infrastructure failures, removes
   workload authority, and destroys the sandbox and its writable storage.

Scheduled jobs use a fresh sandbox per attempt. A generic pristine sandbox may be pre-booted to
reduce cold-start latency, but once tenant material enters it the sandbox is single-use and is never
returned to a general pool. An interactive sandbox may live briefly across requests only while it
remains bound to the same tenant, authenticated session, artifact, and policy; it has a hard lifetime
and is destroyed rather than reassigned.

#### Persistent Sessions and Checkpoint Resume

Disposable sandboxes must preserve ETL-SQL's named-checkpoint recovery without preserving a failed
process. At each completed top-level checkpoint, the engine serializes the resumable logical state
outside the sandbox. A replacement sandbox rehydrates that state and resumes from the last completed
author-declared label; it never resumes at an arbitrary statement index.

```text
tenant/{TenantId}/sessions/{SessionId}/
  manifest
  metadata
  encrypted spill chunks
  checkpoint outcome
```

- A checkpoint contains the permitted variable scope, `#temp` table schemas and chunks, lineage
  state, logical connection aliases, and the last completed label. It does not contain live sockets,
  open transactions, child processes, active leases, resolved credentials, or reusable capabilities.
- Checkpoints are durable tenant artifacts, not container-local files. Storage paths/object keys,
  database rows, caches, and encryption contexts are derived from server-owned tenant/session IDs.
  A sandbox receives scoped access to one checkpoint, never a mount or credential covering a tenant
  root or shared session collection.
- Every checkpoint is versioned, authenticated, integrity-checked, and envelope-encrypted with a
  per-session data key protected by the tenant's current key. The manifest records tenant, original
  run, session, checkpoint label, artifact hash, engine/checkpoint schema version, policy and catalog
  binding versions, key version, creation time, expiry, and content hashes.
- Resume fails closed if the tenant, artifact hash, checkpoint label, engine/schema compatibility,
  policy, principal, catalog bindings, encryption key, retention window, or authorization no longer
  permits the operation. Secrets and `SHARED:` connections are reauthorized and resolved afresh.
- The existing default seven-day stale job-session retention remains a configurable policy rather
  than sandbox lifetime. Expiry or explicit tenant-admin deletion removes metadata and all chunks;
  active references cannot extend retention without an audited policy decision.
- Resumption replays work after the last checkpoint, not necessarily the precise failing operation.
  Sections with external writes must use transactions, staging, idempotency keys, or duplicate-safe
  operations. An ambiguous write is never automatically reported as safely failed or blindly retried.

#### Sandbox Security Baseline

- Use a minimal read-only image, non-root workload identity, dropped capabilities, restricted
  syscalls, no privileged mode, no host devices, and no host/container-runtime sockets or paths.
- Default-deny ingress and egress. Permit only DNS behavior required by policy and explicitly
  authorized connector, object-storage, telemetry, control-plane, or Gateway Broker endpoints.
  Block cloud metadata services and lateral tenant/service discovery.
- Allocate bounded encrypted writable scratch and spill storage per run. Do not reuse volumes,
  directories, object prefixes, or encryption data keys across tenants or sandbox assignments.
- Resolve secrets just in time through tenant-scoped authority and keep them memory-only for the
  minimum operation lifetime. Never persist resolved values in checkpoints, environment exports,
  crash dumps, diagnostics, logs, or sandbox images.
- Verify signed images and locked dependencies before scheduling; retain image digest, runtime,
  host policy, and isolation tier in the audit record. Patch and drain vulnerable images/runtimes
  without allowing old warm sandboxes to survive a revocation.

#### Tenant Isolation and Capacity Boundary

Tenant identity and limits must be enforced consistently across control-plane records, execution
queues, leases, scheduler indexes, artifacts, object storage, checkpoints, databases, keys, secrets,
connections, gateways, caches, result sets, notifications, exports, logs, traces, metrics, billing,
diagnostics, support tooling, and deletion workflows. Tenant identity is always server-derived.

Admission and runtime enforcement cover CPU, memory, process/thread count, scratch and spill bytes,
read/write IOPS, network bytes, rows, result size, duration, connector concurrency, queued/running
jobs, and interactive sessions. Scheduling provides per-tenant fairness and global reserve capacity;
one tenant exhausting its allocation must not starve, evict, or degrade another tenant's committed
capacity. Dedicated tiers must not silently fall back to shared placement.

#### Reliability and Outcome Semantics

- Use monotonic attempt IDs, lease fencing, cancellation, deadlines, and a durable run/attempt ledger
  so scheduler or worker restarts cannot create two active owners for one attempt.
- Distinguish script failure, policy denial, resource exhaustion, sandbox/runtime failure, lost worker,
  cancelled work, and ambiguous external outcome. Retry policy is operation-aware and bounded.
- Successful output publication uses staged or transactional commit where supported. Sandbox death
  cannot publish an incomplete artifact or promote an unverified checkpoint.
- Fleet upgrades drain sandboxes, honor execution-image/runtime compatibility, and retain a rollback
  path. A checkpoint produced by an unsupported runtime is rejected with a useful recovery action.

#### Security Certification and Definition of Done

The SaaS execution plane is not complete until automated and retained certification proves:

- Cross-tenant attempts fail across scheduling, workload identity, network, storage, checkpoints,
  spill, caches, queues, secrets, connections, gateways, logs, metrics, and support operations.
- A compromised sandbox cannot reach the host, runtime socket, metadata service, control plane,
  another sandbox, an unauthorized destination, or broader tenant storage/credentials.
- Container/sandbox crashes, forced termination, node loss, scheduler failover, cancellation, and
  resume leave no reusable tenant data and do not duplicate or misreport external writes.
- Warm-pool assignment and destruction tests prove that a later tenant cannot observe memory,
  filesystem, environment, DNS, network, or metadata residue from an earlier assignment.
- CPU, memory, I/O, network, queue, and connector exhaustion by one tenant stays within its quota and
  does not violate another tenant's availability or reserved capacity.
- A failed persistent job can resume in a different sandbox and worker from its last valid named
  checkpoint within retention, while altered scripts, expired/revoked authority, cross-tenant IDs,
  incompatible versions, corrupt chunks, and unsafe retries fail closed.
- The same representative artifact produces equivalent governed results through Local, Standard,
  Hardened, Dedicated, and self-hosted providers wherever the connector set is supported.

### SaaS Multi-Tenancy — Tenant Portability & Migration (Export/Import)

Customers must be able to enter or leave ETL-SQL SaaS without rewriting their pipeline/report logic
or depending on provider-owned infrastructure. The guarantee is full-fidelity migration of portable
customer-owned artifacts and eligible tenant metadata, with explicit rebinding of environment-owned
identities, resources, secrets, keys, and infrastructure. It is intentionally not "zero-loss":
secrets, ephemeral security material, active sessions, leases, caches, and in-flight operations must
not be transferred as durable tenant ownership.

The minimum configuration/artifact bundle and a certified SaaS → self-hosted Enterprise journey are
release gates for Managed Dedicated SaaS, not late Shared-SaaS enhancements. Large evidence/content
exports, incremental deltas, and cross-provider scale optimization may mature later without weakening
the initial customer exit guarantee.

#### Portability Scope and Honest Compatibility

- **ETL-SQL SaaS to another ETL-SQL operator/cluster** — full-fidelity portable artifacts and eligible
  metadata, subject to target version, feature, capacity, connector, and policy compatibility.
- **ETL-SQL SaaS to self-hosted Enterprise** — the same full-fidelity contract, using supported
  self-hosted Portal, Orchestrator, execution-provider, database, artifact-storage, secret-provider,
  Gateway, and identity integrations rather than a SaaS-only representation.
- **SaaS or Enterprise to Solo/Team** — preserve artifacts and state meaningful at the smaller
  profile; preflight reports features that require rebinding, flattening, disabling, or omission.
- **ETL-SQL to a different vendor's product** — export open scripts, manifests, data, lineage, and
  evidence in documented formats, but do not claim that another product will reproduce ETL-SQL
  language, scheduler, governance, lineage, or report semantics.

The portable contract covers customer-owned ETL-SQL state, not provider topology. It does not
attempt to move Kubernetes objects, proprietary queues, provider KMS configuration, database
connection pools, worker images, load balancer state, billing internals, or platform support data.

#### One Unified Tenant Bundle

Extend and unify the existing Portal configuration export, Orchestrator promotion package, portable
source artifacts, and optional historical evidence. Do not introduce a competing packaging model.
The bundle is a documented, versioned directory/archive format with a canonical JSON manifest and
ordinary inspectable payloads rather than an opaque database backup.

The manifest records:

- Bundle and component schema versions, source product version/profile, tenant export identity,
  creation time, export mode, and required target capabilities.
- Stable logical resource IDs, dependency graph, ownership/provenance, content type, byte length,
  cryptographic hash, and payload location for every included object.
- Included, excluded, skipped, redacted, and failed counts by resource class, with a reason for every
  item that is not portable.
- Required identity, connection, gateway resource, path, secret-reference, key, policy, connector,
  and external-service bindings that the target must supply.
- Signature, encryption envelope, chunk/index information, consistency point, and any base-export
  reference required for an incremental package.

#### Included Customer-Owned State

- Exact plain-text `.etlsql` and `.rptsql` artifacts, policies, rules, tags, declarative administration
  scripts, templates, and other source-controlled content with stable identities and hashes.
- Portal folders, report/dataset definitions, jobs, schedules, dependencies, notifications,
  subscriptions, saved views, alerts, ownership references, groups, ACLs, service-account definitions,
  connection aliases, and Gateway/resource binding references without credentials.
- Optional policy-controlled job history, statement evidence, lineage, quality metrics, stewardship
  workflow, quarantine metadata/content, report snapshots, materialized datasets, audit records, and
  tenant-owned artifacts, retaining original timestamps and provenance.
- A secret/reference inventory that tells the target tenant administrator what must be provisioned,
  without including resolved values.

Large evidence or dataset content travels as content-addressed, resumable chunks or a companion
object archive rather than forcing every migration into one monolithic ZIP. Export modes include
configuration-only, configuration plus selected evidence/content, full eligible tenant export, and
incremental delta from a declared base consistency point.

#### Deliberately Excluded State

- Passwords, access/refresh tokens, private keys, resolved secret values, signing keys, KMS keys,
  gateway private identities, one-time enrollment material, and anonymous share/embed capabilities.
- Interactive sessions, persistent execution checkpoints, active leases/locks, in-flight runs,
  temporary/spill files, caches, warm sandboxes, open transactions, and live network connections.
- Provider-owned fleet topology, worker credentials, platform audit/support records, billing internals,
  abuse controls, aggregate telemetry, and any record or identifier belonging to another tenant.
- Environment-specific hostnames, paths, credentials, identity-provider subjects, and physical
  Gateway targets as executable authority. Only logical references and explicit mapping requirements
  are portable.

#### Tenant-Controlled Export and Import Workflow

1. **Authorize and inventory** — A tenant administrator requests an export or import. The service
   performs a non-mutating inventory, classifies state, estimates size/time, checks permissions and
   policy, and produces unsupported/excluded findings. SaaS platform administration alone cannot
   export tenant content or choose its migration destination.
2. **Establish consistency** — Take a database/artifact consistency point. For a final migration,
   optionally place tenant mutations and scheduling into an explicit drain/fence mode; do not
   silently interrupt active work.
3. **Build and verify** — Produce deterministic manifests and content hashes, scan for raw secrets or
   cross-tenant references, sign the complete manifest, encrypt to a tenant-selected recipient, and
   verify that the package can be read before presenting it as successful.
4. **Target preflight** — Validate schema/version compatibility, licenses/features, connectors,
   storage/capacity, identity and ownership mappings, aliases, Gateway resources, secret references,
   policies, paths, name/ID collisions, and unsupported historical content without changing state.
5. **Bind environment authority** — The target tenant administrator maps external identities/groups,
   service-account owners, `SHARED:` aliases, secret references, Gateway resources, paths, API origins,
   notification services, storage providers, and encryption/signing responsibilities.
6. **Import idempotently** — Apply into a staging tenant or transactionally controlled namespace.
   Preserve stable logical identities where possible, never overwrite a conflicting target silently,
   and leave jobs, schedules, subscriptions, alerts, shares, embeds, and service accounts disabled.
7. **Validate** — Compare counts and hashes; parse/lint artifacts; evaluate target policy; verify ACLs
   and tenant isolation; run read-only connectivity checks and representative `WHAT_IF` executions;
   and produce a tenant-readable migration report.
8. **Cut over** — Fence the source scheduler, export/import a final delta if used, obtain tenant-admin
   approval, activate target identities and selected workloads, and prove representative pipeline,
   report, notification, lineage, quality, and audit continuity without duplicate execution.
9. **Rollback or close** — Retain the last reversible point for the agreed window. Source deletion is
   a separate tenant-authorized workflow with legal/retention holds, backup expiry, key destruction,
   and a completion record; successful import never deletes the source automatically.

Expose the same contract through tenant-scoped Portal workflows and scriptable administrative CLI/API
commands such as `etl-sql admin tenant export`, `validate`, `preflight`, and `import`. Large operations
are resumable by operation ID and content hash, and every read/export/download/import/binding/cutover/
delete action is audited without logging protected payloads.

#### Format, Cryptography, and Versioning

- Publish the manifest schemas, compatibility policy, canonicalization/hashing rules, and reference
  reader/validator so a customer can inspect and verify a bundle without contacting the source SaaS.
- Sign the manifest with a documented verifiable signature chain. Encrypt payloads using a
  tenant-supplied recipient public key or tenant-controlled export key; a package encrypted only to
  a provider-owned KMS key is not a usable exit artifact.
- Support bounded decompression, entry-count/size limits, canonical path validation, content-type
  allowlists, signature-before-import, hash verification, and defenses against archive traversal,
  duplicate identities, malformed graphs, decompression bombs, and resource exhaustion.
- Version each component independently and provide explicit N/N+1 compatibility. Upgrades operate on
  the staging representation and never mutate the only source copy. Unsupported future/legacy state
  produces actionable findings rather than silent omission.
- Keep deployment manifests and execution images standards-based and provider-neutral so the same
  tenant bundle can target another hosted operator or the supported self-hosted reference topology.

#### Migration Certification and Definition of Done

The portability claim is not complete until retained end-to-end tests prove:

- A representative tenant moves SaaS cluster A → SaaS cluster B and SaaS → self-hosted Enterprise
  without changing pipeline/report business logic, with explicit bindings for environment authority.
- Export under concurrent activity produces a declared consistent point, final delta/cutover prevents
  duplicate schedules, and rollback restores the last safe source state.
- Every eligible resource is reconciled by stable ID, count, dependency, hash, ownership, ACL, and
  provenance; every exclusion is visible and justified.
- Secret scanners and seeded marker tests prove that credentials, tokens, keys, capabilities,
  checkpoints, platform internals, and other tenants' records never enter the package.
- Tampered, truncated, replayed, expired, corrupt, cross-tenant, oversized, traversal-bearing,
  incompatible, or unauthorized packages fail before target activation and leave no partial authority.
- Imported identities and permissions do not grant more access than the approved mapping; unresolved
  principals/resources remain disabled, and platform administrators cannot assume tenant authority.
- Jobs remain fenced until tenant approval, representative `WHAT_IF` and live proofs pass, and the
  migration report demonstrates scripts, reports, schedules, connections, lineage, quality, history,
  notifications, and audit behavior appropriate to the selected export mode.
- A customer can validate and retain the export using published tooling and customer-held keys even
  after source SaaS access is unavailable.

### Language — Dialect Standardization and Drift Prevention

To secure the portability guarantee across diverse runtime environments and prevent divergence between the runtime execution, editor, and documentation tooling, the ETL-SQL language dialect is formalized into a tool-verified standard.

#### Pillars of Standardization & Verification:
1. **Canonical Grammar Specification (EBNF)**:
   - Define and publish a machine-readable EBNF (Extended Backus-Naur Form) grammar file in the repository.
   - This file serves as the logical reference for lexicographical parsing, preventing documentation drift and providing a design blueprint for parser-related IDE and validation services.
2. **Conformance & Regression Test Suite (SqlLogicTests)**:
   - Expand and maintain the shared suite of SqlLogicTests (SLT) in `tests/slt_data/` asserting exact execution correctness, mathematical offsets, standard library function returns, and query boundaries.
   - This serves as the ultimate regression net to ensure engine modifications do not break existing scripts or introduce silent dialect drift.
3. **Syntax Addition Checklist**:
   - Rather than establishing a high-ceremony RFC process, new language extensions (keywords, functions, or connector options) must validate against a strict checklist in `CONTRIBUTING.md`.
   - Additions must explicitly address cross-dialect compatibility, translation mappings for remote SQL pushdown, and updates to autocomplete / linting state.
4. **EBNF-to-Parser Validation Fuzzing**:
   - Instead of verifying EBNF against the autocomplete-focused `GrammarStateEngine`, implement a validation runner that parses and generates queries from the EBNF specification.
   - Assert that the actual execution parser (`Parser.cs`) accepts valid sequences and rejects invalid ones, creating a tool-enforced guard against compiler-to-spec drift.
5. **Centralized Dialect Translation Engine**:
   - Refactor dialect-specific SQL rewrite logic out of the main compiler (`QueryCompiler.cs`) and separate connector classes.
   - Introduce a structured `ISqlDialectTranslator` or `SqlDialect` registration system, allowing each database provider to modularly define its function overrides (e.g. `LEN` vs `LENGTH`, `GETDATE` vs `SYSDATE`) in a central, testable abstraction.

### Connectors — Transactional File Staging

To prevent downstream systems from consuming half-written or dirty data on execution failure, file-based and network transfer connectors (e.g. `FLATFILE`, `SFTP`) will support native transactional staging boundaries.

#### Core Design & Parameters:
1. **`TRANSACTIONAL=TRUE` Configuration Option**:
   - Enable transactional staging on connection creation.
   - The engine writes target data blocks to temporary `.tmp` files (or in a hidden `.staging/` directory at the destination) during the active execution stream.
2. **Atomic Commits & Automatic Cleanups**:
   - If the script execution completes successfully, the engine issues a fast atomic rename (e.g. `file.csv.tmp` -> `file.csv`) to expose the complete file.
   - If the script fails during any phase (e.g., in a `load:` block), the engine automatically cleans up and deletes the staged files, leaving the production directory in its original clean state.

### Extensions — Governed Custom Tool Runner

ETL-SQL should cover the common transformation, connector, quality, and orchestration workload
natively while providing a governed escape hatch for specialized algorithms, legacy formats,
proprietary libraries, and customer-specific processing that does not belong in the core product.
Custom code must extend the engine's data flow without turning a script into an unrestricted shell,
an unreviewed plugin loaded into the engine process, or a backdoor around connector, path, egress,
secret, audit, checkpoint, and tenant policy.

The feature is a catalog-backed **custom tool runner**, not a raw `CMD` connector. Scripts refer to a
logical approved tool and operation; they never choose `python`, `powershell`, `bash`, an executable
path, OCI image tag, working directory, shell command, or arbitrary argument string.

#### Trust and Portability Invariants

- ETL-SQL remains the conductor: input is produced through governed engine/connector reads, streamed
  to the tool, validated on return, and staged in engine-owned output before downstream publication.
- A script names a stable logical tool alias. The active environment resolves that alias to an
  immutable approved tool version and physical runtime binding, just as a `SHARED:` connection maps
  portable logic to environment-owned connection authority.
- Tool code is untrusted relative to the platform even when it is tenant-authored or administrator-
  approved. Approval grants only the declared operation and capabilities; it does not confer the
  Portal, Orchestrator, execution worker, tenant administrator, or host service identity.
- No shell is invoked. Command composition, pipes, redirection, globbing, command substitution,
  dynamic interpreter expressions, and caller-controlled executable selection are prohibited.
- A tool cannot use custom code to bypass a denied ETL-SQL connector, destination, filesystem root,
  secret, Gateway resource, mutation guardrail, or isolation tier. Any non-tabular capability is
  declared and authorized independently.
- The same ETL-SQL artifact moves between profiles without changing its logical tool reference.
  Environments may bind the alias differently or reject it during promotion preflight when no
  compatible approved implementation exists.

#### Governed Tool Catalog

Each tool registration contains:

- Immutable tool ID, logical alias, version, tenant/environment ownership, publisher, approver, and
  lifecycle state (`Staged`, `Approved`, `Disabled`, or `Revoked`).
- Artifact type, content digest, signature/provenance expectations, source revision/build identity,
  optional SBOM/scan evidence, supported operating systems/architectures, runtime, and fixed entry
  point. Mutable tags and unresolved executable search paths are not executable authority.
- Named operations with an operation class, protocol version, input/output schemas, parameter schema,
  maximum rows/bytes/frame size/duration/concurrency, determinism and idempotency declarations, and
  required isolation tier.
- Explicit network destinations, filesystem/object-storage roots, secret-reference bindings, child-
  process permission, scratch requirements, and other capabilities. The default is none.
- CPU, memory, process/thread, scratch/spill, I/O, network, output, and log limits plus cancellation,
  graceful-shutdown, and forced-termination policy.
- Ownership, grants, deployment-profile availability, promotion mappings, usage/dependency impact,
  last verification, revocation reason, and immutable audit history.

The catalog stores references and approval metadata, not resolved secret values. A tool version is
re-approved when its digest, entry point, parameter contract, capabilities, runtime, or publisher
changes. Alias rebinding is optimistic-concurrency controlled, impact-reviewed, and audited; a
mutable file or image tag changing underneath an approved record never changes executable code.

#### Administration and Separation of Duties

- **Solo developer** — explicitly enables a local tool registry and approves their own local package;
  the product clearly states that Solo cannot protect the user from code they choose to run.
- **Team/Enterprise tool publisher** — submits a versioned artifact and typed manifest but cannot
  approve its own production capabilities where organization policy requires separation of duties.
- **Enterprise security/operations administrator** — approves publishers, artifacts, capabilities,
  isolation tier, resource ceilings, and promotion into an environment. Tools execute as a dedicated
  low-privilege worker/sandbox identity, never the Portal or Orchestrator service account.
- **SaaS tenant administrator** — manages tenant-owned aliases, versions, grants, and requested
  capabilities within platform policy. A SaaS platform administrator enforces runtime/supply-chain
  requirements but cannot silently replace a tenant's tool, grant tenant access, or use it to assume
  tenant authority.

Portal and CLI workflows support submit, inspect, validate, approve, reject, bind, test with bounded
fixtures, promote, disable, revoke, show dependencies, and view audit/usage. SaaS artifact upload and
activation are disabled until the tenant's authoring/ingress boundary and malware/supply-chain
inspection path are certified.

#### Operation Classes and Delivery Sequence

1. **V1 — Pure tabular transform**
   - Accepts a typed bounded row stream plus declared non-secret parameters and returns a typed row
     stream. It has no network, durable filesystem, secret, Gateway, connector, or child-process
     capability and may write only to disposable bounded scratch.
   - Covers specialized parsing, proprietary algorithms, legacy encoding, data cleansing, address or
     domain standardization, scientific calculations, and local inference while preserving the rule
     that external reads/writes remain governed ETL-SQL connector operations.
   - Is treated as replayable only when the same immutable tool digest, input/checkpoint identity,
     parameters, protocol, and policy remain valid. Partial output is never published.
2. **Later — Governed action tool**
   - Performs an explicitly declared external side effect only when a native connector or API cannot
     reasonably express the operation. Network, path, secret, and destination capabilities are
     individually approved and short-lived.
   - Requires a stable operation/idempotency ID, durable outcome ledger, reconciliation contract, and
     declared retry/transaction/compensation semantics. An unknown external outcome is never retried
     automatically or reported as safely failed.

Keeping V1 pure is a security and reliability boundary, not merely a reduced feature set. A side-
effecting tool must not be disguised as a transform to inherit its retry or checkpoint behavior.

#### Runtime Bindings by Profile

| Profile/topology | Permitted binding |
| :--- | :--- |
| **Solo** | Canonical approved local executable/package path under the user's authority, with explicit opt-in and local limits |
| **Team** | Admin-approved artifact executed by a dedicated low-privilege worker or configured sandbox; never ambient Portal/Orchestrator authority |
| **Enterprise** | Signed/digest-pinned OCI artifact or canonical managed executable in a policy-selected isolated worker/sandbox |
| **Managed Dedicated SaaS** | Tenant-dedicated worker/VM boundary with a disposable OCI or stronger sandbox per attempt |
| **Shared SaaS** | Disabled by default; when permitted, digest-pinned artifact in a Hardened or Dedicated sandbox with hostile cross-tenant certification |

Provider-specific bindings implement one tool-execution contract. Local execution must remain useful
without requiring Docker, while Enterprise/SaaS packaging favors standards-compatible OCI artifacts
and does not embed provider-specific image registry or scheduler syntax in ETL-SQL scripts.

#### Direct Process Security Baseline

Where a profile permits a native process binding:

- Resolve a fixed executable from the approved tool record to a canonical absolute path. Do not use
  `PATH`, file associations, the operating-system shell, or a command string.
- Start the process directly with shell execution disabled and add each fixed/validated argument as a
  distinct structured argument. `cmd /c`, `powershell -Command`, `sh -c`, `bash -c`, `eval`, and
  equivalents are prohibited. A registered script runtime uses a fixed interpreter and immutable
  registered script path, not a caller-supplied expression.
- Construct a minimal allowlisted environment; do not inherit platform credentials, secret-bearing
  variables, profile scripts, user startup files, proxy credentials, or writable executable search
  paths. Never pass secrets on the command line.
- Use a new canonical scratch/working directory inside the authorized run boundary, a dedicated
  low-privilege identity, no host devices/runtime sockets, and default-deny network and durable
  filesystem access.
- Redirect and concurrently drain input, output, and diagnostics with bounded buffers/backpressure.
  On timeout or cancellation, close input, allow the configured grace period, terminate the entire
  process tree/sandbox, discard partial output, and retain a sanitized terminal result.

Interpreters are not automatically trusted merely because their executable is allowlisted. The
approved artifact, fixed entry point, parameters, imports/dependencies, and capabilities are the unit
of authority.

#### Typed Streaming Protocol

- Prefer Arrow IPC streaming for high-volume typed tabular data already natural to the ETL-SQL
  engine. Provide JSON Lines as an approachable compatibility protocol for Python, PowerShell, and
  other runtimes; do not make it the only or canonical high-volume representation.
- Define a versioned handshake, declared input/output schemas, null/decimal/timestamp/timezone/binary/
  Unicode behavior, maximum frame/line and total sizes, compression negotiation, backpressure,
  cancellation, terminal outcome, and compatibility rules.
- Standard output contains protocol frames only. Standard error is a separate sanitized, bounded,
  rate-limited diagnostic channel. Unexpected text, malformed frames, schema drift, excessive output,
  or a success exit without a valid terminal envelope fails the operation.
- Treat every output value as untrusted. Validate schema, types, sizes, row count, encoding, and data-
  quality rules before exposing rows to downstream ETL-SQL statements or publication.
- Stream in O(1)-bounded memory through engine batches. The runner must not buffer the complete input,
  output, or diagnostic stream merely to simplify process handling.

#### Secrets, Files, and Network Capabilities

- Pure transforms receive no resolved secrets. Later action tools identify named secret bindings in
  their approved manifest; the runtime resolves them just in time through a dedicated protected
  channel and never places them in arguments, ordinary row input, logs, checkpoint state, or exports.
- File access uses server-derived capability roots and `ResolvePath`-equivalent canonical enforcement
  inside the sandbox. Tool code cannot read ETL-SQL source, host configuration, session collections,
  another tool's artifacts, or another tenant's scratch/checkpoints.
- Network access is default-deny and restricted to approved canonical destinations/protocols/ports
  with DNS rebinding, redirects, alternate address forms, cloud metadata, and internal control-plane
  protections. Prefer passing data through governed ETL-SQL connectors rather than granting network.
- Capability decisions are bound to tenant/environment, tool/version/digest, operation, actor, job/
  run/attempt, limits, policy version, expiry, and nonce. Revocation denies new operations and defines
  audited handling for in-flight work.

#### Checkpoints, Retries, and Publication

- A checkpoint records the logical tool alias, immutable tool ID/version/digest, operation, protocol,
  parameter hash, input/checkpoint identity, policy/binding versions, and any fully validated staged
  output reference. It never serializes a running process, open handle, live connection, resolved
  secret, or reusable capability.
- Resume in a replacement sandbox reauthorizes the tool and capabilities. Changed/revoked artifacts,
  policies, bindings, schemas, principals, or incompatible runtime/protocol versions fail closed.
- Pure-transform output is staged and integrity-checked; it becomes visible only after valid protocol
  completion and schema/quality validation. Cancellation, malformed output, non-zero exit, limit
  violation, or sandbox loss discards the incomplete result.
- Action tools use the durable operation ledger and their declared idempotency/reconciliation contract.
  ETL-SQL never infers that process exit means an external side effect did or did not occur.

#### Lineage, Observability, and Audit

- Lineage records the logical alias, tool ID/version/digest, named operation, input/output schemas,
  parameter hash or redacted parameter names, publisher, and transformation classification. Optional
  declared column mappings may improve column lineage; an opaque tool is labeled opaque rather than
  inventing unsupported derivations.
- Metrics include queue time, startup, CPU, peak memory, scratch/spill, input/output rows and bytes,
  backpressure, duration, exit/outcome class, retries, and policy decisions, partitioned by tenant and
  operation without recording payloads or secret values.
- Audit covers submit/approve/bind/promote/test/run/deny/disable/revoke, capability and secret access,
  support access, artifact verification, and output publication. Tool stdout/stderr is never treated
  as trusted log structure and is sanitized before bounded retention.

#### Security Certification and Definition of Done

The custom tool runner is not complete until automated and retained evidence proves:

- Scripts cannot select arbitrary executables, interpreters, paths, image tags, arguments, shells,
  environments, working directories, users, mounts, networks, secrets, or isolation tiers.
- Command/argument injection, shell metacharacters, path traversal/symlink escape, `PATH`/environment
  substitution, child-process escape, protocol confusion, malformed/oversized output, log injection,
  fork/process exhaustion, timeout evasion, and cancellation races fail safely.
- A hostile tool cannot read scripts, host/platform credentials, control-plane services, cloud
  metadata, unauthorized connectors/destinations, broader tenant storage, checkpoints, another tool,
  sandbox residue, or another tenant's data.
- Artifact signatures/digests and approval bindings reject mutable, tampered, substituted, revoked,
  incompatible, or unapproved code before execution. Verification checks the artifact and configured
  provenance expectations, not merely the registry/tag name.
- Streaming remains bounded and deadlock-free under slow input/output, full stderr, early exit,
  malformed frames, cancellation, and large datasets; partial output never reaches a committed target.
- Checkpoint resume in another worker/sandbox reproduces an approved pure transform or fails closed;
  side-effecting tools never duplicate ambiguous operations through automatic retry.
- Cross-profile certification runs the same logical tool reference in Solo, Enterprise, Managed
  Dedicated SaaS, and Shared SaaS where supported, with explicit preflight findings where a target
  binding or policy is unavailable.
- Tenant administrators can manage their aliases/grants without platform impersonation, while
  platform policy can revoke unsafe artifacts/capabilities without receiving tenant data authority.

### Reporting — Paginated PDF Export Engine

To support traditional enterprise reporting requirements (similar to SSRS or Crystal Reports), the visualization system needs a layout-aware PDF generation engine.

#### Core Design & Parameters:
1. **Physical Page-Breaking and Layout Rules**:
   - Translate responsive 12-column grid CSS layouts (`STRUCTURE`) into fixed A4/Letter pages on PDF export.
   - Introduce card properties like `PAGE_BREAK = BEFORE | AFTER` to control printable pagination boundaries.
2. **Repeating Table Headers & Footers**:
   - The PDF exporter must automatically repeat `TABLE` headers at the top of every physical page during multiline grid overflow.
   - Support system placeholders in report footers (e.g. `Page X of Y`, runtime timestamp).

### Reporting — Inline Row Detail Subreports

To enable hierarchical and nested data visualization inside tables without forcing users to navigate to separate pages or visuals.

#### Core Design & Parameters:
1. **The `ROW_DETAIL` Mapping Clause**:
   - Expand the `TABLE` mapping syntax to support a collapsible child container:
     ```sql
     CREATE VISUAL CustomerTable AS TABLE (
       SOURCE = #customers,
       MAPPINGS (
         CustomerID, Name, Email,
         ROW_DETAIL (
           TARGET = OrderSubTable,
           KEY = CustomerID
         )
       )
     );
     ```
2. **Interactive Row Expansion**:
   - The Table UI renders a toggle icon (`▸`) at the start of each row. Clicking it expands the row vertically to embed the `TARGET` visual, pre-filtered by the row's `KEY` context.
