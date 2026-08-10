# ETL-SQL Tenant Portability Architecture

**Status:** Approved target architecture; minimum bundle and migration journeys remain incremental

**Applies to:** Deployment promotion, SaaS onboarding, SaaS-to-SaaS migration, SaaS-to-self-hosted
Enterprise exit, profile down-migration, tenant export/import, cutover, rollback, and source deletion

**Parent architecture:** [Deployment Profile Architecture](DeploymentProfiles.md)

**Isolation requirements:** [SaaS Tenant Isolation Architecture](SaaSTenantIsolation.md)

**Implementation sequence:** [Product Roadmap](../../ROADMAP.md)

---

## 1. Decision

Customers can enter or leave ETL-SQL SaaS without rewriting their pipeline or report business logic
and without depending on provider-owned infrastructure. ETL-SQL supplies one open, versioned,
inspectable portability bundle and one inventory/preflight/bind/import/validate/cutover workflow for
profile promotion and tenant migration.

The guarantee is **full-fidelity migration of portable customer-owned artifacts and eligible tenant
metadata**, with explicit rebinding of environment-owned identities, resources, secrets, keys,
paths, providers, and infrastructure.

The guarantee is intentionally not “zero-loss.” Resolved secrets, private keys, reusable
capabilities, active sessions, checkpoints, leases, caches, open transactions, and in-flight
operations are security or runtime state—not portable tenant ownership. They are re-created,
rebound, drained, or deliberately excluded.

The minimum configuration/artifact bundle and a certified SaaS → self-hosted Enterprise journey are
release gates for Managed Dedicated SaaS. Customer exit is an architectural requirement, not a late
Shared-SaaS enhancement.

## 2. Scope and Document Authority

This document owns:

- Portable, exportable, environment-bound, protected, operational, and ephemeral state classes
- The unified bundle format and manifest responsibilities
- Included and excluded customer state
- Export/import authorization and tenant control
- Identity, ownership, connection, Gateway, path, policy, and service rebinding
- Version compatibility, staging, activation, cutover, rollback, and deletion
- SaaS-to-SaaS and SaaS-to-self-hosted Enterprise migration
- Open-format export boundaries for moving to a different vendor
- Portability security and migration certification

[DeploymentProfiles.md](DeploymentProfiles.md) owns the common profile and provider architecture.
[SaaSTenantIsolation.md](SaaSTenantIsolation.md) owns tenant isolation and Gateway execution.
[Deployment_Profile_Strategy.md](roadmaps/Deployment_Profile_Strategy.md) owns adoption journeys and
certification strategy. `ROADMAP.md` owns delivery order and `TODO.md` owns actionable work.

## 3. Portability Contract

### 3.1 Supported Journeys

- **ETL-SQL SaaS → another ETL-SQL operator or cluster** — portable artifacts and eligible metadata
  move through the same bundle, subject to target version, connector, capacity, feature, and policy
  compatibility.
- **ETL-SQL SaaS → self-hosted Enterprise** — the same full-fidelity contract targets supported
  self-hosted Portal, Orchestrator, execution, database, storage, secret, identity, and Gateway
  providers rather than a SaaS-only representation.
- **Solo/Team/Enterprise → SaaS** — customer artifacts are imported into a tenant, then identities,
  connections, resources, and policy are explicitly mapped before activation.
- **SaaS or Enterprise → Solo/Team** — preserve state meaningful at the smaller profile and report
  features that require rebinding, flattening, disabling, omission, or a stronger target.
- **ETL-SQL → another vendor** — export scripts, manifests, data, lineage, and evidence in documented
  formats. ETL-SQL does not claim another product will reproduce ETL-SQL language, scheduler,
  governance, lineage, or report semantics.

### 3.2 What Moves Unchanged

Where supported by the target, these retain exact content and logical semantics:

- `.etlsql` and `.rptsql` source
- Data-quality rules and `ASSERT JOB` gates
- Tags and lineage declarations
- Declarative jobs, reports, schedules, and notification definitions
- Logical resource and `SECRET:name` references
- Stable resource identities, dependency relationships, and provenance
- Policy-controlled historical evidence selected for export

