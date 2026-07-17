# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`docs/architecture/decisions/v0.15.0-flaky-tests.md` and `docs/architecture/decisions/v0.15.0-performance-results.md`.

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

---

## v0.16.0 Pre-Release Evidence

Collect release-suite evidence before publishing v0.16.0. The detailed evidence packet template is
[`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md).

- [ ] Functional fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Migration and upgrade evidence: `.\scripts\Test-PreRelease.ps1 -IncludeSlt -Explain`
      plus N to N+1 upgrade-path evidence.
- [ ] Enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Recovery drill evidence: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] HA failure certification: `etl-sql admin ha-soak fault-run` and
      `etl-sql admin ha-soak validate`.
- [ ] Scale and performance evidence: `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke`;
      run Standard tier when advertising scale claims.
- [ ] Standalone regression:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~StandaloneRegressionTests`.
- [ ] Security boundary docs:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests`.

---

## Portal Script Editor

- [ ] **Report Preview Pane** Having a report preview pane, using the VS Code preview pane allows the user to do a more WYSIWYG approach to creating reports
      - How do we do a serve command?  You already have everything you need can we just build a preview without calling that?
- [ ] **tools/ui-sandbox match**  Make sure ui-sandbox works the exact same as Portal so we can have a better debug/preview experience for developers
- [ ] **Query with Alias not running**  Using CREATE CONNECTION m AS MOCKDB();  SELECT u.* FROM m.Users AS u;  Did not run in the ui-sandbox
- [ ] **Expand * command** I'm pretty sure this is available, but ctrl+space does not work.  If its a different key combo we need to know.
- [ ] **Separate Save from Git Commit/Push** — `Save` updates the Portal script and catalog only.
      When source control is configured, expose Git commit/push as an explicit, separately reported
      operation with its own button. Do not hold a database transaction open during Git or network I/O.

---

## Portal Rename Follow-Up

Initial product-facing rename landed in `788c2c3b`: user docs, visible strings, samples, syntax index,
and the admin connector token now use **Portal** / `PORTAL` instead of **Report Portal** /
`REPORTPORTAL`. Remaining work should be handled as explicit mechanical batches:

- [x] **Decide whether to rename internal project identities.** Answer: Yes,  Current code still uses project,
      assembly, namespace, and test names such as `ETL-SQL.Portal`,
      `ETL_SQL.Portal`, and `tests/ETL-SQL.Portal.Tests`. If we want the rename to be
      complete end-to-end, rename these to `ETL-SQL.Portal`, `ETL_SQL.Portal`, and
      `tests/ETL-SQL.Portal.Tests`, then update solution/project references, Dockerfiles,
      installer scripts, CI/test-lane scripts, architecture boundary tests, and publish/build scripts.
- [x] **Rename connector implementation folders/classes if internal identities are renamed.**
      `src/ETL-SQL.Connectors/ReportPortal/ReportPortalConnector.cs` and
      `ReportPortalDataSource` still use the old internal type names while exposing the new
      `PORTAL` connector token. Rename to `PortalConnector` / `PortalDataSource` only as part of the
      internal identity pass so tests and DI registration move together.
- [x] **Rename sample folder paths after checking release/script references.**
      `samples/report_portal_deployment/` still uses the old folder name even though its prose was
      updated. Rename to `samples/portal_deployment/` and update every sample guide, test, release,
      and packaging reference.
- [x] **Regenerate and verify VS Code syntax assets after any token/name changes.**
      The grammar already advertises `PORTAL`; after additional renames, rerun the syntax/asset
      generation checks and verify `src/etl-sql-vscode/syntaxes/etlsql.tmLanguage.json` has no stale
      `REPORTPORTAL` entry.
- [x] **Run a full stale-name audit before closing the rename.**
      Search docs, scripts, source, tests, samples, installer assets, Docker assets, snapshots, and
      generated browser assets for `Report Portal`, `report portal`, `REPORTPORTAL`,
      `REPORT_PORTAL`, `ReportPortal`, and `report_portal`. Keep only intentional internal names if
      the project/namespace rename is deferred.
- [x] **Verification gate for the internal rename batch.**
      Run `node .\scripts\sync-assets.js -Check`, `node .\scripts\generate-syntax-index.js --check`,
      `dotnet build src\ETL-SQL.App\ETL-SQL.App.csproj --no-restore`,
      `dotnet build src\ETL-SQL.Portal\ETL-SQL.Portal.csproj --no-restore` (or the new
      Portal path), focused docs/suggestion tests, and the Portal test lane. Include Docker-backed
      connector smoke once Docker is available.

> **Completed 2026-07-17** on branch `feat/portal-internal-rename` (off `release/v0.16.0`). Renamed the four
> projects (`ETL-SQL.Portal{,.Data,.Migrations.Postgres}`, `tests/ETL-SQL.Portal.Tests`), the connector
> folder/classes (`Portal/PortalConnector` + `PortalDataSource`, namespace `ETL_SQL.Connectors.Portal`),
> and `samples/portal_deployment/`. Full solution builds clean; architecture-boundary tests pass; Portal
> lane 335/336 (the 1 failure — `AdminCatalogs_FilterPageAndBulkMutate…` — passes in isolation, a
> pre-existing order-dependent flake, not a rename regression). Also renamed deployment-facing artifacts:
> Docker image `etl-sql/report-portal` → `etl-sql/portal`, CI/test image tag `etl-sql-reportportal-test`
> → `etl-sql-portal-test`, AD-group sample mappings `GG-ReportPortal-*` → `GG-Portal-*`, and the optional
> config key `Doctor:ReportPortalHealthUrl` → `Doctor:PortalHealthUrl`.
> **Intentionally kept** (historical/point-in-time records): `CHANGELOG.md`, `BREAKING_CHANGES.md`,
> `certification-results/`, `capacity-results/`, and the old→new link map keys in
> `scripts/migrate-all-docs-links.js`.
> **Pre-existing debt found and fixed** (docs IA restructure, not the rename): `AGENTS.md`'s admin-guide
> link pointed at the non-existent `docs/guides/report-portal-admin.md` (repointed to
> `docs/administration/portal/README.md`); and `scripts/publish_release.ps1` copied pre-restructure
> `Docs/…` guide files that no longer exist (also a Linux case-sensitivity bug via capital `Docs`) — the
> two stale guide copies were dropped in favor of a version-pinned `DOCS.txt` pointer, keeping the
> LICENSE/NOTICE/third-party/changelog artifacts bundled.

---

## v0.16.0 Sprint Code Review

Findings from the 2026-07-15 review of `v0.15.0..HEAD` and the in-progress Portal editor work.
Resolve release blockers before publishing v0.16.0; schedule the remaining hardening and boundary
work by priority.

### Release blockers

- [x] **P0 — Enforce recursively read-only Designer execution.** Reject `INTO` and every other
      state-changing construct in all operands of `SetOperationStatement`; use a shared AST policy
      visitor and add regression coverage for nested `UNION`/`INTERSECT`/`EXCEPT` forms.
- [x] **P0 — Make Portal audit fail-closed atomic with mutations.** Stage the audit outbox record in
      the same transaction and `PortalDbContext` unit of work as each protected database mutation.
      Add tests proving an unavailable fail-closed audit sink leaves the target state unchanged.
- [x] **P0 — Protect and cache `/metrics`.** Restrict the endpoint to the intended monitoring trust
      boundary, apply rate limiting, cache snapshots, aggregate metrics in SQL, and move recursive
      storage-size scans to a bounded background sampler.
- [x] **P0 — Replace process-time artifact fencing.** Issue monotonic fencing tokens from the shared
      database ownership lease and require a valid token for writes, moves, and deletes. Test two
      healthy nodes alternating ownership, clock skew, restart, and stale deletion.
- [x] **P0 — Add security-event outbox retention.** Prune delivered events from the production worker,
      calculate capacity from pending/failed work rather than lifetime rows, and test delivery beyond
      the configured lifetime event and byte limits.
- [x] **P0 — Fix unfenced deletions in FencedArtifactStorage.** Ensure DeleteAsync calls FenceAsync
      before calling the underlying storage, preventing stale/fenced-out nodes from deleting newer
      active artifacts on shared storage.

### Security, correctness, and performance

- [x] **P1 — Remove the process-global Portal PII encryption provider.** `PortalEncryptionProvider`
      stores one static `IDataProtector`, so sequential in-process Portal hosts can replace and dispose
      each other's key provider. Make encryption context-owned and add multi-host isolation coverage.
- [x] **P1 — Keep script save internally atomic after Git is separated.** Coordinate catalog metadata
      and artifact replacement so failures restore both consistently; Git commit/push must not be part
      of the save transaction and must expose its own success or failure state.
- [x] **P1 — Serialize explicit Git operations.** Protect Commit/Push actions from concurrent access to
      the shared repository index, preferably with isolated temporary indexes/worktrees plus a
      repository lease that also works across Portal nodes.
- [x] **P1 — Replace regex-only plaintext-secret validation.** Validate parsed connection definitions
      and connector-native connection strings, including positional strings, URLs, headers, and other
      credential-bearing forms. Add bypass regression tests.
- [x] **P1 — Fix Designer completion document identity.** Build the scoped document URI exactly once and
      use the same value for schema registration and completion. Add a successful connection-aware
      completion integration test.
- [x] **P1 — Bound the metadata cache and remove resolved credential retention.** Use server-owned stable
      document identities, TTL/LRU eviction, refresh concurrency limits, and secret handles rather than
      storing resolved connection strings in singleton dictionaries.
- [x] **P1 — Make Designer schema discovery bounded.** Return tables before columns or use a provider
      batch catalog API; add object limits, single-flight caching, cancellation, timeouts, and controlled
      concurrency instead of sequential N+1 provider calls.
- [x] **P1 — Stop auditing full Designer SQL text.** Record a query fingerprint and structural resource
      metadata; do not place arbitrary literals or potentially sensitive row filters in ordinary audit
      detail.
- [x] **P1 — Make audit outbox claiming atomic across Portal nodes.** Claim batches with a database-safe
      update/lease operation and retain the documented at-least-once EventId deduplication contract.
- [x] **P2 — Correct operational metric labels and values.** Derive database reachability and topology
      from actual state rather than exporting constant healthy/HA values.
- [x] **P2 — Make CodeMirror builds reproducible.** Pin CodeMirror and esbuild versions in a lockfile or
      equivalent manifest and generate SBOM versions from the resolved dependency graph.
- [x] **P2 — Bound expensive Designer requests.** Add script/body size, AST complexity, generated item,
      and concurrency limits to parse, analyze, generate, run, schema, and save endpoints.

### Layering and maintainability

- [ ] **P1 — Split connector implementations into independently deployable projects.** Create a small
      connector contracts/registry layer and provider-specific projects or coherent provider groups so
      hosts do not load every database, cloud, messaging, and native dependency.
- [x] **P1 — Restore `ETL-SQL.Core` as a contracts/domain layer.** Move Testcontainers out of the runtime
      project first, then move Docker, SQLite persistence, native SQLite, and file-sink implementations
      into infrastructure projects behind Core interfaces.
      - [x] Move Docker/Testcontainers runtime implementation into `ETL-SQL.Infrastructure.Docker`.
      - [x] Move SQLite session metadata persistence behind infrastructure interfaces.
      - [x] Move SQLite governance outbox persistence and native SQLite packages behind infrastructure interfaces.
      - [x] Move file-sink logging implementation behind infrastructure interfaces.
- [x] **P1 — Correct the Engine dependency direction.** Remove Engine dependencies on Reporting,
      presentation packages such as `Spectre.Console`, and other upper layers; update implementation or
      boundary documentation where Analysis integration is intentionally part of execution.
- [ ] **P2 — Thin Portal controllers.** Move parsing, AST/DTO conversion, lint orchestration, schema
      registration, and save workflows into application services; keep controllers focused on
      authorization, transport mapping, and HTTP results.
- [x] **P1 — Enforce source boundaries in tests.** Add an architecture test for allowed project
      references and banned namespaces/packages so the documented layering rules fail during CI when
      violated.
