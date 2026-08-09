# ETL-SQL Deployment Profile Architecture

**Status:** Approved target architecture; implementation and certification remain incremental

**Applies to:** Solo / Workstation, Team / SME, Enterprise / Corporate, and SaaS /
Multi-Organization deployments

**Purpose:** Define how one portable ETL-SQL product is hosted behind progressively stronger
management, durability, authority, and isolation boundaries

**Implementation sequence:** [Product Roadmap](../../ROADMAP.md)

**Current support evidence:**
[Deployment Profile Standards](standards/Deployment_Profile_Standards.md)

**Adoption, migration, and certification strategy:**
[Deployment Profile and Portability Strategy](roadmaps/Deployment_Profile_Strategy.md)

---

## 1. Decision

ETL-SQL has one language, one artifact model, and one execution model exposed through four
cumulative **deployment profiles**:

1. **Solo / Workstation**
2. **Team / SME**
3. **Enterprise / Corporate**
4. **SaaS / Multi-Organization**

The profiles are not editions, forks, pricing tiers, or separately implemented products. They are
supported operating envelopes around the same engine. A larger profile replaces local providers
with shared or governed providers and adds authority; it does not change pipeline or report business
logic.

The architectural promise is:

> A source-controlled `.etlsql` or `.rptsql` artifact that is valid in one profile retains the same
> language and business semantics in every larger profile where its required connector and
> capability set is allowed. Promotion changes environment bindings, identities, policy, capacity,
> and placement—not the artifact's business logic.

Portability does not mean that every target must permit every operation. A target may reject an
unavailable connector, unresolved binding, insufficient grant, prohibited capability, or capacity
requirement. It must do so during preflight or authorization with an actionable explanation; it must
not rewrite the artifact or weaken target policy to make the promotion appear successful.

## 2. Document Authority

Deployment-profile material is deliberately split by purpose:

| Document | Owns | Does not own |
| :--- | :--- | :--- |
| **This architecture** | Stable profile definitions, component boundaries, provider substitution, logical resource resolution, state placement, authority flow, and topology invariants | Current completion status or release scheduling |
| [SaaS Tenant Isolation Architecture](SaaSTenantIsolation.md) | Tenant context, control/data-plane isolation, sandbox/checkpoint boundaries, Gateway connectivity, capacity, observability, support, and isolation certification | Tenant export/import format |
| [Tenant Portability Architecture](TenantPortability.md) | Portable state classification, bundle format, rebinding, import, cutover, rollback, deletion, and customer exit | Runtime tenant isolation implementation |
| [Deployment Profile Standards](standards/Deployment_Profile_Standards.md) | Normative capability matrix, smallest safe form, evidence status, and feature-review questions | Future architecture or delivery order |
| [Deployment Profile and Portability Strategy](roadmaps/Deployment_Profile_Strategy.md) | Adoption journeys, transition workflow, certification program, and strategic definition of done | Low-level component ownership |
| [Enterprise Platform Strategy](roadmaps/Enterprise_Platform_Strategy.md) | Enterprise trust hierarchy, governance, identity, audit, HA, and operating model | SaaS fleet isolation or commercial hosting topology |
| [ROADMAP.md](../../ROADMAP.md) | Work sequencing, candidate phases, dependencies, and delivery gates | Durable design decisions already defined here |
| `TODO.md` | Actionable implementation and verification work, retained after completion | Product architecture |

If implementation reveals that a decision here is incorrect, update this document intentionally and
record the reason. Do not let a provider-specific implementation silently become the architecture.

## 3. Goals and Non-Goals

### 3.1 Goals

- Preserve script-first, source-controlled artifacts across all profiles.
- Keep parser, evaluator, connector, reporting, quality, lineage, and governance semantics common.
- Allow a deployment to grow by substituting providers and adding control boundaries.
- Make every environment-owned dependency explicit and preflightable.
- Preserve customer exit from SaaS to another ETL-SQL operator or self-hosted Enterprise.
- Give Solo useful local forms of capabilities whenever the security model permits them.
- Require hostile-boundary evidence before making SaaS tenant-isolation claims.
- Keep cloud vendor, scheduler, sandbox, database, storage, secret-provider, and gateway choices out
  of portable scripts.

### 3.2 Non-Goals

