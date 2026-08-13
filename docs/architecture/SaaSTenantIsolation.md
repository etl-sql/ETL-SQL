# ETL-SQL SaaS Tenant Isolation Architecture

**Status:** Approved target architecture; Managed Dedicated and Shared SaaS implementation and
certification remain incremental

**Applies to:** SaaS control planes, execution fleets, tenant storage, persistent sessions,
on-premises Gateway connectivity, telemetry, support, recovery, and deletion

**Parent architecture:** [Deployment Profile Architecture](DeploymentProfiles.md)

**Implementation sequence:** [Product Roadmap](../../ROADMAP.md)

**Current support evidence:**
[Deployment Profile Standards](standards/Deployment_Profile_Standards.md)

---

## 1. Decision

ETL-SQL SaaS serves mutually untrusted customer organizations. Tenant isolation is therefore an
end-to-end system property, not a database filter, container option, or Portal convention.

SaaS is delivered through two topologies that implement the same logical contracts:

1. **Managed Dedicated SaaS** first: an automated tenant-specific Enterprise-style deployment with
   dedicated database, artifact, key, cache, queue, and worker or hypervisor boundaries.
2. **Shared SaaS** later: shared tenant-aware control planes and fleets with hardened per-run
   execution, fair scheduling, metering, and hostile cross-tenant certification.

Managed Dedicated reduces the number of shared boundaries and supplies the first hosted operating
model. It does not prove Shared SaaS isolation. Shared services are introduced only where demand and
fleet economics justify their security and operational cost, and every new shared boundary remains
unsupported until its negative isolation evidence passes.

OCI containers are the portable workload package. An ordinary shared-kernel container is not, by
itself, the hostile-tenant security boundary. Mutually untrusted SaaS executions require a hardened
or tenant-dedicated boundary plus defense in depth across identity, scheduling, network, storage,
secrets, connections, gateways, observability, support, and lifecycle operations.

## 2. Scope and Relationship to Other Documents

This document owns the stable SaaS isolation design:

- Tenant identity propagation and authorization
- Managed Dedicated and Shared topology boundaries
- Control-plane and execution-plane separation
- Execution scheduling, sandbox lifecycle, and persistent checkpoint placement
- Tenant storage, secrets, keys, queues, caches, audit, and telemetry boundaries
- Secure outbound access through tenant-owned Gateways
- Tenant versus platform administrative authority
- Failure containment and isolation certification

[DeploymentProfiles.md](DeploymentProfiles.md) owns the common four-profile model and provider
substitution. [TenantPortability.md](TenantPortability.md) owns export/import and customer exit.
[Enterprise_Platform_Strategy.md](roadmaps/Enterprise_Platform_Strategy.md) owns the Enterprise
operating foundation reused by hosted deployments. `ROADMAP.md` owns delivery order; `TODO.md` owns
actionable implementation work.

## 3. Security Outcomes

The architecture must provide the following outcomes even when a tenant controls scripts, data,
query values, filenames, report definitions, external tool input, and execution timing:

- A tenant cannot discover, address, read, mutate, delay, resume, export, delete, or infer another
  tenant's resources or activity.
- A compromised workload cannot reach the host, container runtime, cloud metadata service, control
  plane, another workload, an unauthorized destination, or broader tenant authority.
- Platform administration does not implicitly confer tenant-user or tenant-data authority.
- Tenant administrators can manage their own resources without platform impersonation.
- On-premises credentials and physical resource definitions remain under on-premises administrator
  control and are not delivered to SaaS workloads.
- One tenant's CPU, memory, I/O, network, queue, connector, or storage consumption cannot violate
  another tenant's isolation or committed availability.
- Failed, cancelled, retried, resumed, or migrated work cannot publish partial output, duplicate an
  ambiguous write silently, or reuse stale authority.
- Tenant deletion and cryptographic erasure cover every tenant-bearing state class subject to
  documented retention and legal holds.

The design assumes defects may occur in any one layer. Independent checks at the control plane,
scheduler, storage provider, broker, gateway, and execution boundary prevent a single missing
controller predicate from becoming cross-tenant access.

## 4. Trust Domains and Actors

| Domain or actor | Authority | Explicitly lacks |
| :--- | :--- | :--- |
| **Tenant user** | Tenant-granted access to artifacts, reports, jobs, and governed resources | Authority to choose tenant context, physical placement, platform resources, or other tenants |
| **Tenant service account** | Bounded nonhuman authority owned by a tenant principal and capped by tenant/platform policy | Human login, unrestricted delegation, or authority beyond its owner and grants |
| **Tenant administrator** | Tenant catalog, users/groups, aliases, Gateway enrollment, resource approval, grants, policy within platform limits, export/import, and deletion requests | Platform infrastructure administration or local Gateway credentials |
| **On-premises Gateway administrator** | Gateway installation, local resource registration, local credentials, operation allowlists, and local revocation | Tenant catalog grants, SaaS platform administration, or arbitrary cloud control |
| **SaaS platform operator** | Fleet health, rollout, capacity, abuse controls, infrastructure recovery, and platform policy | Ambient tenant impersonation, tenant mappings, local credentials, or tenant-content inspection |
| **Approved support principal** | Time-limited, purpose-bound support capability approved under policy | Standing tenant authority or reusable data-plane credentials |
| **Execution workload** | One attempt's immutable artifact and short-lived scoped resource capabilities | Reusable platform credentials, tenant selection, broad storage roots, arbitrary network, or scheduler authority |

All exceptional authority is explicit, expiring, least-privileged, and audited. “Platform admin” is
not a universal bypass role.

## 5. System Topology

```text
Tenant user/service identity
          |
          v
+-----------------------------+
| SaaS control plane          |
| auth, tenant catalog, ACL,  |
| policy, scheduling intent   |
+--------------+--------------+
               | authorized immutable request
               v
+-----------------------------+       +-------------------------+
| Execution Scheduler         |------>| Tenant-scoped storage   |
| admission, placement,       |       | artifacts/checkpoints/  |
| quotas, leases, outcomes    |       | results/evidence        |
+--------------+--------------+       +-------------------------+
               |
               v
+-----------------------------+       +-------------------------+
| Ephemeral hardened attempt  |------>| Gateway Broker          |
| common ETL-SQL runtime      |       | typed scoped operations |
+-----------------------------+       +------------+------------+
                                                  |
                                      outbound mTLS session
                                                  |
                                      +-----------v-------------+
                                      | On-premises Gateway     |
                                      | local resources/secrets |
                                      +-------------------------+
```

Managed Dedicated may instantiate these responsibilities inside a tenant-specific deployment.
Shared SaaS may share control-plane and scheduler services, but every request, lookup, cache, queue,
lease, object, metric, and support operation remains tenant-scoped below the HTTP/controller layer.

## 6. Tenant Context and Authority Flow

### 6.1 Server-Derived Context

Tenant identity is derived from authenticated server-side authority. It is never accepted from a
script, hostname, query parameter, route value, request body, Gateway name, resource ID, job ID,
object key, queue name, or caller-supplied claim without issuer/audience and tenant-binding
validation.

Every shared entry point resolves:

1. Authenticated principal and issuer
2. Server-owned tenant membership and active status
3. Tenant-scoped object identity and ownership
4. Principal/service-account grant
5. Tenant and platform policy
6. Resource binding and capability limits

The resulting authority is passed forward as an internal immutable context. Downstream services
validate it independently and compare it with their own resource ownership records.

