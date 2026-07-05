# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## v0.14.0 — Enterprise Policy Enforcement & Monitoring

Completes the enterprise controls whose protected enrollment and authoritative client runtime shipped
in v0.13.0. Standalone installations must remain unenrolled, unrestricted by organization policy, and
independent of network services.

**Shipped foundation (v0.13.0, do not redo):** machine-level enrollment, protected bootstrap, trust
key, machine identity, enroll/status/unenroll CLI (`4850f3c0`); tenant-bound RSA-PSS signed policy
retrieval, protected cache, rollback/expiry checks, configuration precedence, diagnostics, dynamic
reload, fail-closed host refresh (`9e0dfbc`). All v0.14.0 work consumes `EnterprisePolicyRuntime.Current`
— do **not** introduce a second policy loader or configuration-precedence path.

> **Before starting any item:** verify it against the current code first — some foundations already
> exist (e.g. `SecurityService` path validation, the governance audit outbox, fail-closed audit
> interceptor) and parts of these phases may be partially implemented. Don't treat a roadmap line as
> net-new work until confirmed.
>
> **Scope note:** Enterprise Phases 4–5 and ROADMAP Phase 6 (Operations Control Plane) remain in
> `ROADMAP.md`. Phases 4–5 are deferred to v0.15.0; promote roadmap work here only when that release
> begins.

### Phase 3: Policy Authority & Operation-Boundary Enforcement — ACTIVE

> **Resumed 2026-07-03** after the billion-row certification gates passed. Only unfinished work is
> retained below.

#### 3.1 Policy authority
- [~] Add an administrator-only policy API and Portal workflow to validate, version, publish, supersede, and retrieve organization policies by tenant/environment. *(Core `PolicyAuthorityService`, durable `DbPolicyAuthorityStore`, migrations, and service/store tests exist. Remaining: authenticated administrator API endpoints, Portal workflow, request validation/authorization, and endpoint integration tests.)*
- [ ] Authenticate enrolled machines, bind responses to tenant/environment, support client certificates, and reject unknown, revoked, or reassigned machine identities.
- [~] Support staged rollout and emergency rollback by publishing a newer signed version; clients must continue rejecting envelopes with older issuance times. *(Forward-only rollback and monotonic issuance are implemented and tested. Remaining: activate/promote staged versions, rollout targeting/state transitions, API/Portal exposure, and durable rollback audit.)*
- [ ] Add policy-authority availability, signing-key rotation, machine revocation, and publication audit coverage.

#### 3.2 Shared enforcement context
- [ ] Capture the snapshot when execution begins and pass it through CLI, TUI, Report Player, Portal, Orchestrator, child processes, parallel branches, and scheduled jobs.

#### 3.3 Filesystem enforcement
- [~] Re-check immediately before mutation to reduce check/use races; use handle-based validation where the platform supports it. *(Delete/copy/move handlers re-authorize immediately before the OS call. `FileSystemPolicyAuthorizer.OpenValidatedRead/Write` open the handle first and verify the OS-resolved final path (`GetFinalPathNameByHandle` on Windows, `/proc/self/fd` readlink on Linux) still matches the authorized canonical target — write opens are non-destructive until validated, so a swapped link never truncates an unauthorized file. Wired into CONVERT FILE ENCODING, SPLIT/MERGE FILES stream I/O and recursive directory copy; junction-substitution race covered by test. Remaining: `File.Copy`/`Move`/`Delete` OS calls take paths, not handles — they rely on the immediate re-authorize; extending validated-handle I/O to them means reimplementing copy/move over streams, to be weighed at the completion gate.)*

#### 3.4 Network and connector enforcement
- [~] Enforce connector allowlists and destination host/port/scheme rules before DNS resolution and connection creation. *(Connector type/host authorization is enforced at CREATE/ALTER, database discovery/version probes, dynamic REST requests, and remote-file client operation boundaries with policy refresh. Remaining: governed per-port and per-scheme rules.)*
- [~] Protect against DNS rebinding, redirects to denied destinations, proxy bypass, IPv4/IPv6 literal variants, loopback/link-local/private ranges, and credentials embedded in URLs. *(URL-authority credentials (`scheme://user:pass@host`) are rejected regardless of policy at the connector boundary, with a colon requirement so bundle URIs are unaffected. `NetworkDestinationRules` normalizes obfuscated IP literals — 32-bit decimal/hex, dotted hex/octal octets, bracketed and IPv4-mapped IPv6 — before allowlist matching, and under an enterprise host allowlist denies loopback/link-local/private/CGNAT/ULA ranges unless the exact address is explicitly listed (a wildcard `*` never grants internal-range access). Unit tests cover the literal forms and range classification; an end-to-end test proves `*` still denies an obfuscated loopback. Remaining: DNS-rebinding re-pin at connect time and HTTP redirect/proxy-bypass handling — needs connect-time hooks, a dedicated slice.)*

#### 3.5 Process, Docker, resource, and script-setting enforcement
- [~] Prevent `SET`, environment variables, command-line options, report parameters, saved sessions, plugins, and child processes from weakening locked or constrained values. *(`SET MAX_PARALLEL_DEGREE / MAX_FILE_OPERATIONS / MAX_RECURSIVE_DEPTH / MAX_SMTP_EMAILS_PER_SCRIPT` now enforce the enterprise ceiling via `OperationPolicyBoundary.EnforceCeiling` before the local `ValidateThresholdOverride` runs — and unlike the local guardrail this has no approved-safe-zone bypass, so a locked value cannot be weakened even inside a safe zone. The mutation guardrails (require-what-if / require-transaction) are enforced at statement dispatch regardless of SET. Remaining: env-var / command-line / report-parameter override paths for the same keys.)*
- [ ] Make every denial deterministic across in-process and spawned-process execution.

### v0.14.0 release gates
- [~] Pass full functional, performance, migration, recovery, v0.14.0-scoped enterprise certification, and standalone regression suites. *(Green as of 2026-07-05: full functional standard suite (4,329 pass / 1 skip — the opt-in 1B Gate F), Portal lane incl. `MigrationConvergenceTests` rolling-expand + `GovernanceRecoveryCertificationTests` (266), Core `GovernanceRecoveryTests`, and the consolidated `StandaloneRegressionTests`. Remaining: the `Category=Performance` lane is run manually (crash-risk 1M-row tests per CLAUDE.md) and the Windows/Linux enterprise certification lanes are Phase 5 / v0.15.0 scope — run `Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration` for the authoritative pre-release gate before tagging.)*
---