- Defining product packaging, licensing, prices, or commercial editions.
- Making all profiles operationally identical or requiring enterprise infrastructure for Solo.
- Treating Team as a separate codebase or simplified language.
- Treating an Enterprise departmental boundary as proof of hostile SaaS tenant isolation.
- Promising that resolved secrets, active leases, open transactions, running processes, or
  interactive sessions can be migrated.
- Making provider topology, cloud resources, or Kubernetes objects portable customer artifacts.
- Supporting arbitrary down-migration without compatibility findings and explicit loss decisions.

## 4. Vocabulary

### 4.1 Profile

A **profile** describes the supported trust and operating envelope: who administers the system,
which identities exist, where durable state lives, and what isolation evidence is required.

### 4.2 Topology

A **topology** is one implementation arrangement within a profile. SaaS has two planned topologies:

- **Managed Dedicated SaaS** — an automated tenant-specific Enterprise-style deployment with
  dedicated database, artifact, key, queue, and worker or hypervisor boundaries.
- **Shared SaaS** — shared tenant-aware control planes and fleets with hardened per-run execution,
  fair scheduling, metering, and hostile cross-tenant certification.

These topologies are delivery stages within SaaS, not fifth and sixth profiles. Evidence from
Managed Dedicated does not certify Shared SaaS.

### 4.3 Overlay

An **overlay** adds constraints to a profile without changing script semantics. Examples include high
availability, disaster recovery, regulated operation, air-gapped operation, high-volume workloads,
and regional data residency.

### 4.4 Portable Artifact

A **portable artifact** is customer-owned, versioned, and interpretable without a specific hosting
provider. It includes scripts, reports, rules, tags, assertions, declarative job/report definitions,
and references to logical resources. It never includes a resolved password, bearer token, private
key, or reusable platform capability.

### 4.5 Environment Binding

An **environment binding** maps a stable logical reference in an artifact to authority and
infrastructure owned by the target environment. Bindings include connections, secret references,
paths, identities, storage, notification endpoints, execution tiers, and gateway resources.

## 5. Architectural Model

Every profile uses the same logical layers. A smaller profile may collapse them into one process;
a larger profile separates them across services and stronger trust boundaries.

```text
Portable source artifacts
        |
        v
+-------------------------------+
| Control and authorization     |
| identity, catalog, policy,    |
| ownership, grants, scheduling |
+---------------+---------------+
                | authorized immutable execution request
                v
+-------------------------------+
| Execution data plane          |
| engine, connector mediation,  |
| limits, checkpoints, results  |
+---------------+---------------+
                | typed, scoped resource operations
                v
+-------------------------------+
| Environment resource plane    |
| databases, files, APIs,       |
| secrets, storage, gateways    |
+-------------------------------+
```

In Solo these layers may share the current OS identity and local process. Team introduces durable
shared services. Enterprise separates users, service identities, providers, policy, audit, and HA
ownership. SaaS adds tenant identity and isolation at every layer and may move execution into a
separate hardened fleet.

### 5.1 Portable Artifact Plane

The portable artifact plane contains the versioned intent:

- `.etlsql` and `.rptsql` source
- Rules, assertions, tags, and lineage declarations
- Declarative jobs, schedules, reports, and notification definitions
- Logical connection, secret, storage, tool, and external-service references
- Required capability and compatibility metadata

Artifacts are immutable for an execution attempt. Catalog publication records the content hash and
provenance used by the run.

### 5.2 Control Plane

The control plane owns durable intent and authority:

- Authentication and server-derived actor, organization, and tenant context
- Catalog identity, ownership, grants, policy, and approvals
- Environment bindings and secret references
- Job and report scheduling
- Admission requests, quotas, and execution placement policy
- Audit intent, lifecycle operations, promotion, export, and deletion

The control plane authorizes work but does not grant a workload broad administrative credentials.
It emits a bounded execution request containing only the authority required for that attempt.

### 5.3 Execution Data Plane

The execution plane owns attempt lifecycle:

- Resolve and verify the immutable artifact
- Enforce execution policy and resource limits
- Evaluate ETL-SQL with common engine semantics
- Mediate connector operations through scoped bindings or typed gateways
- Stage and commit outputs
- Persist named checkpoints where supported
- Record a fenced terminal outcome and destroy attempt-local authority