Shared control-plane identifiers use `SharedTenantResourceRegistry`, a provider-neutral durable
registry backed by the Portal's configured SQLite or PostgreSQL store. Alias, Gateway, resource,
run, object, storage, queue, and index names are stored with a composite tenant/kind/logical key and
a server-derived scoped identifier. Reads, enumeration, deletion, and collision handling always
include the request's verified `TenantContext`; numeric IDs and caller-provided scoped IDs cannot
select a tenant. The registry stores namespace ownership only—provider credentials, payloads, and
runtime authority remain in their purpose-specific systems.

Managed Dedicated adoption is implemented on every shipped surface that can name or disclose a
tenant across deployment boundaries:

- `admin promotion saas-onboard` derives a time-limited `PlatformAccessGrant` from the current
  signed organization policy. `--tenant` is only an assertion against that server authority; a
  mismatch, missing policy, or expired authorization fails before staging begins, and authority is
  rechecked before the final directory move.
- A Managed Dedicated Portal records its host-fixed identity in `Portal:TenantId`. Configuration
  export plans include that identity in the acknowledged plan hash, and SaaS bundle composition
  refuses to use a caller-provided identity when the Portal omits or disagrees with it.
- The Portal support bundle derives its tenant label from the same host-fixed configuration and
  records `HostFixed` as the context origin. It has no caller tenant selector. Fleet visibility is
  derived from server configuration and remains read-only.

Current evidence is
[`SaasTenantOnboardingTests`](../../tests/ETL-SQL.Tests/Orchestration/SaasTenantOnboardingTests.cs),
[`TenantBundleComposerTests`](../../tests/ETL-SQL.Tests/Portability/TenantBundleComposerTests.cs),
[`FleetWorkspaceAndExportPlanTests`](../../tests/ETL-SQL.Portal.Tests/FleetWorkspaceAndExportPlanTests.cs),
and [`SupportBundleTests`](../../tests/ETL-SQL.Portal.Tests/SupportBundleTests.cs). Shared tenant
context and namespace evidence is in
[`SharedTenantHttpBoundaryTests`](../../tests/ETL-SQL.Portal.Tests/SharedTenantHttpBoundaryTests.cs)
and [`SharedTenantResourceRegistryTests`](../../tests/ETL-SQL.Portal.Tests/SharedTenantResourceRegistryTests.cs).
These certify their topology-specific tenant-context cells only; neither proves the remaining
Shared storage, execution, Gateway transport, or data-evidence boundaries.

### 6.2 Managed Dedicated identity separation

Managed Dedicated has two deliberately non-interchangeable authority paths:

- A tenant `Admin` is a Portal user inside the host-fixed tenant boundary. Tenant administrators
  manage their own users, provider-backed groups, mappings, and narrowly delegated
  `admin.identity` service accounts. The service-account route allowlist excludes all unrelated
  administration and refuses Admin creation or promotion.
- A platform operator is represented only by a short-lived attributed `PlatformAccessGrant`
  derived from signed organization policy. Onboarding writes a separate `PlatformOperator` audit
  receipt with its approval and expiry. It does not issue a Portal JWT, create a Portal user, or
  receive the tenant `Admin` role, so implicit tenant-user impersonation is not expressible.

Onboarding accepts one tenant-owned OIDC authority/client registration and emits the existing
Enterprise `Portal:Identity:Oidc` contract into the tenant configuration. The authority is restricted
to a credential-free HTTPS issuer. Client secrets never enter the command, manifest, audit receipt,
or generated file; the tenant deployment supplies
`Portal__Identity__Oidc__ClientSecret` out of band before activation.

Evidence is [`SaasTenantOnboardingTests`](../../tests/ETL-SQL.Tests/Orchestration/SaasTenantOnboardingTests.cs),
[`AdminIdentityScopeIntegrationTests`](../../tests/ETL-SQL.Portal.Tests/AdminIdentityScopeIntegrationTests.cs),
[`OidcAuthTests`](../../tests/ETL-SQL.Portal.Tests/OidcAuthTests.cs), and
[`TenantContextTests`](../../tests/ETL-SQL.Tests/Multitenancy/TenantContextTests.cs). This section
certifies Managed Dedicated; the separately implemented Shared identity boundary is certified in
section 6.2.2.

### 6.2.1 Managed Dedicated policy and key authority

When `Portal:TenantId` is configured, the Portal registers a host-fixed `TenantContext` with the
policy authority. The service itself checks every publish, activation, rollback, canary, list, and
retrieval operation against that context; the controller check is defense in depth, not the tenant
boundary. Policy-machine registration, enumeration, revocation, and envelope distribution apply the
same host tenant predicate. A request naming another tenant is refused, including when a stale
foreign machine row already exists in the Dedicated database.

Tenant administrators may author their tenant policy. A principal carrying platform authority
scope remains a platform operator and is explicitly refused at policy mutation endpoints; platform
scope does not imply tenant `Admin` authority or impersonation.

Key-management bindings are constructed under the same host-fixed tenant scope. Provisioning may
validate several Dedicated namespaces with
`KeyMaterialContractValidator.ValidateTenantNamespacesAsync`; it resolves every purpose and rejects
identical material reused across tenant or purpose boundaries. Runtime execution receives the key
provider through host dependency injection and carries neither provider bindings nor resolved
material in its job artifact. Portability exports retain non-secret binding requirements only.

Evidence is [`PolicyAuthorityServiceTests`](../../tests/ETL-SQL.Tests/Core/PolicyAuthorityServiceTests.cs),
[`PolicyAuthorityApiTests`](../../tests/ETL-SQL.Portal.Tests/PolicyAuthorityApiTests.cs),
[`PolicyDistributionApiTests`](../../tests/ETL-SQL.Portal.Tests/PolicyDistributionApiTests.cs),
[`DedicatedPolicyAuthorityGuardTests`](../../tests/ETL-SQL.Portal.Tests/DedicatedPolicyAuthorityGuardTests.cs),
[`KeyMaterialContractTests`](../../tests/ETL-SQL.Tests/Security/KeyMaterialContractTests.cs), and
[`TenantBundleComposerTests`](../../tests/ETL-SQL.Tests/Portability/TenantBundleComposerTests.cs).
This certifies Managed Dedicated only; shared policy stores and shared provider namespaces remain
`NotCertified`.

### 6.2.2 Shared request credential boundary

Shared Portal requests now establish tenant scope after the Portal JWT has passed signature,
issuer, audience, and lifetime validation. The token carries exactly one canonical `tenant_id`
claim minted from an existing `TenantContext`; middleware converts that claim into the scoped
`TenantContext` consumed by stores and policy services below controller code. Missing, duplicate,
or malformed tenant claims return `401` before controller activation. Dedicated tokens carrying a
tenant claim must match the configured host tenant.

Headers, query parameters, route values, aliases, and issuer strings do not participate in this
binding. They remain caller assertions evaluated inside the resulting tenant scope. Platform access
grants cannot mint tenant-user or tenant-service JWTs, preserving the non-impersonation boundary.
[`SharedTenantCredentialBindingTests`](../../tests/ETL-SQL.Portal.Tests/SharedTenantCredentialBindingTests.cs)
cover the claim and impersonation contract, while
[`SharedTenantHttpBoundaryTests`](../../tests/ETL-SQL.Portal.Tests/SharedTenantHttpBoundaryTests.cs)
prove an authenticated request cannot replace its signed tenant with spoofed header, query, or
issuer values and cannot enumerate another tenant's equal shared-store surface.

