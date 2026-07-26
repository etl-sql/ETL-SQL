# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next
actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md` and the release notes
under `docs/releases/`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

### Language — Canonical Syntax and Lifecycle Consistency

**Audit basis (2026-07-26).** Review the complete top-level statement surface after the recent
language expansion: parser dispatch, AST models, formatter output, Report-SQL, Portal and
Orchestrator administration, file operations, reference documentation, samples, snippets, grammar
completion, and parser/runtime tests. The core language already has the right foundation:

```sql
CREATE <object-kind> <name> [AS <implementation-or-definition>];
ALTER <object-kind> <name> ...;
DROP <object-kind> IF EXISTS <name>;
```

Keep object identity before implementation type. Treat `UNIQUE` in `CREATE UNIQUE INDEX` and similar
orthogonal SQL modifiers as valid modifiers, not implementation types. Because the product has no
live users, remove contradictory forms now rather than carrying permanent compatibility grammar.

#### P0 — Unify managed connections across Engine, Portal, and Orchestrator

1. **Delete the separate Portal SMTP language and resource model.** Remove `CREATE SMTP CONNECTION`,
   `SHOW SMTP CONNECTIONS`, and `DROP SMTP CONNECTION`. SMTP is already a normal connector:

   ```sql
   CREATE CONNECTION local_mail AS SMTP(
       HOST = 'smtp.corp.example',
       PORT = 587,
       USERNAME = 'etl-notify',
       PASSWORD = 'SECRET:smtp_password',
       DEFAULT_FROM = 'etl@example.com'
   );
   ```

   Do not preserve the provider-before-object form as an alias. Reuse connector metadata,
   validation, secret handling, testing, redaction, audit, and connection-catalog behavior instead
   of maintaining Portal-only SMTP options, DTOs, storage, export syntax, and handlers.

2. **Add an optional management target to `CREATE CONNECTION`.** A connection without `AT` remains
   session-local. `AT <connection>` registers or updates the named definition in the governed
   connection catalog owned by a `PORTAL` or `ORCHESTRATOR` connection:

   ```sql
   CREATE CONNECTION portal_admin AS PORTAL(
       HOST = 'https://portal.corp.example',
       API_KEY = 'SECRET:portal_admin_key'
   );

   CREATE CONNECTION portal_mail AS SMTP(
       HOST = 'smtp.corp.example',
       PORT = 587,
       USERNAME = 'portal-notify',
       PASSWORD = 'SECRET:portal_smtp_password',
       DEFAULT_FROM = 'reports@example.com'
   ) AT portal_admin;

   CREATE CONNECTION portal_alerts AS WEBHOOK(
       URL = 'SECRET:portal_alert_webhook',
       FORMAT = 'slack'
   ) AT portal_admin;
   ```

   The same syntax must work for Orchestrator-owned failure and operations notifications:

   ```sql
   CREATE CONNECTION orch_admin AS ORCHESTRATOR(
       HOST = 'https://orchestrator.corp.example',
       API_KEY = 'SECRET:orchestrator_admin_key'
   );

   CREATE CONNECTION orch_mail AS SMTP(
       HOST = 'smtp.corp.example',
       USERNAME = 'orchestrator-notify',
       PASSWORD = 'SECRET:orchestrator_smtp_password'
   ) AT orch_admin;

   CREATE CONNECTION orch_failures AS WEBHOOK(
       URL = 'SECRET:orchestrator_failure_webhook',
       FORMAT = 'teams'
   ) AT orch_admin;
   ```

   `AT` must reject targets that are not management-capable `PORTAL` or `ORCHESTRATOR`
   connections. Define whether the statement also opens a local connection; the preferred contract
   is that `AT` performs an audited remote catalog mutation while the unqualified form creates a
   session-local connection. Never copy resolved secret values across the boundary: catalog entries
   store `SECRET:name` references or host-owned protected values under the existing zero-trust
   rules.

3. **Use one managed-connection lifecycle.** Extend the same object-first grammar to remote
   administration:

   ```sql
   ALTER CONNECTION portal_mail WITH (...) AT portal_admin;
   TEST CONNECTION portal_mail AT portal_admin INTO #test_result;
   SHOW CONNECTION portal_mail CONFIG AT portal_admin INTO #config;
   SHOW CONNECTIONS AT portal_admin INTO #connections;
   DROP CONNECTION IF EXISTS portal_mail AT portal_admin;
   ```

   Portal and Orchestrator must share the catalog contract, connector metadata, authorization
   checks, secret-reference validation, redaction, impact analysis, enable/disable behavior, and
   audit vocabulary. Host-specific policy may restrict which connector types can be registered,
   but it must not create a second syntax family. Include SMTP and WEBHOOK in end-to-end tests for
   both hosts, including notification delivery, disabled entries, missing secrets, unauthorized
   callers, `WHAT_IF`, configuration export/import, and fail-closed audit behavior.

4. **Migrate every existing Portal SMTP consumer.** Update subscriptions, alerts, native failure
   notifications, backup/capacity services, configuration export, Portal administration APIs and
   UI, samples, tests, help, snippets, and documentation to resolve a normal cataloged SMTP
   connection. Remove the Portal-specific SMTP entity/API only after all consumers use the unified
   catalog and a data migration converts any development records without exposing credentials.

#### P0 — Correct named report refresh jobs

`CREATE REFRESH JOB FOR REPORT` is incorrect because a report may have multiple independent refresh
jobs. The job name is required identity, not an optional label. Replace the current singleton form
and its generated `portal-refresh:<orchestrator>:<report-id>` key with:

```sql
CREATE REFRESH JOB FinanceNightly
FOR REPORT 'Finance Dashboard'
SCHEDULE '0 2 * * *'
AT orch_admin;