Portal and Orchestrator must not become container runtimes or general network proxies. In larger
topologies, a dedicated execution scheduler/provider owns admission, placement, sandbox lifecycle,
outcome reconciliation, and cleanup.

### 5.4 Environment Resource Plane

The resource plane owns physical endpoints and protected material:

- Database hosts, catalogs, file roots, object stores, APIs, and message systems
- Secret providers, key providers, and local credential references
- Artifact, checkpoint, spill, result, audit, and backup storage
- On-premises gateways and their locally approved resource catalogs
- Notification and identity-provider integrations

Portable scripts name logical resources. Only the environment binding knows how to reach them.

## 6. Non-Negotiable Invariants

### 6.1 One Language and Runtime Contract

Profiles must not introduce profile-specific grammar, AST nodes, evaluator behavior, connector
semantics, report calculations, data-quality formulas, lineage rules, or checkpoint meaning. A host
may add authorization and policy checks around the common behavior.

### 6.2 Authority Is Server-Derived

In shared services, authenticated server state determines actor, organization, tenant, resource,
and execution authority. Scripts and client requests cannot select another tenant by supplying a
tenant ID, alias, path, gateway ID, object ID, queue name, or storage prefix.

### 6.3 Logical Names Are Portable; Physical Authority Is Not

An artifact may carry a stable logical connection or resource reference. It does not carry a
provider credential, physical gateway destination, platform queue, worker identity, or unrestricted
host/path selector. Promotion maps the logical reference to a target-owned binding.

### 6.4 Larger Profiles Are Additive

A larger profile adds collaboration, durability, identity, policy, availability, and isolation.
It must not require a second artifact solely because the hosting model changed. When a capability
cannot safely exist in a smaller profile, the smaller profile reports it as not applicable or
unsupported instead of simulating a nonexistent security boundary.

### 6.5 Control and Data Are Separately Authorized

Permission to define or schedule a workload does not automatically grant access to every connection
used by that workload. Resource grants are re-evaluated for the executing actor or service identity,
including delivery-time and resume-time authorization where applicable.

### 6.6 Durable State Has an Owner and Scope

Every database row, artifact key, cache key, queue entry, lease, checkpoint, spill object, result,
log, trace, metric, audit record, backup, and deletion marker has an explicit profile-appropriate
owner. Shared SaaS requires server-derived tenant scope in addition to resource scope.

### 6.7 Evidence Is Topology-Specific

A capability is supported only for the profile and topology exercised by current evidence.
Configuration availability, a unit test, or success in a weaker topology is not sufficient proof.

## 7. Profile Definitions and Component Placement

### 7.1 Solo / Workstation

**Trust boundary:** One trusted operator under the local OS/process identity.

**Required shape:**

- CLI, VS Code, Workstation Editor, or Report Player
- In-process engine execution
- Local files and SQLite where persistence is useful
- Machine/user-protected secrets or external references
- Optional local Orchestrator for schedules and history

Solo is the semantic reference and lowest-friction adoption path. Portal, PostgreSQL, OIDC, HA,
containers, and a network service are not prerequisites for correct execution, quality gates,
lineage, reporting, or useful diagnostics.

### 7.2 Team / SME

**Trust boundary:** Multiple users operating within one organization or team; not mutually hostile
tenants.

**Required shape:**

- Single-node Orchestrator and optional Portal
- Common engine and catalog behavior
- Durable local/shared providers where required
- Shared connections, reports, jobs, schedules, history, notifications, backup, and roles
- Explicit per-object authorization wherever more than one principal exists

Team is a configuration of common providers, not a separate architecture. There must be no Team-only
parser, evaluator, connector, catalog, checkpoint, report, UI, or promotion model.

### 7.3 Enterprise / Corporate

**Trust boundary:** One organization with multiple departments, service owners, security roles, and
production operators.

**Required shape:**

- External identity, groups, service accounts, scoped ownership, and approvals
- Central policy, secret-provider integration, audit, and recovery
- PostgreSQL and shared artifact storage when multi-node HA is enabled
- Lease fencing, leader election, health probes, key-ring coordination, and session affinity where
  required by the current hosting architecture
- Governed promotion, backup/restore, upgrade, and disaster-recovery procedures
- Departmental separation and negative authorization evidence

