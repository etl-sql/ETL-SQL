# ETL-SQL Documentation IA Restructure Plan

Companion to `DOCUMENTATION_AUDIT.md`. Focus of this plan: turn the remaining
"big documents" into small, findable, single-source topics. Decisions locked with the
maintainer (2026-07-16): **thin guide hubs**, a **task/how-to index** as the second
index, **auto-generated CLI reference**, plan approved before any file changes.

---

## 1. Context / problem

Legacy docs duplicated information across large manuals (Administrators/Report-Admin/
User Manual/Orchestrator/Report-SQL/grammar) *and* a parallel help corpus in
`src/ETL-SQL.Core/Resources/Help`. Users couldn't find topics quickly.

The physical `Resources/Help` folder is already gone. `src/ETL-SQL.Core/ETL-SQL.Core.csproj`
now embeds `docs/reference/**` pages **as** the runtime help corpus at build time
(`EmbeddedResource … Link="Resources\Help\<Category>\<KEYWORD>.md"`), and
`LanguageService` serves them to CLI `help`, LSP hover, and autocomplete. So
`docs/reference/**` is already the single source of truth for help — the remaining
duplication lives in the oversized `docs/guides/*` narratives.

## 2. Target model

Two layers, one source of truth:

- **`reference/` = atomic, keyword-addressable topics.** Filename = keyword
  (`select.md` → SELECT); category folder = help category via a csproj glob. This is the
  source of truth for syntax/options/functions and *is* the embedded help.
- **`guides/` = thin hubs.** Audience + mental model + a few workflows, linking down into
  reference and cookbooks. **No restated syntax/option/function detail.**
- **Finding layer = two indexes.** `syntax-index.md` (language/keyword; exists) +
  new **`docs/task-index.md`** ("How do I…" → the exact page).
- **`cookbooks/` = complete runnable examples** (unchanged role).

Rule of thumb enforced everywhere: **a fact lives in exactly one reference page; guides,
indexes, and cookbooks link to it, never re-explain it.**

## 3. Load-bearing constraints (guardrails — violating these breaks the build/help)

1. `docs/reference/**` filenames are keywords. **Renaming or deleting a reference page
   changes/removes a help keyword.** If a page must be renamed, update the csproj `Link`
   (or the glob) and any `LanguageService` keyword mapping in the same change.
2. Category folders are embedded by glob. Moving a page **out** of its category folder
   drops it from help. Moving **within** `functions/**`, `statements/**`, `connectors/**`
   is safe (recursive globs) — so the audit's function-taxonomy moves are safe.
3. Splitting a big *reference* page (e.g. `grammar.md`) is safe as long as each resulting
   page keeps a keyword-named filename under the right category folder. `grammar.md` itself
   is embedded as a junk `grammar` keyword today; decomposing it *improves* help.
4. `guides/**` is NOT embedded → free to split/delete/rename (only fix inbound links).
5. Every restructure step ends green on: `dotnet build` (embed globs resolve) + the docs
   link/coverage validation (section 7) + the help-load smoke test.

## 4. Guide split maps (the "kill the big docs" work)

Each big guide becomes a **thin hub** (~80–150 lines: audience, orientation, "see also")
plus focused pages. Substance moves to `reference/` where it is help-embeddable; only
genuinely narrative/workflow material stays in a guide sub-page.

### 4a. Administration (`administration/platform/README.md`, 1,805 ln) — PRIORITY
Hub: `administration/platform/README.md` → short overview + links.
Move to reference (atomic, several already have homes):
- Install/first-run → `administration/platform/installation.md` (narrative) + `reference/configuration/*` for settings.
- Config files/`.etlsqlformat.json` → `reference/configuration/`.
- Security & secrets (encrypting secrets, JWT secret, orchestrator API key, Governance
  Core, RLS) → `reference/security/*` + link RLS to existing row-level-security material.
- HTTPS/network, state & data roots, **Practical HA**, containerized HA →
  `administration/platform/high-availability.md` (narrative) + `reference/configuration/`.