Target-specific configuration may be added around an artifact. The artifact's business logic is not
rewritten merely because the host, provider, topology, or isolation tier changed.

### 3.3 Honest Compatibility

Portability does not guarantee that every target has the same connectors, capacity, external
services, identity provider, policy allowances, or execution tiers. Target preflight reports:

- Compatible and ready resources
- Required mappings or credentials
- Name or stable-ID collisions
- Unsupported connector/features or versions
- Insufficient capacity or isolation
- Target policy denials
- Resources that must remain disabled, be flattened, be omitted, or move to a stronger profile

It is forbidden to weaken target policy, silently substitute a destination, drop a resource, or
rewrite a script to make a migration appear successful.

## 4. State Classification

Every inventory item is classified before export:

| State class | Examples | Treatment |
| :--- | :--- | :--- |
| **Portable source artifact** | Scripts, reports, rules, tags, declarative definitions | Preserve exact content, stable identity, hash, dependencies, and provenance |
| **Exportable catalog state** | Folders, jobs, schedules, reports, ACL definitions, ownership references, connection aliases | Versioned representation with validation and collision reporting |
| **Environment binding** | Hostnames, paths, secret-provider references, identity subjects, Gateway resources, notification endpoints | Export mapping requirement and non-secret metadata; target tenant supplies authority |
| **Protected material** | Passwords, tokens, private/signing keys, resolved secrets, KMS keys, Gateway private identity | Never ordinary export; rebind or transfer only through separately approved protected mechanisms |
| **Operational evidence** | Job history, lineage, quality, audit, stewardship, snapshots, datasets | Optional and policy-controlled; preserve original provenance/timestamps and classification |
| **Ephemeral/runtime state** | Sessions, checkpoints, leases, caches, spill, warm sandboxes, open transactions, in-flight work | Drain, expire, delete, or reconstruct; never import as durable ownership |
| **Provider-owned state** | Fleet topology, billing internals, worker credentials, platform support/abuse records | Excluded from customer bundle |

The classification is recorded in the manifest. Every skipped, redacted, failed, or excluded item has
a machine-readable reason and a tenant-readable explanation.

## 5. One Unified Bundle

ETL-SQL extends and unifies existing Portal configuration export, Orchestrator promotion packages,
portable source artifacts, and optional historical evidence. It does not create a competing package
format for each profile or topology.

The bundle is a documented directory/archive format containing:

```text
bundle-root/
  manifest.json
  signatures/
  artifacts/
  catalog/
  evidence/          (optional)
  content-index/     (optional)
  chunks/            (optional or external companion store)
  reports/
```

This layout is conceptual; the published schema owns exact names. Payloads remain ordinary,
inspectable formats where practical. The bundle is not an opaque database backup and does not carry
cloud deployment objects.

### 5.1 Manifest Responsibilities

The canonical manifest records:

- Bundle schema and independently versioned component schemas
- Source product version, source deployment profile/topology, tenant export identity, creation time,
  export mode, consistency point, and required target capabilities
- Stable logical ID, resource class, ownership/provenance, dependencies, content type, byte length,
  cryptographic hash, and payload location for every included object
- Included, excluded, skipped, redacted, and failed counts by resource class
- A reason and remediation/mapping requirement for every nonportable or inactive item
- Required identity/group, owner, connection, Gateway resource, path, storage, secret-reference, key,
  policy, connector, notification, and external-service bindings
- Signature metadata, encryption envelope, canonicalization rules, chunk/index information, and any
  base-export reference needed for an incremental package
- Source fencing/cutover state when the bundle is intended for final migration

The manifest is deterministic for the same selected consistency point and export options, excluding
explicitly documented envelope randomness and generation metadata.

### 5.2 Stable Identity and Dependency Graph

Portable resources retain stable logical identities wherever possible. Display names are not
identities and may collide at the target. The manifest carries a dependency graph so preflight can
order mappings and activation and can reject missing or cyclic dependencies where the resource model
does not permit them.

Import never silently assigns a foreign resource to an existing target object merely because their
names match. Collision policy is explicit: preserve, map, create, rename, skip, or fail.

### 5.3 Large Content