Enterprise is the operational and self-hosting foundation for SaaS. SaaS services should reuse or
generalize Enterprise provider contracts rather than rebuild identity, policy, catalog, audit,
promotion, and recovery as SaaS-only implementations.

### 7.4 SaaS / Multi-Organization

**Trust boundary:** A platform operator serving mutually untrusted customer organizations.

**Required shape:**

- Server-derived tenant identity across every control-, data-, and observability-plane boundary
- Delegated tenant administration separated from platform infrastructure administration
- Tenant-scoped catalogs, policy, secrets, keys, artifacts, queues, leases, caches, checkpoints,
  results, audit, telemetry, support access, export, and deletion
- Hardened or dedicated execution for tenant workloads according to approved isolation policy
- Fair scheduling, quotas, metering, rollout, and noisy-neighbor containment
- Tenant-controlled portability and a supported exit to self-hosted Enterprise
- Hostile cross-tenant negative certification

SaaS is not merely Enterprise behind a public endpoint. Managed Dedicated may reuse a complete
tenant-specific Enterprise boundary first; Shared SaaS additionally proves that every shared
registry, database, queue, cache, worker, metric, and support surface preserves tenant isolation.

## 8. Provider Substitution

The host selects providers; artifacts do not. Provider contracts must preserve the same logical
behavior while adding the guarantees required by the selected profile.

| Concern | Local provider | Shared/governed provider | SaaS requirement |
| :--- | :--- | :--- | :--- |
| Execution | In-process engine | Managed worker or OCI provider | Hardened/dedicated sandbox provider with tenant-scoped workload identity |
| Catalog/state | Memory/files/SQLite | PostgreSQL or supported durable store | Tenant-partitioned state with isolation and migration evidence |
| Artifacts | Local resolved root | Shared governed artifact root | Tenant-derived prefix/bucket boundary and tenant key context |
| Secrets | Machine/user protected store | External provider with ACL, rotation, and audit | Tenant namespace/key separation; platform operator lacks implicit data authority |
| Connections | Direct connector binding | Governed catalog binding and ACL | Tenant-scoped direct or gateway binding with per-attempt capability |
| Scheduling | Interactive/OS scheduler/local Orchestrator | Durable scheduler with leases and fencing | Tenant-fair admission, tenant queue isolation, quotas, and metering |
| Audit | Local evidence | Durable remote outbox/collector | Tenant-complete audit plus separately scoped platform/support audit |
| Checkpoints | Local protected session files | Governed durable storage | Tenant/session-derived encrypted objects and scoped resume authority |

Provider selection is deployment configuration. A provider-specific option may be required to
operate the host, but it cannot leak into `.etlsql` or `.rptsql` business logic.

## 9. Logical Resource Resolution

### 9.1 Resolution Sequence

The portable artifact references a logical alias. Resolution follows this order:

```text
artifact logical reference
  -> authenticated environment and server-derived tenant/organization context
  -> catalog object and ownership
  -> actor/service-account use grant
  -> target environment binding
  -> policy/capability/capacity validation
  -> short-lived execution handle
  -> physical provider operation
```

Knowledge of an alias or resource identifier never grants access. Each step validates that its
authority agrees with the preceding context.

### 9.2 Connection Binding Kinds

Connection aliases support two architectural binding kinds:

- **Direct binding** — connector type, governed endpoint/options, and `SECRET:name` references for an
  environment where the execution plane is permitted to reach the resource directly.
- **Gateway binding** — connector type plus immutable gateway and resource identities. The physical
  host, port, path, and local credential remain owned by the on-premises gateway resource and are not
  delivered to a SaaS execution sandbox.

A script does not specify route, gateway, physical hostname, or local bypass. Development, test,
production, Managed Dedicated, Shared SaaS, and self-hosted Enterprise may bind the same logical
alias differently after connector compatibility and policy validation.

For example:

```text
logical alias:             sales_prod
tenant catalog binding:    gateway hq-gateway / resource corp-sql-sales
gateway-local destination: MSSQL myserver:1433 / Sales
gateway-local credential:  sales-etl-credential
```

The script knows only `sales_prod`. Tenant administrators own alias mapping and tenant grants;
on-premises gateway administrators own physical resource approval and local credentials; platform
administrators operate infrastructure without inheriting either authority.

### 9.3 Other Binding Classes

The same separation applies beyond database connections:

| Logical reference | Target-owned binding |
| :--- | :--- |
| `SECRET:name` | Provider namespace, secret identity/version, access policy, and rotation ownership |
| Artifact/storage reference | Resolved root, bucket/prefix, encryption context, quota, and retention |
| Path boundary | Canonical allowed root and host/provider ownership |
| Service identity | Target principal, owner, group/role mappings, and maximum capability set |
| Notification target | Target service endpoint, protected credentials, sender policy, and data classification |
| External command/tool alias | Approved immutable artifact/digest, capability profile, isolation tier, and limits |
| Execution tier | Target scheduler/provider class satisfying Local, Standard, Hardened, or Dedicated policy |

Bindings are versioned and auditable. Running attempts capture the binding and policy versions they
were authorized against. Resume and retry reauthorize current authority rather than reviving stale
credentials or reusable capabilities.

## 10. Execution and Checkpoint Contract

All execution providers consume the same conceptual request:

- Server-derived organization/tenant and actor or service identity
- Immutable artifact identity and hash
- Job, run, attempt, and optional session identities
- Policy and binding versions
- Scoped resource capabilities
- CPU, memory, I/O, row, byte, concurrency, and time limits
- Required isolation tier and runtime compatibility
- Optional named-checkpoint reference

The request is a design contract, not permission to expose every field directly to the workload.
Providers translate it into the minimum platform-specific authority needed for the attempt.

### 10.1 Isolation Tiers

| Tier | Boundary | Intended use |
| :--- | :--- | :--- |
| **Local** | Current process and OS identity | Solo and trusted development |
| **Standard** | Ordinary OCI/OS isolation | Trusted Team/Enterprise workloads where policy permits |
| **Hardened** | OCI workload inside a microVM, Hyper-V-isolated container, userspace-kernel sandbox, or independently certified equivalent | Minimum for mutually untrusted shared SaaS workloads |
| **Dedicated** | Tenant-dedicated hardened workers, nodes, or cluster | Regulated, high-assurance, or large-tenant workloads |

OCI is the portable workload package, not by itself the hostile-tenant security boundary.

### 10.2 Attempt Lifecycle

1. Authenticate the caller and derive organization/tenant context.
2. Resolve the immutable artifact and logical bindings.
3. Authorize resource use and admit against policy/capacity.
4. Create a short-lived workload identity and pristine execution boundary.
5. Deliver only the artifact, scoped handles, limits, and optional checkpoint needed by the run.
6. Execute, stage outputs, and record named checkpoints where configured.
7. Commit or reject outputs according to connector-aware outcome semantics.
8. Fence the terminal outcome, revoke authority, and destroy attempt-local writable state.

Scheduled attempts use a fresh sandbox. Generic pristine sandboxes may be pre-booted, but a sandbox
that has received tenant material is never returned to a general pool.

### 10.3 Persistent Sessions

The existing session-retention window is a durable-state policy, not a container lifetime. At a
completed author-declared checkpoint, permitted resumable state is serialized outside the worker.
A replacement worker may resume after reauthorization.

Persisted checkpoint state may include variables, `#temp` schemas and encrypted chunks, lineage
state, logical binding references, and the last completed checkpoint label. It never includes live
sockets, open transactions, child processes, resolved secrets, active leases, or reusable resource
capabilities.

Checkpoint identity, storage key, cache key, and encryption context are derived from server-owned
profile/tenant/session identifiers. Resume fails closed on tenant mismatch, artifact change,
incompatible schema/runtime, expired retention, corrupt content, revoked principal, changed policy,
unavailable binding, or unsafe ambiguous external outcome.

## 11. State Placement and Isolation

| State | Portable? | Solo/Team placement | Enterprise placement | SaaS placement |
| :--- | :---: | :--- | :--- | :--- |
| Scripts/reports/rules | Yes | Source control/local catalog | Governed artifact catalog | Tenant catalog plus immutable artifact storage |
| Catalog definitions | Exportable | SQLite/local store | PostgreSQL/shared store where required | Tenant-partitioned control-plane store |
| Physical endpoints | No; binding requirement only | Local configuration | Governed environment catalog | Tenant direct/gateway binding; no arbitrary workload selection |
| Resolved secrets/keys | No | Protected local provider | External provider/key custody | Tenant-scoped provider/key namespace; never ordinary export |
| Job/report history | Policy-controlled | Local/shared durable store | Governed shared store | Tenant-partitioned evidence store |
| Checkpoints | No migration; locally resumable | Protected session storage | Governed checkpoint storage | Tenant/session-scoped encrypted objects |
| Scratch/spill/cache | No | Process/local bounded storage | Worker-scoped bounded storage | Single-attempt tenant-scoped storage, destroyed after use |
| Audit | Policy-controlled evidence | Local/shared history | Durable outbox/collector | Tenant audit plus separate platform/support audit |
| Leases/in-flight work | No | Local ownership | Database-backed fencing | Tenant-aware attempt ledger and fencing; drain rather than export |

