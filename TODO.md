# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## Connection Diagnostic Engine (TEST CONNECTION)

ROADMAP item (*Shared Connection & Secret Governance → Future Candidate Phases*) promoted to
active work: a governed, layered `TEST CONNECTION <alias>` diagnostic that reports connection
health in plain English. Design/plan captured with the increment.

### Increment 1 — statement + shared core
- [x] Shared `ConnectionDiagnosticEngine` core (DNS → TCP → TLS), governed through
      `ConnectorPolicyAuthorizer` + DNS-rebind check; never echoes secrets.
- [x] `TEST CONNECTION <alias> [INTO #tmp]` statement (AST, contextual soft-keyword parser, handler).
- [x] Unit tests (parse, `test` still a valid identifier, non-network report shape, missing-alias error).
- [x] `TEST_CONNECTION.md` help doc.
- [x] Real-socket runtime coverage: loopback `TcpListener` test drives DNS+TCP to OK through the
      governed path. (Full CLI e2e against a live/bogus host still pending — the app can't start in
      the current sandbox due to an unrelated locked enterprise-enrollment temp dir.)

### Increment 2 — deferred
- [ ] Portal "Test connection" button + API over the same shared core.
- [ ] Credential-auth probe + SSH/SFTP host-key validation (connector-specific; never echo secrets).

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`Docs/Operations/v0.15.0-flaky-tests.md` and `Docs/Operations/v0.15.0-performance-results.md`.

### Restore the 70% coverage gate

`ci.yml`'s threshold was lowered **70.0 -> 69.5** to ship v0.15.0 (landed at 69.8%). Analysis
from 2026-07-13 found that the v0.15.0 headline feature (`Core.Adaptive.*`) is already well-covered;
the remaining gap is infrastructure coverage.

- [ ] `App.*` runners (`WarmJobRunner`, `EnterpriseEnrollmentManager`, `DatabaseMigrationService`) are
      the biggest untested chunk but hardcode elevation checks, stores, and file I/O. Meaningful tests
      need a testability seam first, not error-path-only tests.
- [ ] Iterate CI-in-the-loop: add tests, push, read the CI coverage percentage (the authoritative
      scope; a local run excluding Portal reports around 50%, not comparable), repeat until >= 70.0,
      then restore the `ci.yml` threshold to **70.0**.