CREATE REFRESH JOB FinanceBusinessHours
FOR REPORT 'Finance Dashboard'
SCHEDULE '*/15 8-18 * * 1-5'
AT orch_admin;
```

Provide a complete name-based lifecycle:

```sql
ALTER REFRESH JOB FinanceNightly
SET SCHEDULE = '0 3 * * *'
AT orch_admin;

SHOW REFRESH JOBS FOR REPORT 'Finance Dashboard'
AT orch_admin
INTO #refresh_jobs;

DROP REFRESH JOB IF EXISTS FinanceNightly AT orch_admin;
```

- Require a name in the parser, AST, Portal/Orchestrator requests, persistence model, catalog
  uniqueness constraint, audit events, configuration export/import, dependency views, UI, and
  documentation.
- Scope uniqueness to the owning Portal/Orchestrator catalog as appropriate; do not key uniqueness
  by report. Store the report relationship separately so many jobs can target one report.
- Creating a second job for the same report must add a schedule, never replace an existing job.
  Duplicate job names must fail unless an explicit supported lifecycle modifier is used.
- Update, enable/disable, history, last/next run, dependency, and deletion operations must identify
  the job by name. Report deletion must define and test cascade/restrict behavior for all attached
  refresh jobs.
- Migrate existing generated singleton jobs to deterministic visible names, report collisions
  before mutation, and update the API/UI assumption that only one refresh job can exist per report.

#### P1 — Make lifecycle modifiers explicit and uniform

1. Publish one capability matrix for every creatable object covering `CREATE`, `CREATE IF NOT
   EXISTS`, `CREATE OR ALTER`, `CREATE OR REPLACE`, standalone `ALTER`, `DROP`, and `DROP IF EXISTS`.
   The parser must reject unsupported combinations immediately; it must never silently discard a
   requested mode or let handlers interpret an unknown mode differently.
2. Canonicalize existence modifiers before the object name:

   ```sql
   CREATE TABLE IF NOT EXISTS #stage (...);
   DROP VIEW IF EXISTS ActiveOrders;
   DROP THEME IF EXISTS corporate;
   ```

   Remove post-name forms such as `DROP CONNECTION name IF EXISTS`.
3. Finish or remove advertised Report-SQL lifecycle forms. `ALTER STYLE`, `ALTER NAVIGATION`, and
   `ALTER DATASET` must not parse and then fail as “not yet implemented.” Add the missing
   `ALTER BUTTON`, `DROP BUTTON`, and any deliberately supported Theme lifecycle. Each `ALTER`
   grammar must patch fields meaningful to that object rather than route every report object
   through a visual-shaped property parser.
4. Make `CREATE OR ALTER` and `CREATE OR REPLACE` semantics identical across parser, AST,
   formatter, engine handlers, Portal authorization, persistence, linting, completion, and docs.
   Add negative tests for every unsupported object/mode pair.

#### P1 — Normalize identity, type, and clause ordering

1. Enforce `&name` for local/report datasets across `CREATE`, `ALTER`, `DROP`, `USE`, `REFRESH`,
   `EXPORT`, and `PUBLISH`. Continue using quoted catalog identity where Portal dataset commands
   require it, but do not let an arbitrary unsigiled identifier accidentally select the local
   lifecycle.
2. Make publish commands identify the published object before the source:

   ```sql
   PUBLISH REPORT 'Monthly Sales' FROM 'reports/monthly.rptsql' ...;
   PUBLISH BUNDLE 'finance-etl' FROM 'bundles/finance' ...;
   PUBLISH DATASET &sales_imported FROM 'transfer/sales.parquet' ...;
   ```

   Remove the source-first `PUBLISH DATASET FROM ... AS &name` exception and update export/import
   round-trip tooling.
3. Use `AS` consistently for a type or definition. Typed objects remain
   `CREATE <object> <name> AS <type>(...)`; definition/property-bag report objects should use one
   shared form, including `CREATE STYLE <name> AS (...)`.
4. Treat tags and lineage as inserted metadata records, not unnamed DDL objects. Canonicalize the
   existing shapes on `INSERT`:

   ```sql
   INSERT TAG FOR TABLE #orders (
       owner = 'Finance',
       classification = 'Confidential'
   );

   INSERT TAG FOR TABLE #orders COLUMN customer_id (
       pii = TRUE
   );

   INSERT LINEAGE FOR TABLE #orders
   FROM 'openlineage/orders.json';
   ```

   Retire `CREATE TAG`, `CREATE LINEAGE`, and the duplicate bare `TAG ... WITH (...)` statement.
   Route the new forms through `INSERT` dispatch without confusing them with row DML, preserve the
   existing typed tag-catalog validation and zero-trust lineage source handling, and update lineage
   capture, formatter, linting, completion, help, snippets, samples, and documentation together.
   Tags and lineage do not participate in the `CREATE`/`ALTER`/`DROP` object lifecycle matrix.
5. Reserve compound object kinds such as `SHARE LINK`, `SAVED VIEW`, and `EMBED TOKEN` for genuine
   resources with clear identity and lifecycle. Do not encode an implementation type before
   `CONNECTION`.

#### P1 — Normalize inspection and target clauses

1. Use `AT <connection>` consistently for host/connection targeting. Align parser, formatter, help,
   snippets, and docs on `SHOW TABLES AT <connection>`; remove the parser-only
   `SHOW TABLES ON <connection>` form.
2. Decide and implement one truthful tag inventory surface. If global enumeration is supported,
   implement documented `SHOW TAGS [INTO #table]`; otherwise remove it from the docs and require
   `SHOW TAGS FOR SCRIPT` or `SHOW TAGS FOR TABLE <name>`.
3. Apply one inspection ordering rule: `SHOW <object-or-collection> [qualifier] [target]
   [filter] [AT <management-connection>] [INTO #table]`. Reconcile special cases such as connection
   config, report history/dependencies, bundle versions/files, refresh jobs, and effective
   permissions against that rule.
4. Correct Portal share/embed syntax drift. Parser, formatter, docs, and configuration export must
   agree on one expiration clause; prefer structural `EXPIRES <timestamp>` over a second
   `WITH(EXPIRES_AT=...)` spelling.

#### P2 — Retire duplicate surface syntax

Choose and document one canonical spelling for each operation, then remove unused compatibility
forms before the first supported release:

| Keep canonical | Retire duplicate forms |
| :--- | :--- |
| `SEND EMAIL ...` | `SEND_EMAIL(...)` |
| `SEND FILE ...` / `RECEIVE FILE ...` | `SEND_FILE(...)`, `RECEIVE_FILE(...)`, `FILE_SEND`, `FILE_RECEIVE` |
| `COPY FILE`, `MOVE FILE`, `DELETE FILE`, etc. | `COPY_FILE(...)`, `MOVE_FILE(...)`, `DELETE_FILE(...)`, and sibling underscore forms |
| `CREATE DIRECTORY`, `DELETE DIRECTORY`, etc. | `CREATE_DIRECTORY(...)`, `DELETE_DIRECTORY(...)`, and sibling underscore forms |
| `FOREACH` | `FOR EACH` |
| `WAIT UNTIL <condition>` | `WAITFOR (<condition>)`; retain `WAITFOR DELAY` and `WAITFOR TIME` for their distinct time forms |
| One schema-inspection command | Redundant aliases among `SHOW COLUMNS FOR`, `SHOW SCHEMA FOR`, and `DESCRIBE` |

Generated scripts, samples, snippets, formatter output, autocomplete, hover help, docs, and error
messages must emit only the canonical forms. If a short migration window is retained, every alias
must produce a deprecation diagnostic with an exact replacement and a declared removal release;
do not describe both forms as equally canonical.

#### P2 — Restore parser/formatter/documentation round-trip guarantees

1. Give every executable statement a real `ToSql()` serialization. Report-SQL and Portal
   statements must never fall through to `UNKNOWN STATEMENT`; formatter output must parse back to
   an equivalent AST and preserve lifecycle mode, identity sigils, target host, and security
   clauses.
2. Add a generated statement-surface inventory from parser/AST metadata and fail CI when a
   creatable object lacks its declared lifecycle, formatter coverage, grammar completion, help,
   snippet where applicable, or reference page.
3. Add table-driven parser → formatter → parser tests for every canonical form and explicit
   rejection tests for every retired form. Include `IF EXISTS` position, creation modes, dataset
   sigils, `AT` targeting, named refresh jobs, SMTP/WEBHOOK catalog registration, and all
   Report-SQL object families.
4. Parse every copy-pasteable documentation and sample block in its correct execution context.
   Specifically reconcile `SHOW TABLES`, `SHOW TAGS`, Theme deletion, Portal share/embed expiry,
   Report-SQL lifecycle claims, managed connections, and refresh-job examples.
5. Update `docs/syntax-index.md`, statement references, connector references, administration
   guides, architecture contracts, help resources, snippets, migration guide, samples,
   configuration export, LSP grammar, and release notes as one atomic language change.

**Definition of done.** A user can predict a statement from the object model: the verb names the
operation, the object kind precedes its identity, implementation type follows `AS`, remote ownership
follows `AT`, and lifecycle modifiers occupy one position with one meaning. Portal and Orchestrator
reuse normal SMTP/WEBHOOK connections, multiple named refresh jobs can target the same report, no
unsupported lifecycle parses successfully, and every canonical statement round-trips through the
formatter and documentation test lane.

### Workstation-to-Enterprise — Data Quality and Stewardship

**Review basis (2026-07-26).** The underlying small-scale capabilities are stronger than the current
product story suggests:

- `@expect` / `@fail` rules and `ON FAILURE WARN | QUARANTINE | THROW` execute in the engine and do
  not require either host.
- `ASSERT JOB` evaluates current-run row count, null percentage, freshness, quarantine percentage,
  and warning percentage. It can fail a CLI/CI run directly; historical baselines and transition
  alerts become available when the script runs through Orchestrator.
- The single-node Orchestrator already uses local SQLite by default and persists job history,
  warning/quarantine totals, compact per-rule failure counts, and structured per-column metrics.
- `SHOW DATA QUALITY RULES` exposes the rules recorded by the current execution.
- `SHOW LINEAGE HISTORY FOR MISSING TAGS` audits required `@owner`, `@steward`, `@contact`,
  `@classification`, and `@quality` metadata locally or `AT` an Orchestrator/Portal connection.
  `SHOW PROTECTED DATA [SUGGESTIONS]` provides the corresponding protected-data inventory.

The gap is not a second data-quality engine. It is a coherent operator-facing read model between a
single script's result and the full Portal governance workflow. A one-person shop should be able to
answer "Is my data healthy?", "What failed?", and "What metadata is missing?" from the CLI,
Orchestrator, or a generated report, using the same durable evidence the Portal later presents.

**Product invariant.** Portal is a presentation, collaboration, and remediation layer—not a
prerequisite for data quality or stewardship. The progression must remain additive:

1. **Workstation:** source-controlled rules and tags, current-run assertions, terminal results, and
   a non-zero process exit when a critical gate fails.
2. **Local Orchestrator:** optional SQLite-backed scheduling, history, baselines, tag/lineage
   catalog, reports, and SMTP/WEBHOOK notifications with no Portal deployment.
3. **Enterprise:** the same records and calculations gain Portal queues, assignments, approvals,
   access control, remote audit, PostgreSQL/HA, and organization policy. Moving up a tier must not
   require rewriting rules, tags, assertions, or score definitions.

#### P0 — Ship a no-Portal quality status surface

1. Add a structured status command:

   ```sql
   SHOW DATA QUALITY STATUS
     [FOR JOB <job_name>]
     [SINCE <duration>]
     [AT <orchestrator_connection>]
     [INTO #table];
   ```

   With no `AT`, it reports the current run or the injected local Orchestrator store when one is
   available. The result must include job/run identity, time, status, rows processed, warned and
   quarantined counts and percentages, failed-rule count, freshness state, and error summary. It
   must consume structured history fields rather than parse display prose.
2. Add the drill-down paired with that summary:

   ```sql
   SHOW DATA QUALITY FAILURES
     [FOR JOB <job_name>]
     [SINCE <duration>]
     [AT <orchestrator_connection>]
     [INTO #table];
   ```

   Return one row per run, target, column, rule, and action with the failure count. Persist a
   normalized rule-failure record where necessary; keep the compact history string only as a
   compatibility/display field. Do not persist or return sample values.
3. Expand `SHOW JOB HISTORY` to include its already-persisted `RowsQuarantined`, `RowsWarned`, and
   data-quality summary fields, or make the status command the documented quality projection over
   that history. Both commands must agree on run identity and status.
4. Preserve `ASSERT JOB` as the executable gate. Document the zero-service pattern for Task
   Scheduler/cron/CI and the local-Orchestrator pattern for history, baselines, recovery
   notifications, and scheduled execution.
5. Route optional alerts through the canonical managed connections from the language-consistency
   phase:

   ```sql
   CREATE CONNECTION local_orch AS ORCHESTRATOR(...);
   CREATE CONNECTION quality_mail AS SMTP(...) AT local_orch;
   CREATE CONNECTION quality_hook AS WEBHOOK(...) AT local_orch;
   ```

   A workstation user may omit notifications entirely; notifications must not be required to
   obtain a failing exit code or query the result.

#### P1 — Add transparent stewardship scoring

1. Add a score command over the existing lineage/tag catalog:

   ```sql
   SHOW STEWARDSHIP SCORE
     [FOR JOB <job_name> | TABLE <table_name> | SCRIPT <script_path>]
     [AT <orchestrator_connection>]
     [INTO #table];
   ```

   Report the numerator, denominator, and percentage for each component—not only a badge or opaque
   composite. Initial components should include required-tag completeness, protected-data
   ownership/classification coverage, and data-quality-rule coverage. Include asset/column counts
   and the evaluation time so the score is reproducible and auditable.
2. Make scoring policy explicit and source-control friendly. Default required tags may remain
   `@owner`, `@steward`, `@contact`, `@classification`, and `@quality`, but an organization must be
   able to declare required tags, scope, exclusions, and any weights in normal policy
   configuration. If no weighted policy exists, show component percentages without inventing a
   composite score.
3. Use `SHOW LINEAGE HISTORY FOR MISSING TAGS ... INTO #gaps` as the detail query behind tag
   completeness rather than introducing a synonymous "gaps" command. Score totals and missing-tag
   rows must reconcile exactly.
4. Treat tag source locations as the remediation path. Results should retain script path and line
   when known so a solo operator fixes the source-controlled `INSERT TAG` / `INSERT LINEAGE`
   statement instead of editing an isolated governance database.
5. Make local and remote calculations identical. The CLI, local Orchestrator, Portal API, and
   Portal dashboard must share one scoring service and versioned score definition; the Portal must
   never calculate a more favorable score from browser state or demo records.

#### P2 — Provide runnable operator reports and a small-shop starter path

1. Ship source-controlled `.rptsql` templates for:
   - **Data Quality Health:** latest status, recent failures, warning/quarantine trends, freshness,
     and jobs with no recent successful run.
   - **Stewardship Scorecard:** component scores, missing required tags, protected assets without
     ownership/classification, and rule-coverage gaps.
2. Build the templates from `SHOW ... INTO #table` statements so they run locally in the Report
   Player or on a schedule through Orchestrator. Portal publishing is optional and must not change
   the report's queries or meaning.
3. Add a copy-pasteable "one-person quality loop" guide and sample:
   define tags and column rules in the pipeline, run `ASSERT JOB`, schedule it in the local
   Orchestrator, inspect the two reports, and optionally send failure/recovery notifications through
   saved SMTP/WEBHOOK connections.
4. Add fixtures and acceptance tests for all three deployment rungs. Given the same run history,
   lineage catalog, and policy, workstation/local-Orchestrator queries and Portal APIs must return
   the same counts and scores. Cover empty history, first run, clean run, warning, quarantine,
   critical failure, stale data, missing tags, protected unowned data, and recovery.
5. Apply zero-trust output rules throughout: redact connection/secret material, never include
   failed sample values in history or alerts, enforce identity/policy when querying a remote
   Orchestrator, and preserve counts-only behavior unless a separately authorized quarantine target
   is opened.

**Definition of done.** A user with only the CLI can enforce rules and fail automation; a user with
the default single-node SQLite Orchestrator can schedule those scripts, query durable quality
history, audit tag completeness, obtain transparent stewardship scores, run health/scorecard
reports, and optionally receive SMTP/WEBHOOK notifications without installing Portal. Deploying
Portal or HA reuses the same scripts, records, formulas, and reports while adding collaboration and
enterprise controls.

### Portal — Comprehensive Product and UX Update

**Review basis (2026-07-26).** Walked the production Portal in both a local development host and
the repository Docker image, at desktop and 390px mobile widths. The review covered login, report
catalog and search, publishing, parameterized execution, subscriptions, the visual designer,
governance, lineage, documentation, Orchestrator status, and all eleven administration areas. It
also cross-checked the browser behavior against the controllers, UI source, and current test
guidance.

The Portal has a strong foundation worth preserving: the desktop card/table visual language is
coherent, focus treatment and empty states are generally clear, report publishing and script
validation work end-to-end, and the designer, lineage, connection catalog, secret store, policy
authority, subscriptions, datasets, ACLs, and Orchestrator integration expose substantial product
depth. The next update should make that depth feel like one trustworthy product rather than add
another set of isolated capabilities.

#### P0 — Restore trust in the critical journeys

1. **Make browser/API contracts explicit.** The Admin Users screen currently fails after a
   successful API response because `UserDto.Username` serializes as `username` while
   `admin.html` reads `userName`. The same class of drift should not remain a runtime discovery:
   publish an OpenAPI or generated TypeScript contract, validate critical responses at the client
   boundary, and cover login → users → folders → report publish/run with a real browser test.
2. **Fix identity presentation everywhere.** The shared header renders JWT `sub` before
   `unique_name`, so the signed-in user appears as an internal numeric ID (for example, `1`) on
   Reports, Admin, Docs, and Orchestrator. Use one session identity model and shared shell component;
   audit rows should display the same recognizable identity.
3. **Never present demo governance records as evidence.** A fresh installation currently reports
   a governance score, active bypasses, named glossary stewards, badges, and settings sourced from
   the prototype's demo/browser state. Ship real authorized APIs and explicit unavailable/empty
   states, or hide unfinished routes. The detailed durability work remains in
   [Portal — Governance Dashboard](#portal--governance-dashboard).
4. **Make parameterized report execution one understandable flow.** Before a snapshot exists, keep
   the report name as the page heading, collect required parameters before submitting work, use one
   unambiguous Run action, and show the resulting job through a terminal state. The current flow
   first runs a preparation step, labels the embedded parameter form `Ready`, then asks the user to
   run again. Disable export/subscription actions until their prerequisites exist and give every
   embedded input an accessible name.
5. **Make the primary shell responsive.** At 390px the global navigation and Admin workspace clip
   beyond the viewport; the Reports hamburger only controls the folder sidebar and leaves the page
   underneath interactive. Collapse the global nav, use a modal drawer with overlay/focus
   containment, and provide responsive table, form, tab, and action patterns for Reports, Admin,
   Governance, Docs, and Orchestrator.
6. **Split authoring authority before promoting Studio.** The current editor is gated by the
   Designer module and `Admin,Publisher`; source read/save/commit additionally require report
   `Manage`. This lets the same broad authority edit active source, commit it, and—when
   `PushOnSave` is configured—push it. Introduce explicit authoring capabilities and enforce them in
   every API before exposing Studio in global navigation. Hiding buttons is not authorization.

#### P1 — Connect the product into coherent workspaces

1. **Consumer home and global discovery.** Surface the existing consumer-home and fuzzy catalog
   APIs as a useful landing page: favorites, recent, featured, popular, and one global report
   search. Report cards should use intentional thumbnails/icons and one concise last-run/last-viewed
   status instead of repeating `Not run`, `Never run`, and `Awaiting first run`.
2. **Promote the script editor to a first-class Studio.** Add a top-level **Studio** destination for
   authorized Admins and Publishers when the Designer module is enabled. It should open a
   catalog-scoped authoring home for creating or editing `.rptsql` reports, with the existing script
   editor and visual designer as equal Code and Design modes. This is especially important for a
   closed SaaS deployment where Portal authoring is the approved path and outside files, raw upload,
   or source-control write-back are disabled. Keep the existing interactive trust boundary:
   ACL-filtered `SHARED:` connections, read-only queries plus `#temp` staging, server-enforced
   limits, and no script-supplied credentials or arbitrary connection creation. Make the navigation
   and every authoring API disappear when authoring is disabled; do not rely on hiding the menu.
   Define an explicit deployment policy such as `Disabled`, `CatalogOnly`, or `SourceControlled`
   rather than treating the current Designer and upload/source-control switches as unrelated
   settings.
3. **Administration and operations hub.** Add visible, role-gated workflows for the backend
   capabilities that currently have no coherent browser home: service accounts and secret rotation,
   pending access approvals, anonymous share/embed inventory, fleet/node status, operational
   metrics, and administrative service runs. Join these with health, audit, outbox, and Orchestrator
   context so an operator can move from a symptom to the responsible job or node.
4. **Surface departmental environments without weakening their isolation.** The shipped
   departmental model is deployment isolation, not shared-table multitenancy: every department owns
   a separate Portal database/login, Orchestrator database/login, artifact root, key ring, signing
   and dataset keys, service identity, and network endpoint. Multiple department instances may run
   on the same physical Portal hosts or HA server pool, but they must not share a Portal process,
   database, artifact namespace, key ring, or service identity. Add an Admin **Environments**
   workflow that can generate and validate a deployment plan from an environment id, show isolation
   verification evidence, and link to the read-only fleet status. Provisioning must go through a
   separately authorized deployment control plane or an exported deployment package—not through a
   `FleetReader` credential and not by granting one department access to another. An optional
   environment chooser may list only environments the signed-in identity is entitled to enter;
   each selection establishes that environment's own session and must never merge report catalogs,
   search results, datasets, connections, secrets, or authoring history.
5. **Finish the data-steward journey.** Keep the real lineage and quarantine views, make
   Stewardship and Audit genuine routes, and connect disposition/replay submissions to job status.
   Add rule visibility and structured failure trends. Governed quarantine row access is specified
   separately in [Portal — Quarantine Row Access](#portal--quarantine-row-access).
6. **Use one documentation renderer.** Docs and connector Help currently expose raw Markdown table
   pipes, admonition markers, and code fences. Use a shared, sanitized renderer with consistent
   headings, tables, admonitions, code blocks, links, topic search, and copy actions.
7. **Use one feedback and dialog system.** Replace native `alert`, `prompt`, and `confirm` calls
   across Reports, Admin, Governance, Designer, Orchestrator, and report runtime with accessible
   toasts and purpose-built dialogs. Password reset, destructive changes, policy rollout, and
   source-control commits need structured validation, clear impact text, and auditable outcomes.
8. **Polish the visual designer without reducing its power.** Group or search the long visual
   palette, replace the rainbow of equally weighted buttons with clearer hierarchy, label the
   icon-only toolbar, improve dataset/on-page empty states, and make the canvas/inspector useful at
   laptop and tablet widths.

#### Studio authorization model

Studio permissions should overlay resource ACLs; they should not turn `Publisher` or folder
`Manage` into an all-purpose development credential.

| Action | Required authority |
| :--- | :--- |
| Discover/open Studio | Designer module + deployment authoring policy + `StudioAccess` |
| Read report source | `ScriptRead` + report/folder `Author` or `Manage` |
| Analyze, complete, and render a preview | `ScriptPreview` + source-read authority |
| Run an interactive selection | `ScriptRun` + source-read authority + existing shared-connection ACLs; retain the read-only/`#temp` execution policy |
| Save a draft | `ScriptSave` + report/folder `Author` or `Manage`; saving must not publish, commit, or push implicitly |
| Publish or replace the active report version | `ReportPublish` + target-folder authority |
| Upload/import outside source | `ScriptIngress`; disabled in the catalog-only SaaS profile |
| Commit to repository | `SourceCommit` + source-save authority; record actor, revision, diff summary, and correlation id |
| Push or promote a branch | `SourcePush` or a deployment service identity; separate from commit and disabled by default |

Add an `Author` resource grant so report editing does not require permission to change ACLs, move or
delete reports, or administer the entire folder. Capabilities should be assignable to groups and
service accounts, included in effective-permission diagnostics, auditable, deny-by-default for new
tenants/environments, and tested as a matrix across Viewer, Author, Publisher, Approver, and Admin.
For controlled production, support a draft → review/approval → publish/commit/push workflow with
optimistic concurrency, protected branches, and separation of duties.

#### Enterprise administration coverage audit

The enterprise guides are strong on command-line and configuration runbooks, but several shipped
Portal APIs and health contracts have no corresponding browser workflow. Portal coverage should be
added where the operation is safe while the Portal is online; host-bootstrap, recovery, and
containment operations must remain out-of-process.

| Enterprise area | Current Portal coverage | Portal update and boundary |
| :--- | :--- | :--- |
| OIDC and LDAP identity | Federated login, provisioning, and group sync are implemented; OIDC diagnostics is an API, while provider configuration is file/environment based. | Add identity-provider status, callback/issuer reachability, claim and group-mapping test results, sync health, and break-glass readiness. Never return client secrets; stage high-risk configuration changes and show restart impact. |
| Service accounts | CRUD, secret rotation, revoke, scoped-token issuance, and middleware enforcement exist as APIs; no Admin page exists. | Add a Service Accounts page with scope, expiry, last use, owner, rotate/revoke, one-time secret display, and audit history. |
| Policy authority and machine registry | The Admin Policy Authority page covers validation, publication, activation, canaries, rollback, and machine registration/revocation. | Preserve this as the model enterprise surface; add fleet impact, approval/separation-of-duty state, collector consequences, and links from affected machines to policy history. |
| Host enrollment | `etl-sql enterprise enroll/status/unenroll` is an elevated host command; the Portal registers the corresponding machine identity. | Show enrollment and registration consistency, expiry, certificate posture, and remediation instructions. Keep enrollment/unenrollment on the host because it owns an OS-protected bootstrap and is intentionally outside lower-authority Portal configuration. |
| Secrets and shared connections | Strong Admin pages already support write-only secrets, masked connections, verify, enable/disable, impact, ACLs, and metadata promotion. | Retain and integrate them with Studio capability checks, policy findings, rotation due dates, and cross-environment promotion plans. |
| Audit outbox and security-event delivery | Audit rows are visible; outbox and security-event diagnostics are emitted through health, Prometheus, and fleet status, but have no operator workspace. | Add collector status, pending/failed counts and bytes, oldest age, last attempt/success, fail-closed threshold state, and a redacted test-delivery workflow. Security-event collector configuration remains signed organization policy. |
| Native failure, backup, and capacity services | `api/admin/services` and per-service history exist; configuration is file based and the UI has no page. | Show enablement, schedule, recipients/SMTP alias, last/next run, outcomes, and history. Use staged configuration with validation and an explicit apply/restart contract where live reload is unsupported. |
| Backups and restore drills | Split-custody backup/restore and validation are CLI operations; the native backup service records age/failure evidence. | Show last successful backup, freshness policy, archive/manifest identifiers, validation and restore-drill evidence, and alerts. Keep backup custody, restore, and destructive recovery outside the running Portal. |
| Doctor and support bundles | `etl-sql doctor` and redacted support-bundle generation are CLI-only. | Add an Admin Diagnostics page for the online-safe checks and an audited, redacted support bundle with an explicit review-before-download step. Keep the CLI path as the recovery option when Portal is unavailable. |
| HA, fleet, migrations, and upgrades | Readiness, node heartbeats, fleet status/aggregation, compatibility metadata, migration ownership, and upgrade reports exist in backend contracts; no unified Portal view exists. | Add the read-only Fleet/Operations workspace, upgrade preflight/postflight evidence, node divergence, drain guidance, and migration owner/status. Package deployment, database migration, HA soak, and traffic control remain external operator actions. |
| Dataset at-rest keys | Rotation and recovery behavior are documented and an Admin rotation endpoint exists; no Admin workflow exists. | Add key-version inventory, preflight, impact count, guarded rotation, progress/failures, post-rotation verification, and rollback instructions without ever displaying key material. |
| Configuration export and promotion | A secret-free configuration export API and script-first replay path exist; no guided Portal workflow exists. | Add export, target-plan validation, diff, unsupported-resource summary, approval, and audit. Do not turn it into database backup or move secrets/datasets between isolated environments. |
| RLS and effective access | Report/folder effective permissions are queryable and row filtering is enforced at execution; the Portal does not provide one complete access explanation. | Add an identity access simulator that explains role, group, folder/report ACL, connection grant, Studio capability, and RLS outcome without returning protected rows. |
| Departmental isolation | Deployment templates and verification enforce separate environments; the Portal exposes only read-only fleet status. | Implement the isolation-safe Environments workflow described above, while keeping cross-environment provisioning in a separately authorized deployment plane. |

#### P1 — Accessibility and visual-system completion

- Consolidate the duplicated page headers, identity display, module gating, theme control, spacing,
  icons, status chips, errors, loading states, and empty states into a shared Portal shell and
  component vocabulary. Avoid mixing product icons, CSS glyphs, and emoji as primary controls.
- Give dialogs `role="dialog"`, `aria-modal`, an accessible name, focus trap/restoration, and
  correct hidden-state behavior. Closed Governance modals and the Orchestrator detail drawer must
  not remain in the accessibility tree.
- Name report search, favorite actions, script-picker rows, and report-parameter controls; support
  keyboard activation and arrow-key behavior for tabs, trees, palettes, tables, and card actions.
- Verify light, dark, forced/high-contrast, reduced-motion, 200% zoom, and narrow-viewport behavior
  without horizontal page clipping or information conveyed by color alone.

#### P2 — Browser quality and delivery guardrails

1. Add an automated browser lane; the current testing guide explicitly records that Portal and
   report-runtime JavaScript have none. Cover Chromium desktop and a narrow viewport, at minimum,
   with seeded Viewer, Publisher, Steward, Operator, and Admin journeys.
2. Add accessibility assertions (including no hidden modal content), visual snapshots for the
   shared shell and critical empty/error/data states, and request/response contract fixtures.
3. Run the same smoke suite against `dotnet run` and the production Docker image. Treat console
   errors, unhandled promise rejections, broken Markdown, demo-data fallback, and horizontal page
   overflow as failures.
4. Keep the manual UI sandbox for fast component development, but make its representative stories
   fixtures for the automated lane rather than a separate source of truth.
5. Tighten container build hygiene so generated Portal review data and repository build outputs do
   not inflate the Docker context, and document a small seeded review profile for repeatable product
   acceptance.

#### Suggested sprint sequence

1. **Shell and contracts:** shared identity/navigation/theme shell, Admin Users fix, generated API
   contract, and first end-to-end login/admin smoke.
2. **Report consumer flow:** consumer home/search, report-card cleanup, parameter preflight,
   execution status, prerequisites, and accessible report runtime controls.
3. **Studio authoring:** granular authoring capabilities and `Author` resource grants first, then the
   top-level Studio, catalog-only SaaS policy, Code/Design modes, authoring home, review/promotion
   flow, and end-to-end create/edit/validate/run/save/publish/commit coverage.
4. **Responsive and accessible foundations:** mobile shell, responsive Admin patterns, semantic
   dialogs/drawers, keyboard/focus work, and shared feedback components.
5. **Governance, enterprise operations, and environments:** remove demo evidence, finish
   steward/audit routes, connect job status, implement the enterprise coverage matrix above, and add
   the isolation-safe departmental environment workflow.
6. **Docs and designer polish:** shared Markdown renderer, designer hierarchy/discoverability, and
   final visual consistency pass.
7. **Architecture and administration documentation:** after the implementation and contracts have
   stabilized, reconcile `Docs/Architecture/Portal.md`,
   `docs/architecture/decisions/Departmental_Isolation.md`, the Portal administration guides, API
   inventory, module/authoring policy matrix, HA diagrams, isolation threat model, and deployment
   verification runbook with the shipped behavior. Architecture diagrams and interface contracts
   must be checked against the final C# source rather than copied from this roadmap.
8. **Release gate:** browser, accessibility, responsive, local/Docker parity, departmental
   isolation, and role/module/authoring-capability/policy acceptance runs.

**Definition of done.** A first-time Viewer can find and run a parameterized report without
instruction; a Publisher can validate, publish, design, and diagnose it; a Steward sees only real,
durable governance evidence and can follow remediation work to completion; and an Admin/Operator
can identify users, services, nodes, access requests, and failures without dropping to direct API
calls. Those journeys pass with keyboard-only use at desktop and 390px widths, under both the local
host and production container, with no native browser dialogs, hidden interactive content, demo
fallback, uncaught client errors, or horizontal page overflow. In a catalog-only SaaS profile,
authorized authors can work entirely in Studio while outside script ingress is rejected; in a
departmental topology, a cross-environment identity cannot discover or access another environment's
reports, datasets, connections, secrets, artifacts, or authoring state.

### Portal — Governance Dashboard

Finish the data-steward-first dashboard described in
[`Governance_Dashboard_Strategy.md`](docs/architecture/roadmaps/Governance_Dashboard_Strategy.md).
The current production module is a visual prototype: it substitutes demo assets when the
stewardship API fails and keeps findings, decisions, glossary terms, badges, scans, and scoring
settings only in browser memory.

Replace those placeholders with authorized, audited, durable Portal APIs. The work is complete only
when role and API tests cover the mutation boundaries, UI tests cover live and failure states, and
the production surface never presents demo records as governance evidence.

### Portal — Quarantine Row Access

**Problem.** `DataQualityController.GetQuarantineRows` runs `SELECT * FROM {target}` inside a fresh
in-process `ExecutionSession`. That session is constructed with an empty connection dictionary and
never calls `Evaluator.LoadSessionState`, so it restores nothing from the producing run: no
connections, no temp tables, no session variables. Every real capture target therefore fails —
a connection-qualified target (`warehouse.dbo.quarantine_users`) raises `Unknown source: warehouse`,
and a `#temp` target is silently auto-created as an empty in-memory table, which is worse: the
steward reads "no rows" as "nothing was quarantined". Pre-projection capture plus in-Portal editing
is the strongest part of the remediation workflow, and it is unavailable exactly where quarantine
data actually lives.

The current queue marks these targets **View only**, explains why, and provides review SQL to run
where the connection exists. The remaining product gap is governed, in-Portal access to durable
catalog-backed targets.

**Chosen direction: catalog-backed preview.** Resolve the target through the shared connection
catalog rather than widening the Portal's reach generally.

| Option | Verdict |
| :--- | :--- |
| Rehydrate the producing job's `SessionState` into the preview session | Rejected — restores *every* connection an arbitrary job held, with no bound tied to the manifest, and the state may no longer exist. |
| Resolve the target's connection from the catalog as `SHARED:alias` | **Chosen** — governed path; flows through `SharedConnectionExpander` → `ConnectionSecretResolver` → `ConnectorPolicyAuthorizer`, so policy, secret resolution, and redaction all apply unchanged. |
| Round-trip the read through the orchestrator as a job and return its result set | Deferred fallback — covers ad-hoc script connections the catalog does not know, but needs a result-returning job path and turns an interactive read into an async one. |

Slices:

1. **Manifest provenance.** Add nullable `TargetConnectionAlias`, `TargetConnectorType`, and
   `TargetIsCatalogBacked` to `QuarantineReplayManifest`, written at capture time. Backward
   compatible in the same way the replay-mode fields were: absent means "unknown", which classifies
   as view-only.
2. **Readability consults the catalog.** `QuarantineTargetReadability` gains an
   `IConnectionCatalogProvider` and the caller's `ExecutionIdentity`, and reports readable only when
   the alias resolves, is enabled, and the caller is authorized for it. Every other case keeps its
   existing reason string, so the interim UI needs no change.
3. **Preview session bootstrap.** Prepend
   `CREATE CONNECTION {alias} AS {type}('SHARED:{alias}');` to the preview script. The alias comes
   from the manifest, never from the request, and the statement is still only
   `SELECT * FROM {manifest target}` — not arbitrary SQL. Keep the 15s timeout,
   `MAX_LAST_RESULT_ROWS`, the RLS execution identity, and `SecretRedactor` on the error path.
4. **Kill switch and audit.** Gate the whole path behind `Portal:DataQuality:AllowConnectionPreview`
   (default **off**, so an upgrade does not silently start opening production connections from the
   web tier), and audit each preview read the way dispositions are audited today — reading raw
   quarantined source rows is an access event, not a page view.
5. **Tests.** A **happy-path** read is the first requirement, not the last: every existing
   `quarantine/rows` test asserts a rejection, so the catalog-backed path needs positive coverage
   before it can be considered functional. Then: catalog miss, disabled entry, feature switch off,
   unauthorized identity, and a redaction assertion on the failure path.
6. **Docs + sandbox.** Administration guide: which connections become previewable, what the switch
   does, and what is audited. Flip the sandbox's view-only fixture to a readable catalog-backed
   target so both states stay developable
   (`tools/ui-sandbox/stories/data-quality-queue.story.js`).

Open decision for the sprint: whether a steward reviewing rows through a catalog connection should
be limited to connections their own role can already reach, or whether `DataQualityStewardAccess`
plus a manifest-bound target is authority enough. This changes slice 2's authorization check.

### Portal — Data Quality Follow-through

These lower-level data-quality findings support the comprehensive update above. Ordered by how much
each affects day-to-day use.

1. **Submitted jobs disappear.** Replay and disposition actions report
   `Disposition job {id} submitted` and stop there — no status, no link into job history. A steward
   cannot tell whether the release they just made actually applied. The queue should follow the job
   to a terminal state, or at minimum link to it.
2. **The trend panel re-parses a display string.** `ParseRuleFailures` reconstructs per-rule
   failures by splitting the `DataQualityFailures` history payload on `;`, `:`, and `=`. That format
   exists for humans reading run history; it already needed careful handling because rule text
   contains both `:` and `=` (a `MATCHES` regex). v2 records per-column run metrics — the trend
   should read those instead of parsing prose.
3. **No rule visibility in the Portal.** `SHOW DATA QUALITY RULES` is an engine statement only, so a
   steward who lives in the Portal cannot see which rules protect which columns — the thing they
   most need when a quarantine rate jumps. Wants a read-only endpoint plus a panel beside the trend.
4. **Every preview spins a full engine.** Each request lexes, parses, lints, and evaluates through a
   new `ExecutionSession`. Acceptable at current volume; worth revisiting before any endpoint like
   this becomes a polled or dashboard-refreshed surface.