- Resource controls (lockbox bundles, portal exec, job exec, engine defaults, lineage/
  OpenLineage, snippet templates) → `reference/configuration/`.
- Backup & maintenance, operational checks, monitoring/alerting → `administration/platform/backup-and-monitoring.md`.
- `etl-sql doctor`, **Operator CLI Commands** (§10–11: init, support-bundle, backup/
  restore, upgrade, migrate-database, ha-soak) → **generated `reference/cli/**`** (section 6).

### 4b. Portal Admin (`administration/portal/README.md`, 1,871 ln) — PRIORITY
Hub: `administration/portal/README.md` → overview + links.
- Deployment (Windows/systemd/reverse proxy) → `administration/portal/deployment.md`.
- Configuration Reference (§2, huge) → `reference/portal-admin/configuration.md`.
- User management, roles, enterprise identity/LDAP lifecycle → `reference/portal-admin/users-and-identity.md`.
- Groups & folder ACLs, effective permissions → `reference/portal-admin/permissions.md`.
- Publishing reports, dataset permissions/at-rest-key lifecycle, share links, embed
  tokens, saved views, alerts, env promotion → `reference/portal-admin/*` (one page per area).
- SMTP connections, subscriptions (formats/schedules/delivery semantics) → `reference/portal-admin/subscriptions.md`.
- Extended admin scripting / config export → `reference/portal-admin/scripting.md`.
- Health monitoring, audit log (events/export/guarantees), security model → `reference/portal-admin/*`.

### 4c. Orchestrator / Jobs (`administration/orchestration/README.md`, 1,205 ln) — PRIORITY
Hub: `administration/orchestration/README.md` → overview + links.
- CLI Command Reference (§2: run, ui edit, ui repl, encrypt, session clear, generate,
  gen-script, extract-spec, exit codes) → **generated `reference/cli/**`**.
- Job scheduling (`CREATE JOB`, retry, `SHOW JOBS`/`JOB HISTORY`/`HOST METRICS`,
  `DROP JOB`, cancel, paging) → these are language statements → `reference/orchestrator-jobs/*`
  (already an embedded category) — one page per statement, keyword-named.
- Live files vs published bundles, publishing, bundle inspection → `reference/orchestrator-jobs/bundles.md`.
- Sessions, variable injection, logging, performance tuning, resource governance →
  `reference/configuration/` (settings) + short `administration/orchestration/operations.md` (narrative).
- CI/CD integration, VS Code, DAGs → keep as narrative sub-pages or cookbook recipes.

