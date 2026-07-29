# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.18.0 Release — target 2026-08-24

First release on the monthly cadence (v0.7.0–v0.17.0 were weekly). Rationale in
[Release_Workflows.md](docs/architecture/roadmaps/Release_Workflows.md#release-cadence).
The date is a target, not a commitment — ship when the gate is green and the evidence is collected.

### Release evidence gates — none run yet

Carried forward from
[Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md).
None of these can be inherited from v0.17.0 — evidence is per-release, against the candidate commit.

- [ ] Full pre-release lane — `scripts/Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration`
- [ ] Cross-platform test lane — `scripts/test-lane.ps1`
- [ ] Enterprise hardening certification — `scripts/Test-EnterpriseHardeningCertification.ps1`,
      Windows **and** Linux
- [ ] Recovery drill — `etl-sql admin restore --validate --report`
- [ ] HA fault injection — `etl-sql admin ha-soak validate` (run `fault-plan` before `fault-run`,
      and `evidence` before `validate` — see the RCI item below)
- [ ] Security-boundary docs — `SecurityBoundaryDocTests` green
- [ ] Evidence indexed under `artifacts/release-evidence/0.18.0/`, recording what was **not**
      covered as well as what was

**Sequencing.** The language work below comes first; the release-process RCI items are scheduled
**last**, deliberately. The RCI changes touch the validation gate and CI itself, so landing them
mid-release would mean debugging the measuring instrument and the product at the same time. Doing
them at the end also means they are exercised for the first time on the *next* release rather than
destabilising this one.

### Language — Canonical Syntax and Lifecycle Consistency

**Goal.** Keep ETL-SQL predictable: object kind comes before identity, implementation type or
object definition follows `AS`, lifecycle modifiers occupy one position, and remote administration
uses `EXECUTE <admin_conn> BEGIN ... END` rather than one-off target clauses.

#### P0 — Managed connections

- [x] Dispatch `CREATE/ALTER/TEST/SHOW/DROP CONNECTION` inside Portal and Orchestrator admin
      blocks through the governed shared connection catalog contract.
- [x] Add Orchestrator shared-connection REST endpoints for list/detail/set/enable/disable/delete,
      active-entry test diagnostics, and active-entry metadata export/import.
- [x] Support `SHOW CONNECTION CONFIG` inside Portal and Orchestrator admin blocks with redacted
      catalog values.
- [x] Preserve disabled-entry definitions in shared connection export/import across Local, Portal,
      and Orchestrator catalogs.
- [x] Align shared connection impact analysis across Portal and Orchestrator admin APIs.
- [x] Honor `WHAT_IF` for Portal and Orchestrator shared-connection `CREATE`/`ALTER`/`DROP`
      admin-block mutations.
- [x] Emit shared connection security-audit events from Orchestrator admin APIs with
      Portal-aligned `SHARED_CONNECTION_*` vocabulary and redacted targets/reasons.
- [x] Align remaining shared connection fail-closed authorization/redaction behavior
      across local engine execution and `EXECUTE <admin_conn> BEGIN ... END`.
- [x] Cover Orchestrator managed SMTP catalog lifecycle: unauthorized callers, disabled entries,
      configuration export/import, diagnostics, impact analysis, and fail-closed audit behavior.
- [x] Cover Orchestrator managed WEBHOOK catalog lifecycle: `SECRET:` URL preservation, missing
      secret diagnostics, disabled entries, and configuration export/import.
- [x] Cover Portal managed WEBHOOK catalog lifecycle: `SECRET:` URL preservation, missing secret
      verify/test failures, disabled entries, configuration export/import, and audit behavior.
- [x] Cover WEBHOOK `WHAT_IF` admin-block connection mutations for both Portal and Orchestrator
      connectors.
- [x] Cover Portal managed WEBHOOK admin endpoints rejecting non-admin callers beyond generic
      admin endpoint checks.
- [x] Cover Portal managed WEBHOOK named-notification delivery routing through the Orchestrator
      dispatcher proxy.
- [x] Add remaining managed SMTP/WEBHOOK delivery coverage that exercises Portal-to-Orchestrator
      notification dispatch end to end rather than only catalog lifecycle and generated scripts.

#### P1 — Lifecycle modifiers

- [x] Align local `CREATE OR REPLACE CONNECTION` with `CREATE OR ALTER CONNECTION` patch/upsert
      behavior and cover report-object replacement execution for local report definitions.
- [x] Cover unsupported `CREATE OR ALTER` / `CREATE OR REPLACE` pairs for the core,
      Report-SQL, and Portal object kinds in the lifecycle matrix.
- [x] Strengthen lifecycle completion tests so `CREATE OR ALTER` / `CREATE OR REPLACE`
      suggestions match the documented supported-kind matrix instead of spot checks.
- [x] Cover `DROP IF EXISTS` support and rejection across local/report/catalog/Portal objects,
      including rejecting quoted Portal dataset identities in the local dataset drop path.
- [x] Cover unsupported `ALTER` object kinds from the lifecycle matrix so they cannot parse
      into silently unsupported statement shapes.
- [x] Cover unsupported `CREATE IF NOT EXISTS` object kinds and reject the form before `IF`
      can be misread as an object identity.
- [ ] Make `CREATE OR ALTER` and `CREATE OR REPLACE` semantics identical across parser, AST,
      formatter, engine handlers, Portal authorization, persistence, linting, completion, and docs.
- [ ] Add negative tests for every unsupported object/mode pair in the lifecycle capability matrix.

#### P1 — Identity, type, and clause ordering

- [x] Enforce `&name` for local/report `DROP DATASET` and update parser/handler tests to reject
      bare or `#temp` dataset identities while preserving quoted Portal dataset drops.
- [x] Make `PUBLISH DATASET` use canonical identity-first order:
      `PUBLISH DATASET &name FROM 'file.parquet'`, reject retired `FROM ... AS &name`, and
      update publish examples/tests.
- [x] Enforce `&name` for local/report `ALTER DATASET` attempts while keeping quoted Portal
      dataset metadata alters routed to Portal parsing.
- [x] Clean up dataset identity documentation so local/report examples use `&name`, Portal
      administration examples use quoted catalog names with `IN FOLDER`, and retired
      `PUBLISH DATASET FROM ... AS &name` wording is removed.
- [x] Clean up Portal report publish documentation so examples use
      `PUBLISH REPORT 'name' FROM 'file.rptsql' IN FOLDER '/path'` instead of retired
      source-first `TO FOLDER` / `FROM SCRIPT` forms.
- [x] Enforce `&name` for local/report datasets across `CREATE`, `ALTER`, `DROP`, `USE`, `REFRESH`,
      `EXPORT`, and `PUBLISH`; keep quoted catalog identity only where Portal dataset commands
      require it.
- [x] Make publish commands identify the published object before the source:
      `PUBLISH REPORT 'name' FROM 'file.rptsql'`, `PUBLISH BUNDLE 'name' FROM 'folder'`, and
      `PUBLISH DATASET &name FROM 'file.parquet'`. Remove `PUBLISH DATASET FROM ... AS &name`.
- [x] Use `AS` consistently for typed objects and definition/property-bag report objects, including
      forms such as `CREATE STYLE name AS (...)`.
- [x] Add canonical `INSERT TAG` and `INSERT LINEAGE` forms that reuse existing metadata
      seeding/import execution and round-trip through `ToSql()`.
- [x] Add canonical `UPDATE TAG` and `DELETE TAG` forms for explicit metadata mutation,
      including runtime removal from inherited table/column metadata.
- [x] Add canonical `DELETE LINEAGE` for imported lineage records while preserving immutable
      auto-captured lineage.
- [x] Treat tags and lineage as metadata records, not unnamed DDL objects: add canonical
      `INSERT/UPDATE/DELETE TAG` and `INSERT/DELETE LINEAGE` forms, retire `CREATE TAG`,
      `CREATE LINEAGE`, and bare `TAG ... WITH (...)`, and preserve immutable auto-captured lineage.
- [ ] Reserve compound object kinds such as `SHARE LINK`, `SAVED VIEW`, and `EMBED TOKEN` only for
      named resources with real lifecycle; do not encode implementation type before `CONNECTION`.
- [ ] Correct Portal share/embed expiration syntax drift so parser, formatter, docs, and
      configuration export agree on one structural `EXPIRES <timestamp>` clause.

#### P1 — Inspection via `eng.*`

- [ ] Retire row-returning `SHOW` commands in favor of `SELECT ... FROM [connection.]eng.<table>`
      with normal `WHERE`, `JOIN`, `ORDER BY`, and `INTO` support.
- [ ] Implement and document the full `eng.*` virtual table catalog: session state, lineage,
      governance, data quality, jobs/orchestration, Portal catalog, and parameterized table-valued
      functions such as `eng.catalog_search()`.
- [ ] Implement `eng.tags` as a globally enumerable virtual table; remove `SHOW TAGS FOR SCRIPT`
      and `SHOW TAGS FOR TABLE <name>`.
- [ ] Reconcile connection config, report history/dependencies, bundle versions/files, refresh jobs,
      and effective permissions as `eng.*` virtual tables with `WHERE` filters.
- [ ] Rename `SHOW LINEAGE EXPORT AS OPENLINEAGE TO '...'` to
      `EXPORT LINEAGE AS OPENLINEAGE TO '...'` because it writes a file rather than returning rows.
- [ ] Reserve `eng` as the engine catalog schema name and reject `CREATE CONNECTION eng AS ...`.

#### P2 — Duplicate surface syntax

- [ ] Retire function-style aliases for canonical file/email operations:
      `SEND_EMAIL(...)`, `SEND_FILE(...)`, `RECEIVE_FILE(...)`, `FILE_SEND`, `FILE_RECEIVE`,
      `COPY_FILE(...)`, `MOVE_FILE(...)`, `DELETE_FILE(...)`, `CREATE_DIRECTORY(...)`, and sibling
      underscore forms.
- [ ] Retire `FOR EACH`; `FOREACH` is the only loop spelling.
- [ ] Retire `WAITFOR (<condition>)`; `WAIT UNTIL <condition>` is the condition-polling form.
      Keep `WAITFOR DELAY` and `WAITFOR TIME`.
- [ ] Retire `SHOW COLUMNS FOR`, `SHOW SCHEMA FOR`, and `DESCRIBE`; use
      `SELECT * FROM eng.columns WHERE table_name = ...`.
- [ ] Ensure generated scripts, samples, snippets, formatter output, autocomplete, hover help, docs,
      and diagnostics emit only canonical forms. Any temporary alias must produce a deprecation
      diagnostic with the exact replacement and removal release.

#### P2 — Round-trip guarantees

- [ ] Give every executable statement a real `ToSql()` serialization. Report-SQL and Portal
      statements must never fall through to `UNKNOWN STATEMENT`.
- [ ] Add generated statement-surface inventory from parser/AST metadata and fail CI when a
      creatable object lacks lifecycle, formatter, grammar completion, help, snippet where
      applicable, or reference-page coverage.
- [ ] Add table-driven parser -> formatter -> parser tests for every canonical form and explicit
      rejection tests for every retired form.
- [ ] Replace permissive documentation-snippet validation with production-parser validation that
      asserts `script.Diagnostics` is empty, then parse every copy-pasteable documentation and
      sample block in its correct execution context.
- [ ] Update `docs/syntax-index.md`, statement references, connector references, administration
      guides, architecture contracts, help resources, snippets, migration guide, samples,
      configuration export, LSP grammar, and release notes as one atomic language cleanup.

**Definition of done.** A user can predict a statement from the object model; Portal and
Orchestrator reuse normal SMTP/WEBHOOK connections; multiple named refresh jobs can target the same
report; unsupported lifecycle forms fail at parse/lint time; canonical statements round-trip through
the formatter and documentation test lane.

### Release-process RCI — issues found cutting v0.17.0 (scheduled last)

Thirteen process problems surfaced during this release. Remaining items are listed in rough value
order. The theme: **the gate's failures were mostly not product
defects**, they were the gate measuring the wrong thing, hiding things, or being impossible to run.

#### Process observation worth keeping

The **authorship-permission regression** (five sites, including unauthenticated share links
surviving revocation) was found by two pre-existing tests during the gate. It had been reviewed by
hand in Phase 2 and cleared. Meanwhile the one finding raised purely from reading the diff turned
out to be wrong on both premises, and its proposed fix measured as a no-op. For permission and
revocation logic, a red test is far stronger evidence than a careful read.

### Close CodeQL alert 323 — unescaped telemetry in the lineage tree

Open High `js/xss` accepted for v0.17.0 and left **open** rather than dismissed, because it is a real
latent gap. Full triage in
[v0.17.0-code-review.md](docs/architecture/decisions/v0.17.0-code-review.md).

Implementation has been fixed in the canonical shared runtime and synced to host copies; the
remaining work requires the next CodeQL run on `main`.

- [ ] Confirm alert 323 closes on the next `main` scan.

### Automate the MSI in-place upgrade check

Today this is a manual, elevated step in the release checklist, and it is the kind of step that
quietly stops happening. It is the only thing that catches a WiX major-upgrade regression — a
failure mode that is otherwise **silent**, producing a side-by-side second install rather than an
error. The gate's N→N+1 drill covers the data/engine layer, not the installer.

It is manual because a `perMachine` MSI needs elevation and nobody wants to mutate their own
workstation. **Both reasons vanish on a GitHub-hosted `windows-latest` runner**: it executes as an
administrator, so `msiexec /qn` needs no UAC, and it is ephemeral, so installs leave nothing behind.

- [ ] Add `scripts/Test-MsiUpgrade.ps1 -PreviousMsi <path> -CurrentMsi <path>` asserting the full
      sequence, not just the registry:
      1. install previous → exactly **1** uninstall entry at the previous version
      2. write a sentinel file into `InstallLocation`
      3. install current **over** it
      4. **exactly 1 entry, at the new version** — two entries is the side-by-side regression
      5. sentinel survived → config/data preserved
      6. installed `ETL-SQL.exe --version` reports the new version
      7. uninstall → 0 entries
- [ ] Steps 5–6 matter: a registry-only assertion passes while files are clobbered or
      `RemoveExistingProducts` is mis-scheduled, which is precisely what "preserves config/data" in
      the checklist is asking about.
- [ ] Add a CI job gated to `release/**` pushes and tags (not every PR — the previous release MSI is
      ~900 MB). Resolve the previous tag with `gh release list`, download with
      `gh release download <tag> --pattern '*-x64-Setup.msi'`, and cache it keyed on the tag.
- [ ] Once green, make it a required status check and delete the manual step from
      [release-checklist.md](docs/releases/release-checklist.md) Phase 4.

Static checks are a useful cheap complement but are **not** a substitute: identical `UpgradeCode`,
ascending `ProductVersion`, and an unchanged `MajorUpgrade` element rule out the most common cause
and nothing else. Consider adding them as a fast unit test over the built MSI regardless.

### Scale certification — make the harness incapable of false failures

**Resolves a question open since v0.15.0.** There was no engine regression in v0.15.0, v0.16.0, or
v0.17.0. Every "regression" was produced by measuring cold binaries at the end of a long gate. Full
measurements in
[v0.17.0-performance-results.md](docs/architecture/decisions/v0.17.0-performance-results.md).

The same commit measures 5013 ms warmed and 8977 ms cold — a **56% spread**, far wider than any
threshold the gate compares against. v0.15.0 reached the right conclusion ("environmental, not
code") but had no mechanism to prove it, so it was deferred twice more and cost v0.17.0 most of a
release day plus a false regression alarm.

Remaining work:

- [ ] **Run scale certification before the long test lanes**, or quiesce the machine first. Running
      it last guarantees the worst measurement conditions in the gate.
- [ ] **Add a same-worktree A/B mode** for comparing two commits, so version comparisons cannot be
      contaminated by comparing two directories in different thermal states — the exact error that
      produced the v0.17.0 false alarm.
- [ ] Investigate performance improvements when data-quality allocation is active. Focus on reducing
      per-row allocation and GC pause time without weakening `@expect`/`@fail` behavior, quarantine
      routing, or lineage/tag capture.

Do **not** re-bless the baselines. `baseline-smoke.json` and `baseline-standard.json` both pass when
measured correctly; an earlier bless of cold readings was correctly reverted in `e3fa80af`.