Large datasets, snapshots, quarantine content, history, and evidence use content-addressed,
resumable chunks or a companion object archive rather than forcing every migration into one ZIP.
Chunks are independently bounded, hashed, encrypted, and resumable by operation ID and content hash.

Export modes include:

- Configuration/artifacts only
- Configuration plus selected evidence/content
- Full eligible tenant export
- Incremental delta from a declared base consistency point
- Final cutover delta after source drain/fence

The minimum configuration/artifact mode ships first. Large-content and incremental modes may mature
later without weakening the initial customer-exit guarantee.

## 6. Included Customer-Owned State

Subject to export mode and policy, eligible state includes:

- Exact source-controlled scripts, reports, policies, rules, tags, declarative administration
  scripts, templates, and other customer-authored content
- Portal folders, reports, datasets, jobs, schedules, dependencies, notifications, subscriptions,
  saved views, alerts, service-account definitions, groups, ACL definitions, ownership references,
  connection aliases, and Gateway/resource binding references without credentials
- Selected job/statement history, lineage, quality metrics, stewardship workflow, quarantine
  metadata/content, report snapshots, materialized datasets, audit evidence, and tenant artifacts
- Original timestamps, authorship/provenance, stable logical IDs, hashes, and dependency edges
- A secret/reference inventory explaining what the target tenant administrator must provision
- Compatibility/capability requirements needed to interpret and safely activate the resource

Ownership and ACLs are definitions requiring target identity mapping; they are not proof that a
source identity exists or should receive authority at the target.

## 7. Deliberately Excluded State

The bundle excludes:

- Passwords, access/refresh tokens, private keys, signing keys, KMS keys, resolved secret values,
  Gateway private identities, one-time enrollment material, and share/embed bearer capabilities
- Active interactive sessions, persistent execution checkpoints, leases, locks, in-flight jobs,
  temporary/spill files, caches, warm sandboxes, open transactions, and live network connections
- Provider fleet topology, Kubernetes/cloud objects, worker credentials, platform audit/support
  records, billing internals, aggregate telemetry, and abuse controls
- Provider-specific connection pools, queues, load-balancer sessions, host identities, and runtime
  images as customer-owned state
- Another tenant's records or identifiers, including through shared indexes, audit, backups, or
  content-addressed deduplication
- Physical hostnames, paths, identity-provider subjects, and Gateway destinations as executable
  target authority. Only logical references and explicit mapping requirements are portable.

An exclusion is visible in inventory and manifest output. “Unsupported” is never implemented as
silent omission.

## 8. Authorization and Tenant Control

### 8.1 Export Authority

A tenant administrator or explicitly delegated tenant migration principal authorizes export. SaaS
platform administration alone cannot export tenant content, select a recipient, or choose a
migration destination.

Export authorization is rechecked for inventory, build, download, resume, final delta, and source
deletion. Large exports use expiring operation/download capabilities scoped to one tenant, export,
recipient, and content set.

### 8.2 Import Authority

A target tenant administrator authorizes import, mappings, conflict decisions, validation, and
activation. Imported authority is capped by target platform policy and the importing principal's
delegation.

Platform operators may provide infrastructure and aggregate health but cannot approve target
identity mappings, supply tenant credentials, or activate customer schedules implicitly.

### 8.3 Audit

Inventory, export request, content read, package build, verification, download, import, mapping,
validation, cutover, rollback, and deletion are audited with tenant, actor, operation, resource
counts, hashes, result, and correlation ID. Audit excludes secrets and protected payload content.

## 9. Export Workflow

1. **Authorize and inventory** — Classify resources, estimate size/time, resolve dependencies, check
   policy, and produce included/excluded/mapping findings without mutation.
2. **Select scope and recipient** — The tenant chooses export mode, evidence/content classes,
   consistency requirements, and tenant-controlled recipient encryption.
3. **Establish consistency** — Capture a declared database/artifact consistency point. A final
   migration can place tenant mutations and scheduling into explicit drain/fence mode.
4. **Build** — Serialize canonical manifests and payloads, preserve stable identities/provenance,
   chunk large content, and record every exception.
5. **Scan and isolate** — Detect seeded/raw secrets, provider credentials, capabilities, private keys,
   cross-tenant references, traversal paths, malformed graph edges, and prohibited content.
