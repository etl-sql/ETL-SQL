# ETL-SQL Product Roadmap

This document tracks high-level product tracks and candidate phases. Their actionable work is
decomposed in `TODO.md`. Once an initiative is verified, record its notable outcome in
`CHANGELOG.md` and mark its `TODO.md` entry complete without deleting it. Product-level roadmap
entries may be retired when they no longer describe future work. Release-specific detail belongs
in the release notes under `docs/releases/`.

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

**Delivery stage.** The minimum configuration/artifact bundle and a certified SaaS → self-hosted
Enterprise journey are release gates for Managed Dedicated SaaS, not late Shared-SaaS enhancements.
Large evidence/content exports, incremental deltas, and cross-provider scale optimization may mature
later without weakening the initial exit guarantee. One unified bundle extends the existing Portal
configuration export and Orchestrator promotion package; do not introduce a competing packaging
model, and do not represent the bundle as an opaque database backup.

**Certification gate.** A representative tenant moves SaaS cluster A → SaaS cluster B and SaaS →
self-hosted Enterprise without changing business logic; export under concurrent activity produces a
declared consistency point and cutover creates no duplicate schedules; every eligible resource
reconciles by stable ID, count, hash, ownership, and ACL, with every exclusion visible and justified;
secret scanners prove no credential, key, capability, or other tenant's record enters the package;
tampered, replayed, cross-tenant, or oversized packages fail before target activation; and a customer
can validate and retain the export with published tooling and customer-held keys after source SaaS
access is gone.

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

### Reporting — Paginated Print Layout & PDF Rendering

Provide the physical-page reporting capabilities needed for invoices, statements, operational
reports, scheduled delivery, and regulatory output. The objective is practical SSRS/Crystal Reports
parity for page layout and tabular pagination, not RDL compatibility or a second reporting language.

This work extends the existing static and browser-backed PDF exporters and `PDF_MODE` selection. It
does not introduce an unrelated PDF pipeline. A shared print-layout contract must produce consistent
results from CLI, Portal, Orchestrator subscriptions, and supported deployment platforms.

`CREATE PAGE ... AS PAGINATED` already means deferred report-parameter application. Physical print
pagination must use distinct terminology such as `PRINT_LAYOUT` or `PAGE_LAYOUT`; it must not overload
the existing `PAGINATED` page mode.

#### Physical Page Contract

- Define page size (`LETTER`, `A4`, and bounded custom dimensions), orientation, margins, printable
  area, and measurement units explicitly.
- Use the responsive page/container `STRUCTURE` as the default placement input, but allow an explicit
  print-layout override. Responsive dashboard geometry cannot be assumed to fit every physical page.
- Define deterministic behavior for visual width, minimum height, wrapping, scaling, clipping, and
  horizontal overflow. Wide tables must follow an author-selected fit, wrap, landscape, or fail policy;
  the renderer must not silently discard columns.
- Add layout controls at visual, container, table, and table-group boundaries for page break before,
  page break after, keep together when possible, and row splitting.
- Charts, cards, images, text, and containers remain together when they fit. When they do not fit, the
  renderer follows a documented split/scale policy instead of relying on browser-specific CSS behavior.

#### Tables, Groups, Headers, and Footers

- Flow complete table data across physical pages. Existing preview or safety row caps must not become
  silent PDF truncation; configured export limits fail clearly and identify the exceeded limit.
- Repeat table column headers on each vertical page and row headers where a table spans horizontally.
- Support group headers and footers, keep a group header with at least its first detail row, and allow
  page breaks before, after, or between groups.
- Add true print page-header and page-footer regions independent of the existing web-shell HTML footer.
  They may contain bounded text, images, lines, report metadata, selected parameter values, export
  timestamp, page number, and total page count.
- Page-number and total-page placeholders are resolved by the renderer, including any required second
  layout pass. Culture and timezone are captured from the report execution context.
- Define first-page, last-page, odd/even-page, and empty-page behavior rather than inheriting accidental
  differences between rendering modes.

#### Rendering Architecture

- Compile report definitions and runtime data into one renderer-neutral print-layout model. Static and
  browser-backed exporters consume that model instead of independently inventing pagination rules.
- Treat the deterministic server-side renderer as the canonical paginated-document path. Browser-backed
  PDF remains useful for dashboard snapshot fidelity but must not be the only way to obtain correct
  physical pagination.
- Preserve searchable text, links, document metadata, and embedded or explicitly substituted fonts.
  Font substitution, culture, timezone, and chart rasterization are observable in export diagnostics.
- Provide print preview in the Report Builder using the same page contract used by unattended exports.
- Define how interactive state becomes a document: applied parameters and filters are captured at export
  start, while hover, focus, selection, and other browser-only state are excluded unless a report option
  explicitly promotes them to export state.

#### Safety, Operations, and Definition of Done

- Enforce configurable limits for rows, pages, images, rendered bytes, layout passes, and export time.
  Cancellation cleans temporary files and browser processes, and partial documents are never published
  as successful subscription artifacts.