Tenant deletion must cover every SaaS placement, including replicas, object versions, caches,
queues, checkpoints, metrics where customer data is present, and backups according to documented
retention/legal holds. Import success never authorizes automatic source deletion.

## 12. Identity and Administrative Authority

### 12.1 Authority Progression

- **Solo:** the local OS/process identity and source-control ownership are the meaningful boundary.
- **Team:** named users, groups, and object grants become necessary as soon as a second principal can
  act on shared state.
- **Enterprise:** human identity, service identity, ownership, approval, policy, security operations,
  and platform operations are independently assignable.
- **SaaS:** tenant authority and platform infrastructure authority are separate domains. A platform
  administrator cannot implicitly become a tenant user or grant themselves customer-resource access.

### 12.2 Tenant-Managed Resources

Tenant administrators must be able to manage tenant-owned gateways, logical aliases, resource
approvals, grants, service-account ownership, export/import mappings, policy within platform limits,
and revocation without SaaS administrator intervention.

On-premises gateway administrators independently approve physical destinations and local credential
references. Cloud tenant administrators cannot cause a gateway to reach an unregistered destination.
Platform administrators may revoke unsafe platform capabilities or operate service health, but they
cannot create tenant mappings or inspect tenant credentials.

Exceptional support access is tenant-approved where feasible, time-limited, least-privileged,
purpose-bound, and separately audited. It does not become standing tenant authority.

## 13. Promotion and Portability Architecture

Promotion separates stable customer intent from environment authority:

1. **Artifact layer** — preserve exact source, stable identities, hashes, dependencies, and provenance.
2. **Catalog layer** — export eligible jobs, reports, schedules, folders, grants, ownership references,
   lineage, quality, and selected evidence in a versioned format.
3. **Binding layer** — inventory required identities, connections, secrets, gateways, paths, storage,
   notification services, policy, execution tiers, and capacity without carrying protected values.
4. **Activation layer** — import into staging, map target authority, lint and preflight, validate with
   read-only or `WHAT_IF` operations, then explicitly enable schedules and delivery.

The unified tenant/deployment bundle is an inspectable, documented manifest plus ordinary payloads.
It is not a database backup or cloud topology export. It records included, excluded, redacted,
skipped, and failed resources with reasons.

### 13.1 Supported Direction

- Solo → Team → Enterprise by provider substitution and governed promotion
- Solo or Enterprise → SaaS through tenant-scoped import and binding
- SaaS → another ETL-SQL operator/cluster
- SaaS → supported self-hosted Enterprise reference topology
- Larger → smaller profiles with explicit unsupported, flatten, disable, rebind, or omit findings

Another vendor can receive documented open artifacts, manifests, data, lineage, and evidence, but
ETL-SQL does not claim that a different product reproduces ETL-SQL execution or reporting semantics.

### 13.2 Cutover Safety

Preflight is non-mutating. Import is idempotent or staged. Jobs, schedules, subscriptions, alerts,
shares, embeds, and service accounts remain disabled until target validation and authorized cutover.
The source scheduler is fenced before target activation so migration cannot create duplicate owners.
Rollback and the last reversible point are recorded for each transition class.

## 14. Failure and Outcome Semantics

All profiles distinguish:

- Script or data-quality failure
- Authorization or policy denial
- Missing/incompatible environment binding
- Resource/capacity exhaustion
- Connector/provider failure
- Worker or sandbox loss
- Cancellation or deadline
- Ambiguous external write outcome

A stronger profile may add recovery machinery, but it cannot change an ambiguous outcome into a safe
failure. Retries are bounded and operation-aware. External writes use transactions, staging,
idempotency keys, or duplicate-safe design where available. Partial artifacts and checkpoints are
never published as successful terminal output.