6. **Sign and encrypt** — Sign the complete canonical manifest and encrypt payloads to a
   tenant-selected recipient or customer-controlled export key.
7. **Verify before success** — Reopen, authenticate, decrypt where the verifier has authority, hash,
   inventory, and schema-check the produced package before reporting success.
8. **Deliver and retain evidence** — Issue a bounded tenant download/transfer capability and retain
   non-secret export evidence according to policy.

A configuration export does not pause the tenant. A final migration must declare whether it is a
snapshot plus final delta or a fenced consistency point; it cannot imply stronger consistency than
it obtained.

## 10. Target Preflight and Binding

Preflight is non-mutating and evaluates:

- Bundle/component schema and product compatibility
- Signatures, encryption recipient, hashes, sizes, paths, content types, and dependency graph
- Required connectors, features, execution tiers, storage, capacity, and policy
- Stable-ID and name collisions
- Identity, group, owner, and service-account mappings
- Logical connection aliases, Gateway resources, secret references, paths, API origins, storage,
  notification services, and external integrations
- Historical/evidence content support and retention compatibility
- Down-migration loss or flattening requirements

The result is a versioned mapping plan. Changes to the bundle, target state, policy, mappings, or
target product version invalidate affected approvals and require revalidation.

### 10.1 Identity and Ownership Mapping

Source identity subjects are not copied as target authority. The target administrator maps users,
groups, service-account owners, and ownership roles to known target principals. Unmapped principals
remain unresolved and resources depending on them remain disabled.

Imported permissions never exceed both the source intent and target administrator's approved
mapping. Group removal, disabled principals, and target policy caps are respected at activation.

### 10.2 Resource Mapping

The target binds logical aliases and references to target-owned resources. A SaaS Gateway reference
may become a direct Enterprise binding, a different Gateway resource, or remain unresolved. The
script does not change.

Resolved secrets are provisioned separately through the target secret provider. Import can verify a
reference exists and is usable through a bounded connectivity check but cannot read or return its
value.

## 11. Import, Validation, and Activation

1. **Authenticate package and authority** — Verify tenant/import authorization, signature, recipient,
   hashes, schema, limits, and target mapping-plan version before mutation.
2. **Create staging namespace** — Import into a transactionally controlled or isolated staging tenant
   area with no production schedules, shares, deliveries, or service identities active.
3. **Apply idempotently** — Preserve stable logical IDs where safe, enforce explicit collision
   choices, and make retries reconcile rather than duplicate resources.
4. **Bind environment authority** — Apply approved identity, ownership, connection, Gateway, secret,
   path, storage, policy, notification, and execution-tier mappings.
5. **Validate** — Compare counts/hashes; parse and lint artifacts; resolve non-secret dependencies;
   evaluate target policy; verify ACLs and tenant isolation; run bounded read-only connectivity and
   representative `WHAT_IF` checks.
6. **Produce migration report** — Record imported, mapped, disabled, skipped, failed, and unresolved
   resources with provenance and remediation.
7. **Approve activation** — The tenant administrator selects which jobs, schedules, subscriptions,
   alerts, service accounts, shares, and embeds may become active.

Imported operational objects default disabled. Package possession does not confer execution or
delivery authority.

## 12. Cutover, Rollback, and Source Closure

### 12.1 Cutover

For a live migration:

1. Record the last reversible point and rollback conditions.
2. Drain or fence source mutations and scheduler ownership.
3. Build/import a final delta if the workflow uses incremental transfer.
4. Re-run mapping, policy, count, hash, and representative execution validation.
5. Obtain tenant-admin approval.
6. Activate selected target workloads with fenced ownership.
7. Prove representative pipeline, report, notification, lineage, quality, history, and audit
   continuity without duplicate scheduling.

Cutover does not require the source and target to share infrastructure or credentials.

### 12.2 Rollback

Rollback is defined per transition and consistency model. Before source fencing is released or target
mutations become authoritative, the system records which side owns scheduling and writes. Rollback
must not produce two active schedulers or merge ambiguous external outcomes automatically.

If target activity has produced external effects, rollback may require reconciliation rather than a
simple switch. The migration report states the last automatically reversible point.

### 12.3 Source Deletion