This shared-credential boundary is the root of the multi-IdP certification. Shared identity also
has an authority-registry boundary: `SharedIdentityAuthorities` stores normalized Portal
hosts, login domains, issuers, client identifiers, and `SECRET:` credential references with globally
unique host/domain routing and tenant-scoped administration. Anonymous resolution accepts an
`HttpRequest` and performs one exact enabled-host lookup; it exposes no tenant, issuer, authority-id,
or login-domain selector. Returned bindings expose only whether a client secret is configured, not
the `SECRET:` reference. Only after OIDC validation does `BindValidatedIssuer` compare the token's
validated issuer with that server-routed binding and create a verified tenant context. Prefix hosts,
disabled rows, cross-tenant authority-id updates, raw client secrets, and issuer mismatches fail.

`SharedOidcFlowStateService` carries the routed choice across the browser redirect in a Data
Protection envelope. The ten-minute envelope pins authority id and version, normalized Portal host,
exact HTTPS redirect URI, state, nonce, and PKCE verifier. Callback restoration has no
`HttpRequest` argument, so a callback Host header or tenant/issuer query cannot select a different
authority. State is compared in constant time, and expiration, tampering, authority rotation, or
authority disablement invalidates the outstanding flow.

The shared identity persistence foundation adds `TenantId` to Portal users, groups, user-group
memberships, service accounts, and refresh tokens, plus the normalized external issuer on federated
users. Username, immutable issuer/subject, group-name, and service-account-name uniqueness is now
tenant qualified in matching SQLite and PostgreSQL migrations; existing rows backfill to the
explicit `portal-host` legacy partition. `SharedIdentityPartitionStore` requires a
verified-credential context and applies it to subject/name lookup, provider-group enumeration,
membership creation, and refresh-session attachment. Foreign numeric user or group identifiers are
refused rather than looked up outside that predicate.

The Shared authorization-code controller now consumes these boundaries end to end. The anonymous
login endpoint resolves only the exact routed Portal host, builds discovery and authorization from
that binding, and protects the authority version with the state/nonce/PKCE flow. The callback
restores that same binding, resolves an optional client credential from the selected tenant's
`SECRET:` partition, validates signature/issuer/audience/lifetime/nonce, compares the validated
issuer to the routed authority, and only then establishes the verified request tenant. User lookup
and creation, mutable profile updates, OIDC-group reconciliation, JWT issuance, and refresh-token
creation then use that tenant partition. The anonymous provider-advertisement endpoint applies the
same exact-host lookup, so an unregistered shared host does not advertise or start SSO.

Refresh and service credentials preserve the partition after initial login. A refresh secret is a
high-entropy credential and may locate its own row, but rotation fails unless that row and its user
have the same canonical tenant. Consumption predicates include the tenant, the successor retains it,
and the replacement JWT is minted from the resulting verified context. A service client binds no
tenant from its public client id: only successful constant-work secret verification followed by an
account/owner tenant match establishes the context. Runtime JWT validation uses the signed tenant
claim to re-read user, service-account, and service-owner security state through tenant-qualified
queries and tenant-qualified cache keys. Session invalidation likewise constrains user stamps,
refresh revocation, and group membership selection to the verified tenant.

Delegated administration completes the boundary. User, group, provider-mapping, membership,
session, service-account, identity-authority, and identity-diagnostics operations derive their
tenant only from the verified request context and apply that predicate to enumeration and mutation.
Foreign numeric identifiers are treated as not found, equal local usernames and resource names may
exist in separate tenants, and membership writes require both endpoints to belong to the current
tenant. Tenant authority administration does not broaden anonymous discovery: login still starts
from one exact server-routed host. Platform grants remain unable to mint tenant-user or
tenant-service credentials.

Evidence is
[`SharedIdentityAuthorityServiceTests`](../../tests/ETL-SQL.Portal.Tests/SharedIdentityAuthorityServiceTests.cs)
and
[`SharedOidcFlowStateServiceTests`](../../tests/ETL-SQL.Portal.Tests/SharedOidcFlowStateServiceTests.cs),
with collision and foreign-id persistence evidence in
[`SharedIdentityPartitionStoreTests`](../../tests/ETL-SQL.Portal.Tests/SharedIdentityPartitionStoreTests.cs),
and routed HTTP callback evidence in
[`SharedOidcAuthTests`](../../tests/ETL-SQL.Portal.Tests/SharedOidcAuthTests.cs). Dynamic discovery
and routed client/issuer selection are pinned by
[`SharedOidcAuthenticationServiceTests`](../../tests/ETL-SQL.Portal.Tests/SharedOidcAuthenticationServiceTests.cs).
Delegated HTTP isolation is pinned by
[`SharedDelegatedIdentityAdminTests`](../../tests/ETL-SQL.Portal.Tests/SharedDelegatedIdentityAdminTests.cs).
Together these tests certify the Shared identity and delegated-administration cell.

### 6.3 Collision Safety

Shared stores must behave safely when two tenants use the same display name, alias, numeric ID,
resource name, report name, job name, or object hash. Tenant scope is part of every uniqueness,
foreign-key, lookup, cache, queue, lease, idempotency, and storage-key decision. Globally unique IDs
do not replace tenant predicates; they only reduce accidental collision.

The first shared governance-store slice applies this rule to organization policy versions,
Portal-managed secrets, shared-connection definitions, ACL bindings, and usage records. Secret names
and connection aliases use composite `(TenantId, Name/Alias)` uniqueness in both SQLite and
PostgreSQL. Service reads, writes, lifecycle operations, exports, ACL changes, and usage updates all
carry the server-derived tenant predicate. `Portal:SharedTenancy:Enabled=true` makes those services
fail during construction unless a verified `TenantContext` has been injected; falling back to the
legacy `portal-host` partition is not allowed in Shared mode.

[`SharedTenantStoreIsolationTests`](../../tests/ETL-SQL.Portal.Tests/SharedTenantStoreIsolationTests.cs)
prove equal policy versions, secret names, connection aliases, and differing key versions coexist
without cross-tenant reads or deletes. Shared host key bindings also require a validated explicit
tenant scope. Equal version names are indexed independently by `(scope, purpose, version)`, while
Dedicated bindings must agree with their host-fixed tenant. Startup validation walks every
configured Shared scope and requires independently resolvable Dataset, Credential, Artifact, and
Checkpoint keys for each. This is pinned by
[`SharedKeyManagementBindingTests`](../../tests/ETL-SQL.Portal.Tests/SharedKeyManagementBindingTests.cs).

Shared identity now injects the verified context into request services. Runtime certification still
uses that context through `DatasetTenantScope`: dataset rows are unique by `(TenantId, Name)`, legacy
rows backfill to `portal-host`, catalog enumeration and mutation are tenant-filtered below
controllers, foreign IDs are not found, and dataset preview/rotation resolves the matching tenant's
Dataset-purpose key. Dependent ACL, report-structure/dependency, lineage-impact, configuration-export,
and access-simulation paths use the same catalog partition. Evidence is
[`SharedDatasetTenantIsolationTests`](../../tests/ETL-SQL.Portal.Tests/SharedDatasetTenantIsolationTests.cs).
Snapshot package operations now require that same explicit scope on Shared hosts and resolve the
tenant's Artifact-purpose key. Interactive and queued report execution pass their verified or
persisted-owner tenant into dataset and checkpoint resolution; the Shared checkpoint factory rejects
missing scope, and Orchestrator execution/resume carries its trusted SaaS tenant through the session
store. Legacy plaintext snapshot migration is skipped on Shared hosts because its artifacts have no
certifiable tenant owner. Equal-version cross-tenant failure is pinned by
[`SnapshotPackageServiceTests`](../../tests/ETL-SQL.Portal.Tests/SnapshotPackageServiceTests.cs) and
[`SqliteSessionMetadataStoreTests`](../../tests/ETL-SQL.Tests/Core/SqliteSessionMetadataStoreTests.cs).

