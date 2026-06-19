# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Governance Core

> Status: **active (v0.13.0).**
> Goal: enforce centralized security policy, named secret references, and durable audit delivery
> across all hosts (CLI, IDE, Portal, and Orchestrator) without weakening local zero-trust defaults.
>
> Priority convention: **P1** the supported governance path that must exist before the governance
> claim; **P2** certification, recovery, compatibility, and operational verification.

### Phase 1 - Typed Policy Registry

- [x] **P1.1 Create a central typed policy registry** for settings classified as Forbidden,
  Allowed, Constrained, or Locked.
  *(done)* Added `ETL_SQL.Core.Governance` with typed policy metadata
  (`GovernancePolicyDefinition`, scope/classification/value-kind enums, and
  `IGovernancePolicyRegistry`). The default registry seeds the existing governance surfaces:
  secret persistence, path/host/env controls, execution limits, connector allowlists, secret provider
  selection, and audit outbox knobs. Registered through `AddEtlSqlEngine` so CLI, TUI, Portal,
  Orchestrator, and language-server hosts can consume the same catalog. Tests cover duplicate-key
  rejection, environment-style key normalization, default classification coverage, pinned core keys,
  and DI registration.
- [x] **P1.2 Enforce policy against parsed AST nodes** instead of text matching, with policy
  decisions attached to diagnostics.
  *(done)* Added structured `GovernancePolicyDecision` metadata and threaded it through core
  parser diagnostics, lint results, and neutral analysis diagnostics. The existing
  `ConnectionEncryptionRule` now emits a central `Engine:AllowPlaintextSecrets` policy violation
  for plaintext connector secrets discovered from parsed `CreateConnectionStatement` AST nodes.
  Tests verify policy metadata propagation and that commented plaintext connection text is ignored.
- [x] **P1.3 Apply policy at compile/lint time and execution time** so bypassing the linter does
  not bypass runtime enforcement.
  *(done)* Added `GovernancePolicyRule` to enforce central policy decisions against parsed AST
  statements during lint/analysis, including nested blocks. Runtime execution now enforces the same
  `Engine:AllowPlaintextSecrets` forbidden policy in `SetAllowPlaintextSecretsStatementHandler`, so
  scripts cannot bypass the linter and enable plaintext secret persistence at execution time.

### Phase 2 - Organization Policy Documents

- [x] **P1.4 Implement versioned organization policy schemas** for allowed connector types,
  filesystem roots, script execution modes, remote execution, and mutation guardrails.
  *(done)* Added a typed v1.0 `OrganizationPolicyDocument` schema with connector allowlists,
  approved filesystem roots, execution modes and limits, remote execution controls, and mutation
  guardrails. The schema parser accepts JSON with comments/trailing commas, validates versioned
  documents, rejects duplicate/invalid entries, and maps document sections back to central policy
  keys for later source loading and enforcement.
- [x] **P1.5 Support policy sources** from local OS-protected configuration and HTTPS endpoints.
  *(done)* Added organization policy source abstractions for local protected JSON files and HTTPS
  endpoints. Local sources require fully qualified paths and an OS protection validator; the default
  validator rejects broad writable ACLs on Windows and group/other-writable modes on Unix. HTTPS
  sources reject non-HTTPS URIs, validate loaded v1.0 policy documents, and can be combined through
  a loader/factory that falls back across configured sources.
- [x] **P1.6 Implement offline policy cache windows** with fail-secure behavior when a policy
  expires or cannot be validated.
  *(done)* Added cache-aware organization policy loading. Live policy loads now refresh a validated
  cache entry; if all live sources fail, the loader may use the cached policy only while it remains
  inside the configured offline window. Missing, invalid, disabled, or expired cache state fails
  secure. A file cache store persists validated policy envelopes with protected-file validation on
  read and owner-only Unix permissions on write.

### Phase 3 - Named Secret References

- [x] **P1.7 Implement `ISecretProvider`** with Environment, OS Secret Store, and HTTPS Vault
  provider options.
  *(done)* Added provider-neutral secret resolution with `ISecretProvider` and writable
  OS-secret-store support. Environment secrets resolve normalized names with optional prefixes;
  OS store secrets are protected with existing user/machine-bound Core crypto; HTTPS Vault requires
  HTTPS endpoints and supports bearer auth plus JSON `{ "value": "..." }` responses. Tests cover all
  providers, provider factory selection, protected-at-rest OS store values, and secret-name
  validation.
- [x] **P1.8 Add named secret reference syntax** such as `SECRET:sales_db_password` for connector
  passwords and connection-string fields.
  *(done)* Added `SECRET:name` resolution during `CREATE CONNECTION`, `CREATE OR ALTER CONNECTION`,
  and `ALTER CONNECTION`. Secret references are expanded through the configured `ISecretProvider`
  only for sensitive connector option keys or sensitive fields inside connection strings, plus full
  target-string secret references. The engine DI now registers a configurable secret provider from
  `Governance:Secrets:*`/`Secrets:*` settings.