Successful import never deletes the source. Source closure is a separate tenant-authorized workflow
covering:

- Legal and retention holds
- Scheduler and identity disablement
- Export/download retention
- Primary/replica/object-version cleanup
- Checkpoint, cache, queue, and artifact deletion
- Backup expiry and key destruction/cryptographic erasure
- Final tenant-readable deletion evidence

Platform support records and legally retained audit may follow separate published retention, but
must not preserve reusable tenant authority or be represented as customer-restorable state.

## 13. Cryptography and Package Security

- Publish manifest schemas, canonicalization and hashing rules, signature verification, compatibility
  policy, and a reference reader/validator.
- Sign the canonical manifest with a documented verifiable chain. Signature verification occurs
  before import trusts payload metadata.
- Encrypt payloads to a tenant-supplied recipient public key or tenant-controlled export key. A
  package encrypted only to provider-owned KMS is not a usable customer exit artifact.
- Use authenticated encryption and bind chunks to bundle, tenant export identity, resource identity,
  index, length, and hash.
- Enforce bounded archive decompression, entry counts, sizes, path depth, canonical paths,
  content-type allowlists, duplicate-ID handling, graph limits, and temporary-storage quotas.
- Reject tampered, truncated, replayed, expired where policy applies, cross-tenant, oversized,
  traversal-bearing, decompression-bomb, incompatible, or unauthorized packages before activation.
- Never log decrypted payloads, raw secret findings, recipient private material, or sensitive
  report/query content during validation.

The bundle can be retained and verified by a customer after source SaaS access is unavailable.

### 13.1 Decided formats (2026-08-09)

The algorithm and custody choices §20 left open are now fixed. They do not change the policy above.

| Decision | Choice | Why |
| :--- | :--- | :--- |
| Signature and encryption stack | **OpenPGP**, via the `PgpCore` dependency already in `ETL-SQL.Core` | It is the "encrypt to a recipient public key" model this section already describes, it is license-cleared and shipping, and `CREATE PGP KEY PAIR` means a customer may already hold a usable key because the product told them to make one. JOSE was chosen first and reversed: no JOSE library is present anywhere in the repository, so it meant either a new dependency plus a license audit, or hand-writing JWE key agreement — hand-rolled key derivation is the wrong trade in an artifact whose purpose is surviving unopened for years. |
| What is encrypted | Manifest **plaintext and signed**; payloads encrypted | §5 wants ordinary inspectable formats, and preflight must verify structure, hashes, counts, and the dependency graph *without* the recipient key. The validator already refuses resolved secret material in the manifest, so a plaintext manifest carries no secret. |
| When encryption is required | Required when the source is **SaaS**; optional and recorded otherwise | A self-hosted operator moving their own tenant between their own machines should not be forced into recipient-key management. The manifest always records which was used, so "unencrypted" is never ambiguous. |
| Who signs | The **operator** (exporting deployment), with a published key | The signature is an authenticity claim — "this is what we exported for you" — and is separable from the confidentiality claim made by encrypting to the tenant. A customer can verify provenance offline against the published key. |

Signature verification precedes any trust in payload metadata, as above. The operator publishes an
HTTPS OpenPGP keyring plus immutable per-fingerprint public-key artifacts and a lifecycle index;
customers authenticate first use by comparing the fingerprint through an independent release or
tenant-administration channel. Routine keys are prepublished for 30 days, exactly one key signs new
exports, retired public keys remain available for the bundle-retention and compatibility horizon,
and compromised keys trigger export suspension, revocation publication, tenant notification, and
re-export unless independent immutable audit evidence proves pre-compromise signing. The complete
operational contract is [Tenant Portability Signing Keys](../administration/platform/tenant-portability-signing-keys.md).

## 14. Versioning and Compatibility

Bundle and component schemas evolve independently. The manifest declares required capabilities and
minimum/maximum interpretation rules rather than relying only on one product version string.

- Support explicit current and N/N+1 compatibility for the initial contract.
- Unknown required fields/features fail with actionable findings; optional fields can be preserved or
  ignored only according to published rules.
- Migrations/upgrades operate on staging copies and never mutate the only source bundle.
- Import records the source and target versions and any canonical transformations applied to catalog
  representation. Portable source artifacts remain byte-identical unless the user separately
  authorizes a source migration.