Shared lineage and stewardship now use a separate tenant-qualified contract rather than the legacy
deployment-wide catalog API. Every lineage edge carries `TenantId`; writes and all graph, tag,
missing-metadata, job, source, source-file, and recent-history reads require server-derived
`TenantContext`. Scheduler attempts bind from their immutable signed job identity, queued Portal
work binds from its persisted owner, and request paths bind from the verified credential. Signed
Orchestrator HTTP identity—not a query/header tenant selector—controls the partition. The Portal's
derived governance state is partitioned by the same tenant across settings, scans, findings,
decisions, reviews, badges, resolution categories, and glossary terms. Evidence is
[`LineageCatalogTests`](../../tests/ETL-SQL.Tests/Orchestration/LineageCatalogTests.cs),
[`SharedLineageEndpointTests`](../../tests/ETL-SQL.Portal.Tests/SharedLineageEndpointTests.cs), and
[`SharedStewardshipTenantIsolationTests`](../../tests/ETL-SQL.Portal.Tests/SharedStewardshipTenantIsolationTests.cs).

Shared audit persistence follows the same collision rule. Audit rows and outbox events carry
`TenantId`; equal event identifiers can coexist because uniqueness is composite, and every
tenant-facing audit query, collector-health view, fleet count, and support-bundle outbox count uses
the verified request partition. Fail-closed delivery evaluates only the committing tenant's
backlog, preventing another tenant's collector failure from becoming cross-tenant denial of
service. The host transport may drain all partitions, but it preserves the tenant identifier in
the remote event envelope. Provider migrations backfill legacy rows into the Dedicated
`portal-host` partition. Evidence is
[`SharedAuditTenantIsolationTests`](../../tests/ETL-SQL.Portal.Tests/SharedAuditTenantIsolationTests.cs),
[`AuditOutboxTransportTests`](../../tests/ETL-SQL.Portal.Tests/AuditOutboxTransportTests.cs), and
[`PortalPostgresProviderTests`](../../tests/ETL-SQL.Portal.Tests/PortalPostgresProviderTests.cs).

### 6.4 Short-Lived Capabilities

The control plane issues attempt- or operation-specific capabilities containing only the necessary
tenant, actor/service account, run/attempt, resource, operation class, limits, policy/binding
versions, audience, expiry, and replay protection. The exact serialization is an implementation
decision; the security properties are not.

Capabilities are:

- Audience-restricted to one service or provider
- Bound to one tenant, run/attempt, and resource operation
- Short-lived and non-renewable by the workload
- Integrity-protected and replay-resistant
- Invalid after principal, alias, resource, Gateway, policy, or attempt revocation where required
- Insufficient on their own when downstream resource ownership does not agree

## 7. Control-Plane Isolation

The control plane stores tenant-scoped identity mappings, catalog resources, ownership, ACLs,
policies, schedules, report metadata, connection aliases, Gateway registrations, quotas, and
lifecycle operations.

Shared control-plane storage requires:

- Tenant scope enforced in repository/storage APIs below controllers
- Database constraints or equivalent partition boundaries where practical
- No unscoped “get by ID” path for tenant-owned records
- Tenant-aware migrations, backups, point-in-time recovery, and cache rebuilds
- Tenant-aware job queues, leases, idempotency keys, pagination cursors, and search indexes
- Separate platform records that cannot be joined into tenant data without an authorized audited
  support operation
- Negative tests for identifiers copied between tenants and for missing/altered tenant context

Managed Dedicated can use physically separate stores, but application-level tenant context and
authorization remain present so the same contracts survive progression to Shared SaaS.

Managed Dedicated certification provisions separate Portal and Orchestrator databases for lineage,
quality, scan, audit, identity, and catalog evidence plus tenant-specific artifact roots for caches,
security outbox, quarantine, queues, keys, reports, datasets, and snapshots. The negative evidence deliberately
reuses numeric object IDs in two tenant databases and verifies that catalogs, datasets, snapshots,
subscriptions, share/embed tokens, artifact paths, and configuration exports remain confined to the
host-fixed tenant. Tenant onboarding policy and Portal identity tests separately preserve controlled
ingress and the tenant Admin/Author boundary. These physical-store results do not certify Shared
SaaS: its row predicates, search/graph indexes, caches, outboxes, exports, and worker-facing paths
still require hostile cross-tenant testing against shared services.

## 8. Execution Data Plane

### 8.1 Provider-Neutral Request