### 4d. Report SQL (`guides/report-sql.md`, 2,633 ln)
Hub + split into: authoring, visuals (visual pages already live in
`reference/visuals-reporting/visuals/**` — link, don't restate), layout, actions/filters,
datasets, publishing. Visual/report reference pages are embedded → keep as source of truth.

### 4e. Getting Started (`guides/getting-started.md`, 1,882 ln)
Trim hard to a true onboarding narrative (first script, engine mental model, connections,
variables). Everything that is lookup → link to reference. Target ~300–400 lines.

### 4f. Grammar monolith (`reference/statements/grammar.md`, 3,400 ln)
Decompose into focused statement/query/variable pages under `statements/**` (already the
embedded keyword layer), rewrite inbound links, then delete `grammar.md` (no compat
pointer). A generated statement overview can replace its "one big list" role.

### 4g. Standard library (`reference/functions/standard-library.md`, 985 ln)
Demote to an overview/index that links to the per-function atomic pages (already the source
of truth + embedded). Do not restate signatures.

## 5. Task/how-to index — `docs/task-index.md`

Intent-first companion to `syntax-index.md`. Grouped by goal, each row → the exact
reference/cookbook page. Seed groups (derived from guide/cookbook headings):
- **Ingest & move data**: load CSV/Excel/Parquet, DB→DB copy, SFTP/FTP, compress/encrypt files.
- **Transform**: staging, validation, MERGE/upsert, dedup, cleanup.
- **Orchestrate**: schedule a job, retry policy, DAG fan-out/gating, session state, capacity.
- **Secure**: encrypt a connection string, `SECRET:`/`ENC:`, JWT/orchestrator keys, RLS.
- **Operate the server**: install, HA, backup/restore drill, doctor, support bundle, migrate SQLite→Postgres.
- **Portal admin**: add a user, groups/ACLs, publish a report, subscriptions, share/embed, audit.
- **Author reports**: first `.rptsql`, visuals, layout, filters, datasets, publish.
Generation option (later): lint that every task row resolves to an existing page.

## 6. CLI reference — auto-generated from `CliOrchestrator.cs`

Single source: the System.CommandLine tree in `src/ETL-SQL.App/App/CliOrchestrator.cs`.
- Add a small generator (dev-time tool or a `[Trait("Category","Docs")]` test/`dotnet` target)
  that walks the command tree (names, descriptions, arguments, options, defaults, subcommands)
  and emits one page per command under `docs/reference/cli/**` with stable, keyword-named files.
- Emit `reference/cli/README.md` as the command index.
- A verification test asserts generated output == committed pages (fails CI on drift), same
  pattern as the architecture-boundary test — no new runtime dependency.
- Hand-written prose (when to use, examples) lives in a clearly-marked non-generated section
  or a sibling guide sub-page, so regeneration never clobbers narrative.
- Open sub-decision for maintainer: generator as a repo dev-tool vs. a test that writes files.
  Recommend a test-backed generator invoked via a script target so CI can both regenerate and verify.

## 7. Validation / automation (make it stay fixed)

Add a docs-validation test lane (`Category=Docs`, dependency-free, repo-root discovery like
`ArchitectureBoundaryTests`):
- No links to `.worktrees/**/Docs` or old `Docs/` paths (except flagged historical quotes).
- Every dir with >5 md files has a `README.md`.
- Every `reference/functions/**` page has Syntax, Parameters, Returns, Example, References.
- Every embedded reference page resolves (csproj globs ∩ filesystem — catch a moved keyword).
- CLI generated pages match the command tree (section 6).
- No exact-duplicate markdown outside an allow-list.
- Help-load smoke test: `LanguageService` returns help for a sampling of keywords across
  every category (guards the reference↔help coupling during moves).

## 8. Sequenced execution (each phase ends green: build + docs lane + help smoke)

1. **Guardrails first**: add the docs-validation lane + help-load smoke test (section 7) so
   every later move is checked. No content changes.
2. **CLI generator** (section 6): stand up `reference/cli/**` + index + drift test. Unblocks
   admin/orchestrator hubs that currently inline CLI docs.
3. **Admin cluster** (4a/4b/4c): the maintainer's priority — split the three admin guides
   into thin hubs + focused reference, moving CLI bits to the generated pages.
4. **Task index** (section 5): author `docs/task-index.md`; wire from `docs/README.md`.
5. **Grammar decomposition** (4f) + **standard-library demotion** (4g).
6. **Report SQL** (4d) + **Getting Started** (4e) trims.
7. **Function taxonomy move** (audit P1) as a dedicated pass with link rewriting.

## 9. Risks / watch-items

- **Keyword drift**: any reference rename must update csproj `Link`/glob + `LanguageService`
  map atomically. The embed-resolution check in the docs lane is the safety net.
- **Concurrent docs work**: maintainer edits docs live; execution should be branch-isolated
  (worktree) and merged like the v0.16.0 work, and must not clobber in-flight edits.
- **Guide-link churn**: splitting guides breaks inbound links across docs + repo README +
  in-app links; the no-broken-link check must run each phase.
- **Scope**: filling ~400 thin reference pages to template is long-tail; the audit already
  tracks it. This plan sequences the *structure*; page-by-page fill continues under the audit.

## 10. Open questions for the maintainer (next round)

- CLI generator packaging: dev-tool vs test-that-writes (recommend test-backed + script target).
- Admin reference home: one `reference/portal-admin/` + `reference/cli/` + `reference/configuration/`
  split as above, or a dedicated `reference/administration/` umbrella?
- Do getting-started and report-sql keep multi-page guide sub-folders, or collapse to a single
  hub each linking straight to reference?