HA ownership uses durable leases and fencing rather than node-local locks. Restart, failover, and
resume reauthorize current policy and bindings before work continues.

## 15. Architectural Boundaries for Feature Work

Every new feature must answer:

1. What is the smallest safe profile in which it is useful?
2. Which portable artifact or common semantic contract does it use?
3. Which providers or bindings vary by profile?
4. Where does its durable and ephemeral state live, and who owns every key/row/path?
5. Which actor, service, tenant, and platform authorities can administer and use it?
6. Can it be promoted upward without rewriting business logic?
7. What does down-migration report rather than silently discard?
8. What happens during retry, resume, failover, export, import, and deletion?
9. Which profile/topology and negative tests prove the claim?

Portal-only administration is acceptable when collaboration is intrinsic, but underlying portable
evidence and script semantics must not become Portal-only without an explicit architectural reason.

## 16. Rejected Alternatives

### 16.1 Four Product Forks

Rejected because language, connectors, reporting, fixes, and tests would drift. Profiles use common
contracts and provider substitution instead.

### 16.2 SaaS-Specific Script Syntax

Rejected because scripts would cease to be portable and would encode provider topology. Logical
aliases and target bindings carry the variation.

### 16.3 Ordinary Containers as the Sole SaaS Tenant Boundary

Rejected because a shared kernel alone is not the required hostile-tenant boundary. Shared SaaS uses
hardened or dedicated isolation with defense in depth.

### 16.4 A General-Purpose On-Premises Tunnel

Rejected because it would let cloud workloads select arbitrary destinations and protocols. The
gateway exposes only locally registered typed resources and bounded operations.

### 16.5 SaaS Export as an Afterthought

Rejected because customer exit is part of the architecture. A minimum configuration/artifact bundle
and SaaS-to-self-hosted Enterprise journey are release gates for Managed Dedicated SaaS.

### 16.6 Platform Administrator as Tenant Superuser

Rejected because infrastructure operations and customer data authority are separate. Exceptional
support access is explicit, limited, and audited rather than ambient.

## 17. Open Implementation Decisions

The following choices remain implementation work and do not change the architecture above:

- Exact provider interfaces and project ownership for execution scheduling and tenant storage
- The first supported hardened sandbox technology on each host platform
- The canonical bundle schema versions and signing/encryption algorithms
- Gateway transport selection where restrictive proxies prevent the preferred typed streaming path
- Which evidence/history classes are included in the first portability bundle
- Default quota values, retention periods, and paid/dedicated capacity policies
- The order in which existing catalog/state models acquire provider-neutral and tenant-aware storage

Each choice must preserve the invariants in this document and be recorded in implementation plans or
decision records when it materially constrains future providers.

## 18. Architecture Definition of Done

The deployment-profile architecture is realized when:

1. Common artifacts execute with equivalent semantics through all applicable profile providers.
2. Team is demonstrably a common-provider configuration rather than a code fork.
3. Enterprise supplies the governed identity, state, audit, recovery, HA, and promotion foundation
   required by hosted deployments.
4. Managed Dedicated SaaS proves full tenant lifecycle and exit before Shared SaaS claims density.
5. Shared SaaS proves hostile isolation at every shared state, execution, network, cache, queue,
   telemetry, support, and deletion boundary.
6. Logical resources can be rebound without changing pipeline/report business logic, and unresolved
   authority fails during preflight.
7. A representative tenant moves SaaS → self-hosted Enterprise using published tooling and
   customer-verifiable artifacts.
8. Every supported profile and transition has current commit-bound certification evidence and an
   N → N+1 upgrade or documented recovery result.

## References

- [SaaS Tenant Isolation Architecture](SaaSTenantIsolation.md)
- [Tenant Portability Architecture](TenantPortability.md)
- [Deployment Profile Standards](standards/Deployment_Profile_Standards.md)
- [Deployment Profile and Portability Strategy](roadmaps/Deployment_Profile_Strategy.md)
- [Enterprise Platform Strategy](roadmaps/Enterprise_Platform_Strategy.md)
- [Product Roadmap](../../ROADMAP.md)
- [Administration](../administration/README.md)
- [Source Boundary Migration Plan](roadmaps/Source_Boundary_Migration_Plan.md)