Execution scheduling consumes the common request described by
[DeploymentProfiles.md](DeploymentProfiles.md#10-execution-and-checkpoint-contract): server-derived
tenant and principal, immutable artifact hash, run/attempt/session identities, policy and binding
versions, scoped resources, limits, required isolation tier, deadline, and optional checkpoint.

Cloud vendor, cluster, node, runtime, image location, warm-pool identity, and physical tenant storage
credentials are provider concerns and do not appear in portable scripts.

### 8.2 Isolation Tiers

| Tier | Minimum boundary | SaaS use |
| :--- | :--- | :--- |
| **Hardened** | OCI workload inside a microVM, Hyper-V-isolated container, userspace-kernel sandbox, or independently certified equivalent | Default minimum for mutually untrusted tenants in shared fleets |
| **Dedicated** | Tenant-dedicated hardened worker pool, nodes, VM set, or cluster | Managed Dedicated, regulated/high-assurance tenants, or reserved large-tenant capacity |

Local and Standard tiers remain useful in Solo, Team, and trusted Enterprise environments but do
not satisfy the shared hostile-tenant SaaS boundary.

### 8.3 Per-Attempt Lifecycle

1. The control plane authenticates the actor, resolves the tenant and immutable artifact, and
   authorizes logical resources.
2. The scheduler admits work against tenant and fleet capacity, fences an attempt ID, selects a
   compliant provider/tier, and creates short-lived workload identity.
3. A pristine sandbox receives only the exact artifact, scoped handles, limits, runtime contract,
   and optional authorized checkpoint.
4. The engine executes with bounded scratch/spill storage and default-deny network policy.
5. Outputs are staged and committed only through authorized connector/storage operations.
6. The scheduler reconciles terminal or ambiguous outcomes, revokes authority, and destroys the
   sandbox and writable storage.

Scheduled jobs receive a fresh sandbox per attempt. A generic sandbox may be pre-booted, but once
tenant material enters it the sandbox is single-use. Interactive sandboxes may survive briefly only
while bound to the same tenant, authenticated session, artifact, and policy, with a hard lifetime.

The provider-neutral writable-state boundary is `ISandboxWorkspaceProvider`. A scheduler supplies a
server-owned `SandboxAssignmentIdentity` containing verified tenant, run, and fenced attempt identity;
the filesystem implementation returns a cryptographically identified root that has never previously
existed. Its input, scratch, and staged-output directories belong to exactly that assignment. Before
destructive teardown the provider rechecks both root containment and a cryptographic ownership marker,
then deletes without following reparse points and verifies that no writable root remains. Marker loss
or alteration fails closed instead of risking deletion of an unowned path. This contract is not by
itself the Hardened compute boundary. The production `DockerSandboxExecutionProvider` consumes it
only through a digest-pinned image and a registered gVisor/Kata runtime, mounts assignment input
read-only and output writable, and uses bounded tmpfs scratch. Ordinary `runc` and `crun` are rejected
rather than relabeled Hardened. Deployment certification must still execute forced-exit and non-reuse
probes against the actual selected runtime.

`SandboxExecutionCoordinator` binds that workspace lifecycle to the provider-neutral execution
contract. `ISandboxExecutionProvider.PrepareAsync` must return a non-executing attempt with provider,
runtime, image, host-policy, and isolation-tier evidence. The coordinator rejects incomplete or
insufficient evidence before calling `ISandboxAttempt.RunAsync`. Every terminal path then destroys
the runtime before deleting its workspace, including execution exceptions, caller cancellation, and
ambiguous outcomes. `ISandboxAttempt.DestroyAsync` is the provider's assertion that the sandbox is
stopped and mounts are detached. If that assertion fails, the coordinator raises a teardown failure
and retains writable state for fenced reconciliation rather than deleting a possibly live mount.

### 8.4 Sandbox Baseline

- Minimal read-only signed image and locked dependencies
- Non-root workload identity, dropped capabilities, restricted syscalls, no privileged mode
- No host devices, runtime socket, host paths, control-plane credentials, or cloud metadata access
- Default-deny ingress and egress
- Bounded encrypted scratch and spill storage unique to the attempt
- Bounded CPU, memory, processes/threads, I/O, network, rows, bytes, duration, and connector use
- Just-in-time scoped secret/resource authority kept out of checkpoints, environment exports, crash
  dumps, logs, and images
- Destructive cleanup and retained audit of image digest, runtime, isolation tier, host policy, and
  terminal outcome

Tenant-authored native extensions or external command tools, if permitted, require separately
approved immutable artifacts and capability profiles and run only in Hardened or Dedicated isolation.
They are disabled by default in SaaS.

## 9. Persistent Sessions and Checkpoint Resume

A failed job session may remain resumable for the configured retention window—currently up to seven
days by default—without keeping its container alive. Process lifetime and recovery lifetime are
separate.

At each completed top-level named checkpoint, the engine serializes resumable logical state to
tenant-scoped durable storage. A replacement sandbox on another worker rehydrates that state and
resumes from the last completed author-declared label, never an arbitrary instruction pointer.

Conceptual storage ownership is:

```text
tenant/{server-owned-tenant-id}/sessions/{server-owned-session-id}/
  authenticated manifest
  metadata
  encrypted state chunks
  checkpoint outcome
```

The path is illustrative; providers may use database rows or object keys. A workload receives a
scoped handle to one checkpoint, never credentials or a mount covering a tenant root.

Checkpoint state may contain permitted variables, `#temp` schemas and encrypted chunks, lineage
state, logical connection aliases, and the last completed label. It excludes live sockets, open
transactions, processes, leases, resolved secrets, and reusable capabilities.

Every checkpoint records tenant, original run, session, label, artifact hash, engine/checkpoint
schema version, policy and binding versions, key version, timestamps, expiry, and content hashes. It
is authenticated and envelope-encrypted with a per-session data key protected by tenant key
authority.

Resume reauthorizes the principal, policy, logical connections, Gateway resources, and checkpoint
access. It fails closed on mismatch, revocation, expiry, incompatible versions, corrupt content, or
an unsafe ambiguous external write. Retrying from a checkpoint may replay work after the checkpoint;
authors and connectors must use transactions, staging, idempotency, or duplicate-safe operations.

## 10. Tenant Storage, Keys, and Secrets

### 10.1 Storage Scope

Tenant identity is part of every artifact path/object key, checkpoint, result, dataset, report
snapshot, spill object, cache key, queue entry, lineage/quality index, quarantine record, export,
backup, and deletion marker.

Storage access is granted through narrow capabilities. A sandbox cannot enumerate a tenant root or
shared bucket. Path canonicalization, symlink handling, archive extraction, object-prefix validation,
and provider redirects are checked after resolution and before I/O.

The engine contract is `TenantStorageCapability`: a trusted host issues it from `TenantContext`, a
single run identifier, a tenant/run object prefix, and named canonical roots with explicit read/write
grants. Scripts can propose a path or object identifier only as an assertion against that immutable
authority. `IExecutionContext.ResolvePath` applies root containment to file and directory connectors
and file operations; `FileSystemPolicyAuthorizer` applies the exact access grant after canonical and
symlink resolution. Forked evaluator branches retain the same immutable capability. Provider-specific
root allocation, disjoint Dedicated provisioning, and Shared per-attempt volume/prefix assignment are
separate topology obligations built on this contract.

The first Dedicated binding wraps Portal artifact storage in `TenantScopedArtifactStorage` whenever
the host has a fixed `TenantId`. Logical service keys are mapped below that immutable tenant prefix
for local and SMB providers, and enumeration strips the physical prefix before returning results.
Report and general execution receive the host tenant's script/map, dataset/snapshot, and disposable
tenant/run scratch grants; spill uses and cleans the assigned scratch root, and named checkpoints use
the tenant's dedicated session root and key scope. Archive extraction is reauthorized entry by entry
against the same run capability, and dataset preview cache identities include the server-derived
tenant. Dedicated startup enumerates tenant artifact areas and fails visibly if legacy unprefixed or
foreign artifacts remain: operators must migrate or quarantine them rather than silently assigning
ownership or shadowing data after an upgrade.

Shared control-plane storage uses the same provider backend only through tenant views derived from
verified request context or a persisted server-owned work binding. Scripts, maps, snapshots, and key
artifacts receive physical tenant prefixes; dataset caches and decrypted-preview scratch receive
tenant directories. Snapshot storage and Artifact-purpose encryption resolve the same tenant
independently, so defeating either boundary does not select another tenant's object. Published and
generated script paths, report execution roots, checkpoint roots, spill, and run scratch are scoped
to that tenant before I/O. Relative identifiers containing another tenant name remain ordinary
segments below the current prefix, while absolute foreign paths fail containment. Hardened worker
mount non-reuse, destructive teardown, and assignment-residue certification remain part of the open
execution boundary rather than being inferred from these control-plane guarantees. The first
provider-neutral workspace implementation now proves fresh tenant/run/attempt directory allocation,
tamper-evident owned teardown, no reparse-point traversal during deletion, and no ordinary filesystem
residue across successive assignments. Actual Hardened-provider mount behavior and forced-termination
cleanup remain certification requirements.

Dedicated stores reduce collision risk but do not remove application authorization. Shared stores
require negative tests against tenant swaps, equal object names, stale cache entries, backup/restore,
and index rebuild.

### 10.2 Key and Secret Separation

- Resolved secret values and private keys never enter scripts, manifests, execution images,
  checkpoints, logs, or ordinary portability exports.
- Managed Dedicated uses a disjoint tenant provider/key namespace.
- Shared SaaS derives provider namespace and key context from server-owned tenant authority and
  proves tenant/key/version separation.
- Data keys are scoped to their artifact/session purpose and protected by tenant key authority.
- Rotation and revocation are versioned and audited; resume and retry do not revive a revoked value.
- Platform credentials used to operate storage or key infrastructure do not become workload or
  support credentials.

## 11. Secure Outbound Data Gateway

### 11.1 Boundary

The on-premises Gateway allows SaaS to reach approved private databases, file roots, and APIs without
inbound firewall exceptions or a general-purpose tunnel. It is an outbound-connected,
tenant-attested policy enforcement point—not a VPN, SOCKS proxy, raw TCP relay, remote shell, or
cloud-configurable arbitrary host/port forwarder.

Deliver tenant-admin enrollment, resource catalog, local enforcement, and typed operations first in
Managed Dedicated. Add a shared Gateway Broker registry and routing plane only with Shared SaaS
certification.

### 11.2 Logical Resource Mapping

Scripts reference governed logical connection aliases; they do not select a Gateway or physical
network address. Within server-derived tenant context, the catalog resolves the alias to either:

- A **direct binding** for a resource the execution plane may reach directly; or
- A **Gateway binding** containing connector type and immutable Gateway/resource IDs, with no
  cloud-side physical endpoint or credential.

Example resolution:

```text
tenant connection alias:  sales_prod
  -> tenant Gateway:       hq-gateway
  -> registered resource:  corp-sql-sales
  -> Gateway-local target: MSSQL myserver:1433 / Sales
  -> Gateway-local secret: sales-etl-credential
```

The script knows only `sales_prod`. Promotion changes the target environment's binding, not the
script. There is no script option that requests Gateway routing or an automatic local bypass.

### 11.3 Administrative Workflow

1. A tenant administrator creates a one-time Gateway enrollment in the Portal.
2. An on-premises administrator installs the Gateway, consumes enrollment once, and establishes a
   unique asymmetric workload identity with short-lived rotated credentials.
3. The on-premises administrator registers typed resources with stable IDs, local credential
   references, allowed operations, and limits. Discovery can propose but never approve a resource.
4. The Gateway publishes bounded non-secret metadata and health to the tenant catalog.
5. A tenant administrator maps an online tenant-owned Gateway resource to a logical alias and grants
   tenant groups or service accounts use. No grant means deny.
6. Runs record tenant, actor/service account, alias, Gateway/resource IDs, operation class, policy
   version, counts, result, and correlation ID without secrets or sensitive payloads.
7. Tenant or on-premises administrators can disable aliases/resources or revoke the Gateway. New
   work fails immediately; cached authority is invalidated and in-flight behavior follows policy.

Platform operators receive aggregate service health but cannot create tenant mappings, approve local
destinations, read local credentials, or grant themselves resource use.

### 11.4 Gateway Runtime

The Gateway:

- Runs as a hardened Windows service or Linux systemd daemon with minimal local identity.
- Initiates outbound-only mutually authenticated TLS over HTTPS-compatible transport.
- Resolves local credentials and executes bounded typed connector operations.
- Enforces immutable resource ID, connector/operation type, database/catalog or canonical path,
  API origin, concurrency, row, byte, and time policy.
- Revalidates DNS and canonical paths at operation time to block rebinding and traversal.
- Never evaluates ETL-SQL scripts or accepts arbitrary socket, shell, path, or protocol forwarding.

The shared Gateway Broker, when introduced, is a dedicated data-plane service. It authenticates
Gateway sessions, maintains a tenant-scoped registry, routes typed streams, meters traffic, applies
backpressure, and isolates queues, buffers, caches, temporary state, retry ledgers, logs, traces, and
metrics by tenant and operation.

Routing occurs only when execution tenant, capability tenant, Gateway identity tenant, catalog
binding, resource ownership, actor grant, and policy version agree. Containers receive a typed
operation handle, never reusable tunnel authority.

### 11.5 Typed Operation Protocol

Prefer bidirectional gRPC streaming over HTTPS, with a typed WebSocket transport only where required
by restrictive proxies. Both implement one versioned operation model.

- Database operations carry bounded connector-specific query/parameter requests and typed row batches.
- File operations address registered roots/resources and stream bounded content.
- API operations address registered origins and allowed operation shapes.
- Deadlines, cancellation, bounded buffering, flow control, maximum request/response sizes, and
  concurrency limits are mandatory.
- Reconnect uses operation IDs and a durable outcome ledger. Ambiguous writes are never retried
  blindly or reported as safely failed.

## 12. Network Egress

SaaS workloads have default-deny ingress and egress. Policy may authorize only the exact connector,
object-storage, telemetry, control-plane, or Gateway Broker destinations required by the attempt.

Controls block:

- Cloud metadata and instance identity services
- Hosting control-plane and internal management networks
- Container runtime and node services
- Other tenant/service discovery ranges
- Unregistered hosts, ports, protocols, redirects, and alternate address forms
- DNS rebinding and policy changes that would widen an existing run silently

Network allowlisting supplements resource authorization; reachability alone never grants connector
or Gateway access.

## 13. Capacity and Noisy-Neighbor Boundary

Admission and runtime enforcement cover tenant and global limits for:

- Queued/running jobs and interactive sessions
- CPU, memory, processes, threads, and execution time
- Scratch/spill bytes, storage IOPS, and retained artifacts
- Network bytes and Gateway throughput
- Rows, result bytes, and connector concurrency
- Queue depth, retry rate, and support/export operations

Scheduling provides fair or weighted admission plus global reserve capacity. One tenant exhausting
its allocation cannot starve, evict, or degrade another tenant's committed capacity. Dedicated
placement never silently falls back to shared placement, and Hardened requirements never silently
fall back to Standard isolation.

The first provider-neutral admission implementation is `FairShareSandboxAdmissionController`.
Server-resolved policy selects an exact capacity pool, tenant weight, concurrent maximum, and queue
maximum. Unknown pools fail closed instead of borrowing from another isolation or service tier.
Within each pool, bounded weighted round-robin prevents an unbounded tenant queue from monopolizing
newly available slots, while per-tenant concurrency limits allow another tenant to use remaining pool
capacity. Cancellation removes queued demand and queue limits apply immediate backpressure. The
execution coordinator releases a reservation only after the runtime is proven detached; uncertain
teardown retains the reservation and exposes its admission ID for explicit provider reconciliation.
This implementation is process-local. Durable HA queue ordering, leases, fencing, and restart
reconciliation are represented by `RelationalSandboxAdmissionLedger` through the established
SQLite/PostgreSQL Orchestrator dialect. It persists tenant/pool policy, FIFO sequence, cancellation,
active owner/expiry, monotonic fence token, terminal state, and retained reconciliation reason.
Only one node can activate a queued admission, and every renew/complete/retain mutation carries owner
and fence. Lease expiry moves an active attempt to `Retained`; it never means that capacity or storage
is safe to reuse. Pool and tenant capacity counters change in the same relational transaction as
activation and terminal release. Therefore two nodes claiming different admissions cannot each
observe and consume the same last slot, and retained attempts continue occupying both counters until
fenced reconciliation. Queue order and active/retained reservations can be rebuilt after restart.
A real PostgreSQL/Testcontainers scenario pins two-node final-slot contention, retained capacity, and
fresh-ledger recovery on the HA provider. `LedgerBackedSandboxAdmissionController` writes queue intent
before local weighted waiting, requires fenced relational activation before returning an admission,
and heartbeats that lease for the attempt lifetime. Lease renewal loss cancels the same execution token
the coordinator passes to workspace preparation and runtime execution. Normal completion is committed
durably before local capacity is returned; explicit reconciliation releases both durable and local
reservations. Weighted choice is still process-local. Cluster-wide weighted selection and
scheduler-owned queued-work dispatch after process loss remain open scheduler work.

`SandboxAdmissionReconciliationService` performs the expiry pass without treating time as teardown
proof. It first moves expired active leases to retained state, then asks the environment-owned
`ISandboxRuntimeReconciler` to locate the provider runtime by admission identity. Capacity is released
only for `Detached`. `Running`, `Unknown`, provider exceptions, and fence conflicts remain retained and
continue consuming pool/tenant counters for a later pass. This keeps an unavailable control plane or
ambiguous runtime from becoming accidental overcommit.

After scheduler-process loss, `LedgerBackedSandboxAdmissionController.ResumeQueuedAsync` may rebuild
local fair waiting only from a current `Queued` ledger row. It rechecks server-derived tenant, durable
sequence, pool, weight, and queue/concurrency limits, then adopts the same admission ID and obtains a
new fenced activation. It rejects active, retained, completed, and cancelled rows rather than turning
old authority into new work. Recovery cancellation does not cancel the durable row; another node may
resume it later. Resolving the admission ID back to immutable scheduler workload metadata and hosting
the recovery loop remain integration work.

Scheduled jobs persist their requested execution class in the existing `JobDefinition.Options` JSON
as the single `SandboxProfile` name. `SandboxWorkloadPolicyResolver` treats that value only as a
request: it resolves the verified tenant against a server-owned policy catalog, checks that the named
profile is entitled, and returns the catalog's physical pool, required isolation tier, resource
limits, tenant weight, and queue/concurrency ceilings. Jobs cannot supply those authoritative values
directly. Unknown tenants or profiles, unentitled profiles, malformed option JSON, non-string profile
values, and case-ambiguous duplicate profile keys fail closed. `SandboxScheduledJobExecutor` connects
this resolved policy to normal scheduler execution. It stores the script in a content-addressed
append-only artifact store, creates server-owned run/attempt identity, and passes named-checkpoint and
variable-override intent as typed fields rather than raw provider arguments. Recovery of an orphaned
queued admission after total scheduler-process loss still requires a durable
admission-to-workload dispatch record.

`JobDefinition.TenantId` is the immutable scheduler tenant binding. In shared deployments the Portal
derives it from the already-validated request `TenantContext` and includes it in the short-lived,
HMAC-signed Orchestrator identity assertion; a fixed `Orchestrator:TenantId` supplies Dedicated host
authority and must match any signed tenant. REST job creation, script-first `CREATE JOB`, and
tenant-scoped subscription job generation persist the canonical value. Store upserts may bind a
legacy null row once but cannot replace a non-null tenant, and optimistic updates cannot cross the
binding. `SandboxWorkloadPolicyResolver` rejects legacy/unbound jobs and requires the persisted tenant
to match the server-derived execution context before resolving any profile or capacity authority.

Long-running Orchestrator hosts call `AddSandboxAdmissionHosting`. The feature is opt-in under
`Orchestration:SandboxAdmission`; when enabled it uses the same configured SQLite/PostgreSQL authority
as the job store, registers `LedgerBackedSandboxAdmissionController`, and runs retained-admission
reconciliation on the configured interval. Pool capacities and all intervals must be positive. The
environment must register `ISandboxRuntimeReconciler`; omission is a startup dependency failure, not
an implicit `Detached` result. `AddHardenedSandboxExecution` supplies the Docker runtime/reconciler
binding when `Orchestration:SandboxExecution:Enabled=true`. It also replaces scheduled dispatch with
the sandbox seam; existing in-process and process-spawn paths are never silently reclassified.
Startup requires durable admission, an entitled profile/tenant catalog, absolute artifact,
workspace, session, and key roots, a digest-pinned image, an allowlisted runtime, and matching fixed
tenant/pool authority for Dedicated profiles. Each tenant has a distinct mounted machine-key file so
encrypted checkpoints survive a disposable worker without placing key material in arguments or
environment values.

## 14. Observability, Audit, and Support

Logs, traces, metrics, profiles, health responses, diagnostics, billing records, and support bundles
are potential data-exfiltration paths and carry explicit tenant/data classification.

- Tenant views contain only tenant-scoped operational evidence.
- Platform views default to aggregate infrastructure health and do not expose scripts, parameters,
  connection targets, filenames, row values, or tenant secrets.
- Shared metric labels avoid unbounded tenant-controlled values and content-bearing dimensions.
- Raw support access is separately authorized, purpose-bound, time-limited, redacted where possible,
  and audited as data access.
- Tenant-complete audit and platform-operator audit are separate but correlatable by non-secret
  operation IDs.
- Metering uses tenant-scoped counts and resource measures; it never becomes execution authority or
  a payload inspection service.

Managed Dedicated scheduled work records one idempotent usage row per tenant-bound job-history
attempt in the Orchestrator's provider-neutral state store. Attribution comes from the immutable
`JobDefinition.TenantId`, not a request parameter or a script value. The row contains workload kind,
terminal status, row count, peak memory, CPU seconds, duration, and recording time; it deliberately
contains no script, parameters, connector destination, filenames, row values, or secrets. A ledger
write failure is operationally visible but cannot fail, retry, or otherwise change the completed
workload.

Shared-fleet adoption starts with the separate `RelationalTenantMeteringLedger`. Its append and query
operations require a host-fixed or verified-credential `TenantContext`; the fixed event payload has
no tenant selector and the table keys idempotency by tenant, source, and opaque source event. Typed
enums classify source, workload, connector class, and status. Numeric fields cover rows, read/write
bytes, sandbox CPU/peak-memory/I/O, Gateway traffic, storage, concurrency, and duration. There is no
field for scripts, parameters, connector targets, resource/object names, row samples, secrets, or an
authorization decision, and no execution policy consumes the ledger. Scheduled sandbox attempts now
append rows/CPU/memory/spill-I/O evidence; the CLI completion envelope carries process peak-memory and
CPU across the OCI boundary. A metering outage is logged after execution and cannot alter its result.
Gateway traffic, storage sampling, and connector-class producers remain required before Shared SaaS
can claim complete metering support.

## 15. Availability, Upgrade, and Recovery

- Attempts use monotonic identities, durable leases, fencing, cancellation, deadlines, and a durable
  outcome ledger so restarts cannot create two owners.
- Fleet upgrades verify image/runtime/checkpoint compatibility, drain workloads, and retain rollback.
- Gateway upgrades are signed, version-windowed, staged, and drain before identity or protocol change.
- Backup and restore preserve tenant boundaries. A restore, point-in-time recovery, or index rebuild
  cannot introduce another tenant's rows, keys, queues, or resumable sessions.
- Managed Dedicated lifecycle includes automated provisioning, upgrade, fence/drain, support,
  portability export, deletion, and recovery without manual platform database edits.
- Shared SaaS rollout is tenant-aware and cannot violate Dedicated reservations or isolation tiers.

Managed Dedicated recovery composes the existing split-custody backup format with the provisioned
tenant boundary. The boundary manifest and host-fixed configuration must name the same canonical
tenant; both archive halves repeat that identity, and the recovery environment must supply the same
expected tenant before validation succeeds. Backup rejects explicit foreign tenant bindings and
captures the configured databases, artifacts, data-protection key ring, and stripped secrets rather
than assuming single-node default paths. Restore never merges into a non-empty target. Before the
recovered deployment can be activated, jobs are disabled and lease fences are advanced; queued
sandbox admissions are cancelled, while formerly active attempts are retained for environment-owned
runtime reconciliation. This physical-boundary recovery contract does not certify recovery from
Shared stores.

Managed Dedicated upgrades use a separately signed target-and-capacity authorization. The asserted
target must equal the running upgrade binary or the host-fixed managed release identity, preventing
an operator from recording an arbitrary version as active. The tenant boundary is exclusively
locked; schedules are disabled and queued admissions cancelled before cutover. Active attempts must
complete, and retained attempts must receive explicit provider reconciliation, before the command
updates the tenant manifest and runtime configuration. Concurrent-job, storage, report-session, and
Dedicated-pool capacity move with the release assignment. Exact pre-cutover configuration and
manifest files are retained for rollback, partial mutation restores them and resumes only previously
enabled jobs, and an interrupted `Cutover` receipt is recovered before retry. This is a single-tenant
deployment lifecycle; rollout and drain across a fleet of Dedicated stacks remains the separate HA
contract.

Managed Dedicated deletion is a separately signed deployment-plane operation. Its policy grant names
one canonical tenant, platform actor, approval reference, reason, short expiry, retention boundary,
and affirmative legal-hold clearance. The operator-facing tenant argument is only a mismatch
assertion. Execution requires an explicit flag, verifies both provisioned identity files, refuses
filesystem roots and reparse points, inventories a counts-and-hashes-only boundary digest, and moves
the boundary out of service before erasing it. A Started record is durable outside the boundary
before mutation and becomes Completed only after erasure; interruption leaves that record plus a
uniquely named tombstone for reconciliation and never puts partially deleted state back into service.

Shared tenant lifecycle uses a durable two-control-plane saga. A separate platform-management key
authenticates the Portal host caller but grants no tenant API access; every provision, upgrade, or
delete still requires a short-lived signed organization-policy grant naming the canonical tenant,
platform operator, approval reference, and reason. Shared provisioning also requires the signed
Portal host, login domain, OIDC issuer/client, and `SECRET:name` client-credential reference, avoiding
an unauthenticated tenant or a manual bootstrap database edit. Initial release and capacity come from
server-owned configuration. Upgrade release and capacity come from the signed upgrade grant.

Portal persists an idempotent operation receipt and moves the tenant out of `Active` before calling
Orchestrator. Authenticated requests then fail with a lifecycle fence. Portal waits for persisted
interactive/report work; Orchestrator disables only that tenant's schedules, atomically refuses stale
lease acquisition, cancels its queued admissions, and waits for active or retained sandbox authority.
Unavailability or an ambiguous response leaves the tenant fenced and the exact authorization
reference replayable. Completion restores only jobs fenced by that upgrade. Deletion applies the same
drain, then removes the tenant's Portal catalog/identity/policy partitions and Orchestrator jobs,
history, usage, and terminal admissions while retaining lifecycle/audit tombstones outside the erased
partition. Retention and legal-hold checks occur before the first mutation. SQLite and PostgreSQL use
the same state machine and composite tenant predicates.

## 16. Failure and Outcome Semantics

The system distinguishes script failure, policy denial, quota exhaustion, connector failure,
sandbox/runtime failure, lost worker, cancellation, Gateway disconnect, and ambiguous external
outcome. These states are durable and tenant-scoped.

Successful publication uses staged or transactional commit where supported. Sandbox death cannot
publish an incomplete artifact or checkpoint. Retry is bounded and operation-aware. For an ambiguous
write, the system reconciles with connector/Gateway outcome evidence or requires operator action; it
does not assume failure and issue a duplicate write.

## 17. Certification Contract

### 17.1 Cross-Tenant Isolation

Negative tests attempt to cross tenant boundaries through:

- Authentication, object IDs, aliases, pagination cursors, and caller-supplied tenant values
- Database records, migrations, backups, restores, and search/index rebuild
- Artifacts, paths, object prefixes, archives, symlinks, caches, checkpoints, spill, and results
- Queues, schedules, leases, attempts, retries, notifications, and idempotency ledgers
- Secrets, keys, connections, Gateways, operation capabilities, and network destinations
- Reports, datasets, snapshots, embeds, shares, subscriptions, lineage, quality, and quarantine
- Logs, metrics, traces, billing, diagnostics, health, exports, deletion, and support tooling

Known foreign tenant IDs, equal logical/numeric IDs, stale capabilities, malformed authority,
revocation, races, and restore/failover paths must fail safely without confirming foreign existence.

### 17.2 Sandbox and Residue

Tests prove a hostile workload cannot reach the host/runtime/metadata/control plane or another
workload, and that later assignments cannot observe filesystem, memory, environment, DNS, network,
cache, or metadata residue. Forced termination, node loss, cancellation, and resume leave no reusable
tenant authority.

### 17.3 Gateway

Tests cover mismatched Gateway tenant identity, known foreign Gateway/resource IDs, capability
alteration/replay/expiry/revocation, arbitrary destinations, DNS rebinding, path traversal, symlink
escape, unregistered databases/shares/origins, limit overruns, disconnect/reconnect, broker restart,
Gateway restart, failover, and ambiguous writes.

### 17.4 Capacity and Operations

CPU, memory, I/O, storage, network, queue, connector, Gateway, export, and support exhaustion by one
tenant remains within quota. Upgrade, restore, support, and deletion operations preserve isolation.

Certification evidence records source commit, exact topology, provider/runtime versions, isolation
tier, scenario IDs, negative outcomes, cleanup, and artifact references. Managed Dedicated and Shared
results are reported separately.

## 18. Rejected Alternatives

- **Enterprise with a tenant column is enough** — rejected because queues, caches, storage, keys,
  workers, telemetry, support, restore, and deletion also cross tenant boundaries.
- **Ordinary containers are sufficient** — rejected as the sole hostile-tenant boundary because they
  share the host kernel.
- **Gateway as VPN or raw relay** — rejected because workloads could choose arbitrary destinations
  and protocols.
- **Platform admin as tenant superuser** — rejected because infrastructure and customer-data
  authority are separate.
- **Container-local seven-day sessions** — rejected because it preserves compromised processes,
  wastes capacity, and prevents portable resumption. Checkpoints are durable tenant artifacts.
- **Client-side tenant filtering** — rejected because presentation is never an authorization boundary.
- **Shared SaaS claim inferred from Dedicated evidence** — rejected because the shared attack surface
  is materially different.

## 19. Open Implementation Decisions

- The first hardened sandbox provider for each supported host platform
- Execution Scheduler service/project ownership and provider API shape
- Shared storage partitioning choices by state class
- Capability serialization, signing, rotation, and revocation mechanism
- Preferred Gateway protocol implementation and compatibility window
- Default quota, retention, support-approval, and in-flight revocation policies
- Exact automated deletion and backup-expiry mechanisms by provider
- Which Dedicated services become shared first after demand evidence

These decisions may change providers or operations but cannot weaken the invariants above.

## 20. Definition of Done

The SaaS tenant-isolation architecture is realized when:

1. Managed Dedicated completes tenant provisioning, delegated administration, Gateway connectivity,
   execution, checkpoint resume, reporting, support, metering, recovery, export, and deletion with
   current topology-specific evidence.
2. Platform administrators cannot implicitly impersonate tenant users or acquire tenant resource
   authority.
3. Tenant administrators can manage tenant-owned mappings and grants while on-premises administrators
   retain physical destination and credential control.
4. Failed persistent jobs resume in a different sandbox from an authorized named checkpoint without
   retaining the failed process or stale secrets.
5. Shared SaaS passes hostile cross-tenant certification across every shared state, execution,
   network, Gateway, observability, support, restore, capacity, and deletion boundary.
6. One tenant's resource exhaustion remains within its quota and cannot violate another tenant's
   committed isolation or placement.
7. Release claims name Managed Dedicated or Shared explicitly and link current commit-bound evidence.

## References

- [Deployment Profile Architecture](DeploymentProfiles.md)
- [Tenant Portability Architecture](TenantPortability.md)
- [Deployment Profile Standards](standards/Deployment_Profile_Standards.md)
- [Enterprise Platform Strategy](roadmaps/Enterprise_Platform_Strategy.md)
- [Product Roadmap](../../ROADMAP.md)
- [Administration](../administration/README.md)