- Incremental exports bind to a specific verified base manifest and consistency lineage; mixing bases
  or applying deltas out of order fails.

Provider-specific infrastructure versions are target operations concerns and are not embedded as
portable customer state.

## 15. Resumability and Scale

Large export/import operations have stable operation IDs, bounded checkpoints, content hashes, and
idempotent reconciliation. Resuming never means trusting client-reported completion.

- Inventory and manifest assembly can resume from durable server-owned operation state.
- Individual content chunks are retried by identity/hash and never appended ambiguously.
- Import staging records terminal status per resource and binding plan version.
- Changed source consistency point, target mapping, policy, or package content invalidates affected
  work rather than silently combining versions.
- Limits cover resources, dependency edges, bytes, chunks, compression ratio, temporary storage,
  processing time, concurrent operations, and download bandwidth.
- Cancellation leaves an auditable incomplete operation and cleans nonretained staging safely.

## 16. Scriptable and Portal Surfaces

The same portability contract is exposed through:

- Tenant-scoped Portal inventory, export, mapping, validation, activation, rollback, and deletion
  workflows
- Administrative CLI/API operations for automation and runbooks
- A standalone/reference bundle reader and validator that does not require contact with the source
  SaaS operator

The shipped CLI surface is `etl-sql admin tenant export|validate|preflight|import`:

- `export` composes the existing Portal configuration export, optional Orchestrator promotion
  package, and portable source artifacts into a signed bundle. A SaaS-sourced bundle must also be
  encrypted to the tenant recipient key. Portal service credentials come only from the established
  environment/`SECRET:` flow; the narrow `admin.portability` scope reaches only the reviewed plan
  and its hash-acknowledged download.
- `validate` and `preflight` are offline. They can verify the published operator signature and list
  exclusions and required logical bindings without an account on either deployment.
- `import` requires explicit `SOURCE=TARGET` bindings, refuses collisions by default, and supports
  `--dry-run`. Its Portal bootstrap uses an interactive administrator credential supplied only by
  `ETLSQL_PORTAL_IMPORT_USERNAME` and `ETLSQL_PORTAL_IMPORT_PASSWORD` (the password may be a
  `SECRET:name` reference); no mutating service-account scope exists. Imported Orchestrator objects
  always remain disabled.

Private key passphrases likewise stay out of argv: export reads
`ETLSQL_TENANT_SIGNING_PASSPHRASE`, and import reads
`ETLSQL_TENANT_RECIPIENT_PASSPHRASE`; each may contain a machine-local `SECRET:name` reference.

UI and CLI are clients of the same server-side authorization and operation model. The browser never
assembles authority, rewrites package manifests, or performs client-side tenant filtering.

## 17. Threat Model and Negative Testing

Tests cover:

- Export or download requested by a platform admin without tenant authority
- Foreign tenant object IDs, export IDs, chunk IDs, mapping plans, and download capabilities
- Concurrent mutation and inconsistent database/artifact snapshots
- Raw/encoded secrets, private keys, capabilities, checkpoints, provider credentials, and seeded
  cross-tenant marker records entering the bundle
- Archive traversal, symlink/hardlink tricks, duplicate entries/IDs, malformed graphs, unsupported
  content types, oversized manifests, decompression bombs, and temporary-storage exhaustion
- Signature alteration, hash mismatch, wrong recipient, replay, truncation, reordered/mixed deltas,
  incompatible schema, and unauthorized mapping changes
- Identity mappings that would increase access or bind to disabled/foreign principals
- Resource mappings that target unauthorized connections, paths, Gateways, origins, or providers
- Partial import, retry, cancellation, activation race, duplicate schedule ownership, failed cutover,
  and rollback after external effects
- Source deletion that misses replicas, object versions, caches, queues, checkpoints, backups, or keys

Failures leave no active partial authority and do not confirm the existence of another tenant's
resources.

## 18. Migration Certification

Retained end-to-end evidence proves:

1. A representative tenant moves SaaS cluster A → SaaS cluster B without changing pipeline/report
   business logic.