- Resolve report assets through existing path, governance, tenant, and authorization boundaries. Remote
  images and hosted browser export remain subject to outbound-network policy and bounded retrieval.
- Subscription, retry, and HA behavior is deterministic: a retried export uses the same immutable report
  version, parameter snapshot, data snapshot policy, culture, timezone, and renderer configuration.
- Certification covers A4/Letter, portrait/landscape, repeating headers, group breaks, keep-together,
  page totals, oversized content, wide tables, long tables, font substitution, cancellation, and identical
  authorization behavior across interactive and unattended export.
- The feature is complete only when layout assertions and rendered-page regression tests prove that
  content is neither clipped nor silently omitted on supported Windows and Linux hosts.

### Reporting — Expandable Master/Detail Rows

Allow a `TABLE` row to expand in place and display related detail without forcing navigation to a
different page. This is an expandable master/detail or drilldown interaction, not an SSRS-style
subreport: a true subreport is a separately published report with its own execution and parameter scope.
Keeping those concepts separate avoids introducing hidden per-row report execution and authorization.

#### Prepared-Data Execution Model

- The first release uses parent and child data prepared by the same plain-text report script. The engine
  evaluates security, transformations, and row filtering before either dataset enters the report manifest.
- The runtime builds a bounded index over the child relationship fields and renders matching children on
  expansion. It must not issue one database query per parent row or allow the browser to construct SQL.
- Prepared data preserves portability, offline/static report use, deterministic PDF export, and consistent
  behavior across CLI, Report Player, Portal, and subscriptions.
- Deferred or externally stored visual rows remain tenant-, report-version-, session-, and authorization-
  scoped. Client-side collapsing is presentation behavior and is never treated as a security boundary.

#### Structural Detail Contract

- Row detail is a structural `TABLE` capability, not a `MAPPINGS` column. `MAPPINGS` continues to describe
  displayed column projections and formatting.
- A detail definition references a child visual or child container template and declares explicit typed
  parent-to-child bindings. Both sides of every binding are named; a lone `KEY = CustomerID` is insufficient.
- Bindings support composite keys and define null, duplicate, missing-child, and incompatible-type behavior.
- Raw typed binding values are retained as hidden row metadata before display mappings rename, format, or
  omit columns. Formatted cell text is never used as an execution or relationship key.
- The contract defines default expanded/collapsed state, whether multiple rows may remain open, maximum
  nesting depth, maximum open rows, maximum detail rows/bytes, and behavior after sorting, filtering,
  parameter application, refresh, and data-version changes.
- Target references participate in linting, lineage, dependency analysis, packaging, and cycle detection.
  Missing targets or bindings fail during validation rather than at the first user click.

#### Interactive and Accessible Behavior

- Render an explicit button in the row header with keyboard operation, accessible labeling,
  `aria-expanded`, and an owned detail region. Expansion must preserve valid table/grid semantics.
- Display loading, empty-detail, error, retry, and authorization-denied states without corrupting the
  parent table or exposing provider exception details.
- Integrate detail content with existing visual actions and interactions using a scoped parent-row context;
  a detail visual cannot mutate another row's context accidentally.
- Virtualization and row recycling preserve expansion state by stable raw key, not by visible row index.
  Sorting or paging therefore cannot attach one customer's detail to another customer's row.

#### Export and Pagination Semantics

- Define report-authored detail export behavior such as omit details, include all details, or include
  details selected by a deterministic expression. PDF and unattended exports do not inherit incidental
  expand/collapse state from an unrelated browser session.
- When details are included in paginated output, keep the parent row with the first child row when possible,
  repeat child table headers, honor group/page-break rules, and clearly delimit each parent instance.
- Define noninteractive behavior for PDF, HTML snapshot, CSV, and spreadsheet export. Formats incapable of
  representing nesting must fail clearly or use an explicitly selected flatten/separate-data policy.

#### Security and Definition of Done

- Every parent and child row in the manifest has already passed connector policy, report authorization,
  tenant isolation, and applicable row-level policy. Filtering unauthorized rows in JavaScript is forbidden.
- Detail bindings are data values, never script fragments, connection names, object names, or executable
  expressions supplied by the browser.
- Certification covers composite and formatted keys, nulls, duplicate parents, missing children, large
  child sets, virtualization, refresh races, cancellation, malicious values, cross-tenant attempts,
  accessible navigation, and all supported export policies.
- Performance tests prove that expansion does not cause N+1 connector execution and that manifest/index
  limits fail safely under adversarial cardinality.

#### Explicitly Deferred: Reusable Report Subreports

A later, separately approved roadmap item may embed a published child report and bind parent values to
declared child parameters. That feature would require permission intersection, immutable version binding,
parameter type validation, dependency and cycle detection, recursion limits, cache isolation, audit,
failure rendering, and protection from per-row execution amplification. It must not be smuggled into the
prepared-data master/detail implementation.