- [x] **P1.9 Block raw secret values from logs, diagnostics, audit rows, support bundles, and
  dashboards**, including policy tests for common connector and report workflows.
  *(done)* Added a shared Core `SecretRedactor` and routed diagnostics, execution exceptions,
  Core logger sinks, connector configuration display, result JSON/XML/table formatting, portal
  audit rows, report execution errors/parameters, and orchestrator job status/history errors
  through it. Common secret shapes now mask `PASSWORD`/`PWD`, API keys, bearer tokens, named
  `SECRET:` references, protected `ENC:`/`DPAPI:`/`MACHINE:` values, client/account keys, SAS
  tokens, passphrases, and SASL credentials. Tests cover connector config redaction, diagnostics
  and execution exceptions, result serialization redaction, and portal audit persistence.

### Phase 4 - Durable Audit Outbox

- [x] **P1.10 Implement a transactional audit outbox table** for security and mutation events that
  must survive process crashes.
  *(done)* Added durable `AuditOutboxMessages` state to the portal database model with pending
  delivery status, attempt/lock timestamps, redacted JSON payload, correlation metadata, and an
  optional `AuditLog` link. `AuditService` now stages the audit row and outbox row in the same EF
  unit of work, so security and mutation audit events commit atomically with their durable delivery
  record. SQLite and PostgreSQL migrations create the outbox table and delivery indexes; tests cover
  redacted payload persistence and migration convergence.
- [x] **P1.11 Create an HTTPS audit transporter** with batching, retry, deduplication, and
  backpressure limits.
  *(done)* Added a hosted audit outbox transporter that runs only when an HTTPS collector endpoint
  is configured. It drains pending outbox rows in bounded batches, sends event envelopes keyed by
  unique `EventId` values for collector-side deduplication, marks successful deliveries as
  delivered, and applies exponential retry/backoff until the configured attempt limit moves a row
  to `Failed`. Configuration now covers endpoint, bearer token, batch size, sweep interval,
  timeout, max attempts, lock duration, and backlog warning threshold. Tests cover successful
  delivery, retry/backoff with terminal failure, and HTTPS-only endpoint enforcement.
- [x] **P1.12 Define and enforce fail-closed mutation policy** when required remote audit delivery
  is unavailable.
  *(done)* Added `AuditFailClosedInterceptor`, a SaveChanges interceptor on `PortalDbContext` that
  enforces the central `Audit:RemoteDeliveryRequired` policy at the one choke point every
  security-sensitive mutation shares: the audit row staged into the same EF unit of work. When
  `Portal:Audit:RequireRemoteDelivery` is on, `AuditDeliveryGate` blocks the commit (so the mutation
  itself cannot succeed) once the local outbox shows the collector is unreachable — any terminally
  Failed delivery, pending backlog at `FailClosedMaxPendingBacklog`, oldest pending event past
  `FailClosedMaxBacklogSeconds`, or queued payload past `OutboxMaxBytes`. A single event during a
  brief outage still commits; the default best-effort posture is unchanged. The block surfaces as
  HTTP 503 (`AuditDeliveryUnavailableException` → middleware). Tests cover backlog/failed blocking,
  first-event-allowed, and default-posture pass-through.
- [x] **P2.1 Add disk-size safeguards and retention controls** for the local outbox queue during
  extended collector outages.
  *(done)* The transport sweep now runs `PruneAsync` before each drain: it purges Delivered rows
  past `OutboxDeliveredRetentionMinutes`, and — only when remote delivery is *not* mandatory — sheds
  the oldest rows (Delivered first) to keep the queue under the `Audit:OutboxMaxBytes` cap, bounding
  local disk use during a prolonged outage. When delivery *is* mandatory nothing is dropped: the
  fail-closed gate stops new mutations instead, and saturation is logged for operators. Tests cover
  retention purge, size-cap shedding when best-effort, and no-shed under mandatory delivery.
- [x] **P2.2 Certify governance recovery scenarios**: expired policy cache, unavailable policy
  endpoint, unavailable audit collector, duplicate audit delivery, and secret-provider failure.
  *(done)* Added `GovernanceRecoveryTests` (Core) certifying that an unavailable HTTPS policy
  endpoint with no cache fails secure and that Environment/OS-store/HTTPS-vault secret providers
  fail closed (error, never a blank secret) on missing/unreachable secrets; expired/fresh policy
  cache recovery remains certified by `OrganizationPolicyCacheTests`. Added
  `GovernanceRecoveryCertificationTests` (Portal) certifying the unavailable-collector fail-closed
  mutation path, and an `AuditOutboxTransportTests` case certifying that a redelivery after a lost
  "Delivered" commit resends the *same* `EventId` so the collector can deduplicate.