2. The same tenant moves SaaS → supported self-hosted Enterprise with explicit target bindings.
3. Export under concurrent activity declares and honors its consistency point; final cutover prevents
   duplicate scheduling.
4. Every eligible resource is reconciled by stable ID, count, dependency, hash, ownership, ACL
   definition, and provenance; every exclusion is visible.
5. Secret and cross-tenant marker tests prove prohibited state never enters the package.
6. Tampered, malformed, incompatible, unauthorized, and resource-exhausting packages fail before
   activation and leave no partial authority.
7. Imported principals receive no more authority than approved mappings and target policy permit.
8. Workloads remain disabled until target validation and tenant approval.
9. The customer independently validates and retains the export with published tooling and
   customer-held keys after source access is unavailable.
10. Rollback or reconciliation follows the declared last reversible point and never creates two
    active schedulers.

Evidence records source/target commits and versions, topology, bundle/manifests hashes, export mode,
consistency point, mappings, resource counts, exclusions, validation outcomes, cutover ownership,
rollback/restore result, and isolation-negative results.

## 19. Rejected Alternatives

- **Database backup as tenant export** — rejected because it carries provider schema/topology,
  protected/internal state, and cannot safely express target mappings.
- **Separate package formats for Portal, Orchestrator, and SaaS** — rejected because they drift and
  undermine one promotion model.
- **Export resolved secrets for convenience** — rejected because it transfers authority and makes
  recipient/storage compromise materially worse.
- **Copy active checkpoints and sessions** — rejected because they bind runtime, authority, and
  ambiguous execution state to the source environment.
- **Import and immediately enable schedules** — rejected because unresolved identity/resources or
  duplicate scheduler ownership could cause unauthorized or duplicate work.
- **Provider-controlled encryption only** — rejected because the customer could not validate/use the
  exit artifact after losing source-provider access.
- **Promise zero-loss migration** — rejected because some state is intentionally nonportable for
  security and correctness.

## 20. Open Implementation Decisions

- Canonical manifest/component schemas and MIME/content type registrations
- ~~Signature algorithms, trust chain, recipient encryption formats, key rotation, and published-key
  distribution~~ — decided, see §13.1 and
  [Tenant Portability Signing Keys](../administration/platform/tenant-portability-signing-keys.md).
- Exact stable-ID preservation and collision UX per resource class
- Initial evidence/content classes and first large-content storage provider
- Consistency-point mechanism across Portal database, Orchestrator state, and artifact storage
- Incremental delta granularity and retention
- Standalone validator packaging and supported platforms
- Initial supported self-hosted Enterprise reference topology and compatibility window
- Maximum bundle/chunk limits and default retention for completed/incomplete operations

These choices cannot weaken tenant control, inspectability, explicit rebinding, staging, or the
customer-held exit guarantee.

## 21. Definition of Done

The portability architecture is realized when:

1. One documented bundle represents portable artifacts and eligible catalog state across deployment
   promotion and tenant migration.
2. Inventory and preflight expose every included, excluded, unresolved, incompatible, and
   policy-denied resource before activation.
3. Resolved secrets, private keys, reusable capabilities, checkpoints, leases, provider internals,
   and foreign tenant state are provably absent.
4. Import is authenticated, bounded, idempotent/staged, and disabled by default until target bindings
   and tenant authorization are complete.
5. SaaS → SaaS and SaaS → self-hosted Enterprise certification preserve business logic and prove
   identity, resource, lineage, quality, history, notification, reporting, and audit continuity
   appropriate to the selected export mode.
6. Cutover fences duplicate execution and records rollback or reconciliation boundaries.
7. Customers can verify and retain exports using published tooling and customer-held keys without
   source-provider availability.
8. Source deletion remains separately authorized and produces evidence covering all tenant-bearing
   state and retention exceptions.

## References

- [Deployment Profile Architecture](DeploymentProfiles.md)
- [SaaS Tenant Isolation Architecture](SaaSTenantIsolation.md)
- [Deployment Profile and Portability Strategy](roadmaps/Deployment_Profile_Strategy.md)
- [Deployment Profile Standards](standards/Deployment_Profile_Standards.md)
- [Product Roadmap](../../ROADMAP.md)
- [Administration](../administration/README.md)
