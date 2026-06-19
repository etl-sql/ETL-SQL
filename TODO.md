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

- [ ] **P1.4 Implement versioned organization policy schemas** for allowed connector types,
  filesystem roots, script execution modes, remote execution, and mutation guardrails.
- [ ] **P1.5 Support policy sources** from local OS-protected configuration and HTTPS endpoints.
- [ ] **P1.6 Implement offline policy cache windows** with fail-secure behavior when a policy
  expires or cannot be validated.

### Phase 3 - Named Secret References

- [ ] **P1.7 Implement `ISecretProvider`** with Environment, OS Secret Store, and HTTPS Vault
  provider options.
- [ ] **P1.8 Add named secret reference syntax** such as `SECRET:sales_db_password` for connector
  passwords and connection-string fields.
- [ ] **P1.9 Block raw secret values from logs, diagnostics, audit rows, support bundles, and
  dashboards**, including policy tests for common connector and report workflows.

### Phase 4 - Durable Audit Outbox

- [ ] **P1.10 Implement a transactional audit outbox table** for security and mutation events that
  must survive process crashes.
- [ ] **P1.11 Create an HTTPS audit transporter** with batching, retry, deduplication, and
  backpressure limits.
- [ ] **P1.12 Define and enforce fail-closed mutation policy** when required remote audit delivery
  is unavailable.
- [ ] **P2.1 Add disk-size safeguards and retention controls** for the local outbox queue during
  extended collector outages.
- [ ] **P2.2 Certify governance recovery scenarios**: expired policy cache, unavailable policy
  endpoint, unavailable audit collector, duplicate audit delivery, and secret-provider failure.
