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

**Design decisions taken 2026-07-27 — these supersede the `AT` proposal in item 2 below.**

- **Targeting uses the existing `EXECUTE <portal_conn> BEGIN … END` block, not a new `AT` clause.**
  `AT` was only ever shorthand for something the language can already express, and
  `ExecuteRemoteBlockStatementHandler` already forwards every inner statement to the Portal
  dispatcher. Item 2's `AT` syntax is therefore **withdrawn**; item 3's `... AT portal_admin`
  examples become `EXECUTE portal_admin BEGIN ... END`.
- **Scope is every connector type, not just SMTP.** Inside a Portal admin block,
  `CREATE CONNECTION <name> AS <connector>(…)` registers a governed entry in the catalog. WEBHOOK
  needs exactly this (it substitutes for SMTP), so restricting the first pass to SMTP would only
  buy a second migration.
- **The store is the existing `PortalSharedConnection` catalog; the bespoke `SmtpConnection` table
  is deleted.** This is the substance of the change, not the spelling. `SmtpConnection` holds an
  `EncryptedPassword` — a credential **value** — while `PortalSharedConnection` enforces
  `SECRET:` **references** on write, encrypts target/options at rest, and carries ACLs, ownership,
  a usage ledger and versioning. Portal SMTP is currently the one path that stores a secret value,
  bypassing the zero-trust rule the shared catalog exists to enforce.
- **Migration drops existing passwords and forces re-entry.** There is no honest automatic
  conversion from an encrypted value to a `SECRET:` reference, and nobody is using this yet.

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

   **Progress 2026-07-27 — language done, storage NOT yet migrated.**

   Done:
   - `CREATE SMTP CONNECTION` / `DROP SMTP CONNECTION` retired; both reject with a diagnostic
     naming the canonical replacement (they differed in three ways at once — string alias vs
     identifier, `WITH` vs `AS`, `FROM_ADDRESS` vs `DEFAULT_FROM` — so a generic parse error
     would have left all three to guess at).
   - `CreatePortalSmtpConnectionStatement` / `DropPortalSmtpConnectionStatement` deleted.
   - `PortalDataSource` routes `CreateConnectionStatement` / `DropConnectionStatement` inside an
     `EXECUTE <portal>` block to `PUT|DELETE api/admin/connections/{alias}`, for **any** connector
     type. Option values are forwarded **unresolved** so a `SECRET:name` arrives as a reference;
     a literal credential is refused by the catalog rather than stored. `WHAT_IF` covers both.
   - `ConfigurationExportService` emits the canonical form with `DEFAULT_FROM`.
   - No Portal-side API work was needed: `PortalConnectionCatalogService` and
     `api/admin/connections` were already connector-agnostic and already enforce `SECRET:`-only
     credentials. The bespoke SMTP stack was redundant, not merely inconsistent.

   **Still to do — the system is inconsistent until this lands.** `CREATE CONNECTION ... AS SMTP`
   now writes to `PortalSharedConnection`, but every consumer still reads the old
   `SmtpConnections` table, so a connection created with the new syntax is invisible to
   subscriptions and alerts:
   - [x] `SmtpAdminNotificationSender` — now resolves the catalog entry and copies the
         `SECRET:` reference into the generated script. It no longer takes `SmtpPasswordProtector`:
         the credential is never materialised in the Portal process, because the engine resolves
         the reference on connect. This turned out **simpler** than expected, not harder — the
         feared "decrypt here, re-encrypt there" step does not exist.
   - [x] `SubscriptionsController` — `api/admin/smtp` CRUD deleted (superseded by
         `api/admin/connections`); `api/smtp-aliases` and subscription alias validation now read
         the catalog, treating a disabled entry as absent.
   - [x] `SubscriptionDeliveryService` — resolves from the catalog; the delivery script carries
         the `SECRET:` reference. `Sanitize` no longer takes a password to redact because the
         process never sees one.
   - [x] `ConfigurationExportService` — enumerates the catalog and exports option values verbatim.
         The `${SMTP_<ALIAS>_PASSWORD}` placeholder machinery is gone for connections: a `SECRET:`
         reference is not a secret, so the exported script is directly replayable.
   - [ ] **5 Portal tests still red — all fixture-shape, no product defect known.** They seed
         `db.SmtpConnections` and assert the old export shape:
         `ConfigurationRoundTripTests`, `ConfigurationExportSecretExclusionTests`,
         `ConfigurationPromotionTests`, `ScriptedPortalImportTests` (use `SmtpCatalogSeed.Add`,
         added for this purpose), and
         `OperationalObservabilityTests.DeliveryFailure_SanitizesSmtpCredentialFromLogsAndAudit`,
         whose premise — that a plaintext credential could leak into logs — no longer holds and
         should assert the reference is what appears.
   - [x] `OperationalMetricsService` and `ExecutionCapacityHealthCheck` count SMTP entries from the
         catalog. The public metric name (`etlsql_portal_smtp_connections`) and the record field
         are unchanged — renaming a scraped gauge would break dashboards for no gain.
   - [x] `connections-admin.js` — no change needed; the shared-connections form already offers
         SMTP as a connector type.
   - [x] Portal integration tests moved to the catalog. **`ETL-SQL.Portal.Tests` is 551/551
         green.** Registering SMTP is now genuinely two steps — `PUT api/admin/secrets/{name}`
         then `PUT api/admin/connections/{alias}` referencing `SECRET:name` — because the catalog
         rejects literal credentials. **That is the operator flow, not a test detail: the
         administration docs must describe it before this ships.**

   **Two test-authoring traps found here, both worth generalising:**
   - A projection composed *inside* a LINQ query over an encrypted column is translated to
     SQL-side concatenation and bypasses the PII value converter — so the test compares
     ciphertext, which is non-deterministic and differs on every write. Materialise before
     projecting. Any test touching `OptionsJson`, `Target`, or other converter-backed columns is
     exposed to this.
   - Comparing `OptionsJson` as raw JSON asserts on key order, which the export normalises
     alphabetically. Compare a sorted `key=value` projection instead.
   - [ ] **DEFERRED TO v0.19.0 — the table must NOT be dropped in v0.18.0.**
         `MigrationConvergenceTests.PortalMigrations_UpOperationsFollowRollingExpandContract`
         rejects `DropTableOperation` in `Up`, and it is right to: during a rolling HA upgrade a
         node still running v0.17.x would query a table that no longer exists. This is
         expand/contract — v0.18.0 is the expand half (nothing reads `SmtpConnections` any more),
         v0.19.0 is the contract half.
         Keep the `SmtpConnection` entity, its `DbSet` and its model configuration in place until
         then: removing them without the migration makes the model diverge from the schema and
         `SchemaUpToDate` goes false. `SmtpPasswordProtector` can go earlier — it is not
         schema-bound — but check the test fixtures that still resolve it first.
         Passwords are deliberately not migrated; re-entry as `SECRET:` references is the
         documented path.
   - [ ] `SmtpPasswordProtector` becomes dead once the table goes — delete it and its DI
         registration.
   - [ ] **Redact resolved credentials in engine output — a capability lost in this change.**
         `SubscriptionDeliveryService.Sanitize` used to take the plaintext password and scrub it
         from downstream error text by literal replacement. The Portal no longer holds the
         plaintext, so it cannot scrub what it cannot know, and pattern-based `SecretRedactor`
         matches `SECRET:`/`SHARED:`/Bearer/URL/JSON shapes — not a bare password in free text.
         An engine error quoting the resolved password verbatim would therefore reach the delivery
         ledger and audit detail.
         The fix belongs where the secret is resolved: whichever component turns `SECRET:name`
         into a value must redact that value from its own error output before returning it. Net
         exposure is still lower than before (the credential is no longer written into script text
         at all), but this specific mitigation is currently absent and should not be assumed.
         `OperationalObservabilityTests.DeliveryFailure_SanitizesSmtpCredentialFromLogsAndAudit`
         documents the narrowed boundary.

2. ~~**Add an optional management target to `CREATE CONNECTION`.**~~ **WITHDRAWN 2026-07-27** —
   superseded by the `EXECUTE <portal_conn> BEGIN … END` block, which already carries the target.
   The examples below are retained only to show the intended *option shapes* (`SECRET:` references,
   never resolved values); read `... AT portal_admin` as `EXECUTE portal_admin BEGIN ... END`.
   A connection created outside such a block remains session-local.

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
   
   **Do we use this syntax to create shared connections?**

3. **Use one managed-connection lifecycle.** Extend the same object-first grammar to remote
   administration:

   ```sql
   ALTER CONNECTION portal_mail WITH (...) AT portal_admin;
   TEST CONNECTION portal_mail AT portal_admin INTO #test_result;
   SELECT * FROM portal_admin.eng.connection_config WHERE connection_name = 'portal_mail' INTO #config;
   SELECT * FROM portal_admin.eng.connections INTO #connections;
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

#### P0 — Correct named report refresh jobs (Unified Job, Schedule, & Notification Refactor)

**Design specification established 2026-07-27:** See [job_schedule_notification.md](docs/architecture/decisions/job_schedule_notification.md) for full design, grammar, and database schema mappings.

Since this is a greenfield deployment with no legacy data migration risk, migrate straight to the new
normalized schema and unified syntax.

**Decisions taken 2026-07-27** (details and rationale in the design spec):
- **One grammar.** The engine's `CREATE JOB … ON SCHEDULE EVERY n UNIT … AS <statement>` is
  *replaced*, not supplemented. `WITH (MAX_RETRIES, RETRY_DELAY)` carries over; the inline
  `AS <statement>` body does not — a job names a script path.
- **Cron is the one schedule representation.** The Orchestrator's `Interval`/`Unit`/`AtTime` model
  predates that decision and is corrected, not bridged: it cannot express `*/15 8-18 * * 1-5` and
  `SchedulerService.CalculateNextRun` has no timezone concept at all.
- **The Orchestrator is the system of record** for `JOB`, `SCHEDULE`, `NOTIFICATION`. Names are
  unique per orchestrator. The Portal keeps only a report→job link; it does not mirror the catalog,
  so there is no Portal→Orchestrator reconciler.
- **Targeting is `EXECUTE <server> BEGIN … END`**, never `AT <server>` — same call as the managed
  connection work.
- **`ReportId` stays a real FK.** `TargetPath` is a mutable job property changed by
  `ALTER JOB … SET TARGET`.
- **`Name` stays the primary key and is not renameable.** The surrogate-key/renameable-label model
  was considered and rejected: configuration here *is* code, `ConfigurationExportService` emits
  replayable scripts that reference objects by name, and a name that drifts under a script makes a
  re-import create a duplicate instead of reconciling. It is also the identity model every other
  object in the language already uses. Presentation moves to `DISPLAY_NAME`, `DESCRIPTION`, and an
  `OPTIONS` bag — freely editable, never referenced by a script. Anything the scheduler *reads* gets
  a real column, not an options key.
  This removes the largest and riskiest piece of the plan: `JobHistory`, `JobState (JobName,
  StateKey)` and `HostMetricsDaily (Day, JobName)` are no longer re-keyed, so every orchestrator
  store change stays additive — SQLite cannot alter a primary key in place, and this would otherwise
  have been the first non-additive change that store has ever taken.
- **`ALERT` is a fourth entity owned by the Portal.** It is a *condition* plus destinations, where a
  `NOTIFICATION` is only a destination; and it says "a visualization changed", so evaluating
  `WHEN VISUAL …` is Report-SQL work the Orchestrator cannot do. `CREATE ALERT n FOR REPORT '…'
  WHEN VISUAL v < 100` then `ALTER ALERT n ADD NOTIFICATION …` (no `ON <condition>` — the alert is
  the condition). Evaluated on report-refresh completion, which the Portal already observes.
  `ASSERT JOB … ON FAILURE ALERT <connection>` becomes `… ON FAILURE NOTIFY <notification>`.
- **`CREATE DATASET … REFRESH EVERY` is removed**, not migrated. It is a 1:1 dataset↔schedule
  coupling and the only engine-side consumer of the retired inline `AS <statement>` job body — it
  synthesises a `PRINT`-bodied job purely to give the poller a completion to see. `TTL` stays; it is
  cache expiry, not a schedule.
- **Timezones reuse `RelDateResolver.FindTimeZone`** — the same function `AT TIME ZONE` and
  `RELDATE` already call, which accepts IANA, Windows, and abbreviation aliases per
  `docs/reference/dates-times/dates-times.md`. Validate at `CREATE`/`ALTER` time and store the
  resolved default, so editing `appsettings.json` cannot silently move existing schedules.
- **Generated names for sugar** derive from `NEWID()` (UUID v7 — time-ordered, so they sort by
  creation), under a reserved prefix `CREATE JOB` refuses. Readability is `DISPLAY_NAME`'s job, so a
  generated name can stay stable and export-safe without being what an operator reads.

**Open — see §12 of the spec.** None block the job/schedule slice. Q1: notifications are defined
where they are used (Orchestrator's for job outcomes, Portal's for alerts, same grammar targeted by
the `EXECUTE` block) — confirm the two-catalog split. Q2: alerts are `FOR REPORT` only and evaluate
on refresh completion rather than carrying their own schedule. Q3: whether the Portal should
provision connection aliases onto an orchestrator instead of an operator doing it by hand.

**Implementation Steps:**

1. **Orchestrator store (system of record for `JOB`/`SCHEDULE`/`NOTIFICATION`):**
   - Add `Schedules`, `Notifications`, `JobSchedules`, `JobNotifications`; add `DisplayName`,
     `Description`, `Options`, `JobType`, `TargetPath`, `CreatedBy`, `ModifiedBy` to `Jobs`.
   - Drop `Interval`/`Unit`/`AtTime` — no installation exists, so carrying dead `NOT NULL` columns
     buys nothing. Tell developers to delete a local orchestrator DB from earlier testing: an
     existing `Jobs` row has no schedule link afterwards and silently never fires.
   - Every name column is `COLLATE NOCASE` (`IOrchestratorStoreDialect.CollationDdl` supplies the
     Postgres equivalent) — the name is now the identity, and identifiers elsewhere in ETL-SQL are
     case-insensitive. `Jobs.Name` has no collation today, so this is a change.
   - `JobHistory`, `JobState` and `HostMetricsDaily` are untouched.
   - A `JobSchedules` link computes `NextRun` at creation rather than leaving it null: null means
     "due now", so linking a `0 2 * * *` schedule at 3pm would otherwise fire immediately.
   - Cron is minute-granularity only; `EVERY n SECONDS` has no replacement, deliberately. DST policy:
     a local time that does not exist fires when the gap ends (so a nightly job still runs that
     night), a repeated local time runs once — pinned by tests on fixed dates.
   - **[DONE]** Catalog schema, `IJobCatalogStore`, and `CronSchedule` — commit `fe1cbc15`.
   - **[DONE]** Scheduler reads schedule links: `GetJobsDueByScheduleAsync` (DISTINCT, so
     simultaneous links coalesce into one run), `AdvanceScheduleLinksAsync` (marks *every* due link
     fired, not just the earliest — otherwise the job re-fires on the next tick), and
     `JobScheduleAttachment.AttachAsync`, which keeps "a link is always armed when created" in one
     place. The legacy interval query now excludes jobs that have links, so the two paths are
     disjoint; that exclusion and the whole interval branch go when `CREATE JOB` stops producing
     interval jobs.
   - The legacy `CalculateNextRun` is deliberately left on local time and untouched — it is being
     deleted, and changing its semantics now would break its tests for no benefit. `DateTime.Now`
     disappears from the path when it does.
2. **Portal persistence:**
   - Replace `DatasetJobs` with `ReportJobLinks` (`ReportId` FK, `OrchestratorAlias`, `JobName`), EF
     migrations on both providers.
   - Add `Alerts` (`ReportId` FK, visual + operator + threshold, plus `LastState`/`LastEvaluatedAt`/
     `LastNotifiedAt` for transition-based alerting) and `AlertNotifications` (orchestrator alias +
     notification name — the destination lives in the orchestrator's catalog, so it cannot be an FK
     and a dangling reference must be surfaced by the sweep, not discovered at dispatch).
   - The `DropTable` violates `MigrationConvergenceTests.PortalMigrations_UpOperationsFollowRolling
     ExpandContract`; add the migration to that test's `PreDeploymentBreakingMigrations` allow-list
     with a written justification (precedent: `_DropSmtpConnections`).
   - Remove the default interface method bodies on `IDatasetRegistry` — a default body means deleting
     an override still compiles and silently binds to a no-op.
3. **Parser, AST, formatter, linter:**
   - `CreateJobStatement` holds `FOR REPORT`/`FOR SCRIPT` (mutually exclusive) plus retry options;
     new `SCHEDULE`, `NOTIFICATION` and reshaped `ALERT` statements; `ALTER <kind> … ADD|REMOVE`
     and `ALTER <kind> … RENAME TO`.
   - Remove `REFRESH EVERY` from `CREATE DATASET`; delete
     `CreateDatasetStatementHandler.CreateRefreshJob`, `ParseRefreshInterval`, and
     `DatasetRefreshIntervalRule`.
   - `IF EXISTS` before the name. **`CREATE OR ALTER` and `CREATE OR REPLACE` are both supported** —
     config-as-code only pays off if replay converges instead of failing. `OR ALTER` patches and
     leaves links alone; `OR REPLACE` is a full redefinition that **drops links**, which is the only
     way a script that stopped saying `ADD SCHEDULE S2` converges to what it now says. `ADD`/`REMOVE`
     must be idempotent too, or a replayed link statement fails on a duplicate key and breaks the
     import at statement three. This also heals a partially-applied `EXECUTE` block on re-run.
   - `WHAT_IF` on every new statement, matching the managed-connection precedent.
   - Validate the timezone at parse/execute time, not at first fire. `AT TIME ZONE` disambiguates
     from `AT <connection>` with the two-token lookahead `ExpressionParser` already uses.
   - Reject each retired form with a diagnostic naming its replacement — they all parse cleanly
     today, so a generic syntax error would leave the reader guessing.
4. **Notification dispatch and audit:**
   - The Orchestrator dispatches, for jobs **and** for alerts — one catalog, one path, so an operator
     configures a mail destination once. The Portal evaluates the alert condition and calls the
     Orchestrator to deliver. Resolve connections through normal `SECRET:` resolution on the
     orchestrator host so no credential is stored on the `Notification`.
   - A delivery failure never fails the job — an SMTP outage must not turn every successful run red.
     Log it, record it against the run, retry under the notification's own policy.
   - Alerts are transition-based (reuse the v0.17.0 data-quality policy) and evaluate only on
     *scheduled* refresh completion — an interactive refresh must not mail a distribution list.
   - Attribution: the Portal passes the acting user through; `CreatedBy`/`ModifiedBy` are recorded.
     This is attribution, not authorization — see `ROADMAP.md` → *Orchestrator — Per-Object
     Authorization* for the deferral and its trigger.
   - Outbox audit for `CREATE_JOB`, `DROP_SCHEDULE`, `ATTACH_SCHEDULE`, `ATTACH_NOTIFICATION`, …
   - Enforce the referential rules: restrict on `DROP SCHEDULE`/`DROP NOTIFICATION` while linked,
     cascade the links on `DROP JOB`, restrict report deletion while refresh jobs are attached.
5. **Consumers, UI, docs:**
   - `OrchestratorPollerService` matches `ReportJobLinks` by job name, then evaluates the alerts
     attached to that report; `SCRIPT` completions ignored.
   - Subscriptions become sugar over `JOB` + `SCHEDULE` + `NOTIFICATION`.
   - `ReportDependencyService`, `ConfigurationExportService` (emit `SCHEDULE`/`NOTIFICATION` before
     the linking `JOB`), `LineageImpactService`, `ReferenceImpactService`,
     `SubscriptionScriptMaintenance`.
   - Any Portal-node background sweep runs on every node — gate it on `IClusterLockStore` like
     `OperationalMetricsDigestService`, and never delete an Orchestrator job it does not recognise
     (a shared Orchestrator legitimately carries another Portal's jobs).
   - `ConfigurationExportService` emits `CREATE OR REPLACE` plus the full link set, so an export
     round-trips to exactly what it describes.
   - `eng.jobs`, `eng.schedules`, `eng.notifications`, `eng.alerts`, `eng.job_schedules` — reconcile
     with the `SHOW`-retirement inventory above, which currently lists `eng.refresh_jobs`.
   - Samples, snippets, hover help, `docs/syntax-index.md`, and every doc snippet using the retired
     inline-`AS` job form or `REFRESH EVERY`.

#### P1 — Make lifecycle modifiers explicit and uniform

1. Publish one capability matrix for every creatable object covering `CREATE`, `CREATE IF NOT
   EXISTS`, `CREATE OR ALTER`, `CREATE OR REPLACE`, standalone `ALTER`, `DROP`, and `DROP IF EXISTS`.
   The parser must reject unsupported combinations immediately; it must never silently discard a
   requested mode or let handlers interpret an unknown mode differently.
2. **[DONE]** Canonicalize existence modifiers before the object name:

   ```sql
   CREATE TABLE IF NOT EXISTS #stage (...);
   DROP VIEW IF EXISTS ActiveOrders;
   DROP THEME IF EXISTS corporate;
   ```

   Remove post-name forms such as `DROP CONNECTION name IF EXISTS`. The post-name spelling was
   accepted for six kinds (`CONNECTION`, `PROCEDURE`, `FUNCTION`, `VIEW`, `INDEX`, `JOB`) and is now
   rejected with a diagnostic naming the exact replacement. The rejection is applied to all sixteen
   `DROP` kinds so the diagnostic is uniform rather than an accident of which kinds happened to
   accept it. `CREATE TABLE IF NOT EXISTS` was already canonical — verified, not assumed.
3. **[DONE]** Finish or remove advertised Report-SQL lifecycle forms. `ALTER STYLE`,
   `ALTER NAVIGATION`, `ALTER THEME`, and report-scoped `ALTER DATASET` are refused by the parser
   instead of failing at execution as “not yet implemented”; the diagnostic names the
   `CREATE OR REPLACE` form **as that kind spells it** (`STYLE` takes no `AS`, `NAVIGATION` names its
   type after `AS`), because a suggestion the parser rejects sends the reader to a second error.
   `ALTER BUTTON` and `DROP BUTTON` are added — `BUTTON` had `CREATE` only, while
   `DropReportObjectStatementHandler` already removed buttons and `CREATE BUTTON`'s duplicate-name
   error advised "use CREATE OR ALTER or DROP BUTTON first", advice the parser then rejected.

   Each `ALTER` kind now declares the clauses it can patch (`ReportParser.AlterableReportObjects`),
   so the shared visual-shaped body no longer accepts clauses the object has no field for. That was
   not merely untidy: `ALTER PAGE p (SOURCE = ...)` parsed and the handler discarded it, so the
   statement reported success having changed nothing. `ALTER PAGE` gained `VISIBLE`/`REFRESH` and
   `ALTER CONTAINER` gained `VISIBLE`/`ICON` — fields those objects always had, previously reachable
   only by redefining the object and restating its whole layout.

   Two things were deliberately left out. `STRUCTURE`/`MAP` are a re-layout rather than a patch, so
   they stay with `CREATE OR REPLACE`. And `ALTER TEMPLATE` still takes `OPTIONS (...)` while
   `CREATE TEMPLATE` takes a bare `(key = value)` bag; unifying the two property-bag body shapes
   belongs to the “Use `AS` consistently for a type or definition” item below, not here.

   A pre-existing test named `AlterTemplate_AfterCreate_UpdatesTitle` asserted the discard: it
   passed a `TITLE` clause a template has no field for and asserted only that nothing threw — which
   is exactly what a statement that does nothing achieves. It now asserts the refusal.
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
   existing shapes on `INSERT`, and extend to a full DML surface where the semantics warrant it:

   **Tags** are mutable metadata facts and support full `INSERT`, `UPDATE`, and `DELETE`:

   ```sql
   -- Attach metadata
   INSERT TAG FOR TABLE #orders (owner = 'Finance', classification = 'Confidential');
   INSERT TAG FOR TABLE #orders COLUMN customer_id (pii = TRUE);

   -- Correct or transfer metadata
   UPDATE TAG FOR TABLE #orders COLUMN customer_id (owner = 'DataPrivacy');

   -- Remove one key or all tags on a target
   DELETE TAG FOR TABLE #orders COLUMN customer_id (pii);
   DELETE TAGS FOR TABLE #orders COLUMN customer_id;
   ```

   **Lineage** is a provenance record and has asymmetric mutability. Auto-captured lineage
   (produced by `SELECT INTO`, `MERGE`, etc.) is immutable — it is an audit record of what
   actually ran. Only manually imported lineage may be deleted, to allow correction of a bad
   import; deleting auto-captured lineage must be blocked by the engine or require an explicit
   `SET ALLOW_LINEAGE_DELETE = ON` governance override. `UPDATE LINEAGE` is not supported in
   any form because editing a provenance record rewrites history.

   ```sql
   -- Import curated or cross-system lineage
   INSERT LINEAGE FOR TABLE #orders FROM 'openlineage/orders.json';

   -- Correct a bad import (imported lineage only; auto-captured is blocked)
   DELETE LINEAGE FOR TABLE #orders;
   ```

   Retire `CREATE TAG`, `CREATE LINEAGE`, and the duplicate bare `TAG ... WITH (...)` statement.
   Route the new forms through `INSERT`/`UPDATE`/`DELETE` dispatch without confusing them with
   row DML, preserve the existing typed tag-catalog validation and zero-trust lineage source
   handling, and update lineage capture, formatter, linting, completion, help, snippets, samples,
   and documentation together. Tags and lineage do not participate in the
   `CREATE`/`ALTER`/`DROP` object lifecycle matrix.
5. Reserve compound object kinds such as `SHARE LINK`, `SAVED VIEW`, and `EMBED TOKEN` for genuine
   resources with clear identity and lifecycle. Do not encode an implementation type before
   `CONNECTION`.
6. **Retire `SHOW` as a data-retrieval verb. Replace all row-returning `SHOW` commands with
   `SELECT` against the `eng.*` virtual schema.** Engine metadata, session state, lineage,
   governance, jobs, orchestration, and portal catalog are all queryable as virtual tables under
   the `eng.` schema prefix — a dedicated namespace that no remote database engine claims. The
   three-part `connection.eng.table` form targets a remote connection's `eng.*` catalog using
   the existing identifier routing rules, eliminating the need for an `INTO` escape hatch or a
   separate `SHOW ... INTO #table` pattern:

   ```sql
   -- Engine-local (schema.table, no connection prefix)
   SELECT * FROM eng.variables;
   SELECT * FROM eng.lineage WHERE target_table = '#orders';
   SELECT * FROM eng.tags WHERE tag_name = 'pii';
   SELECT * FROM eng.connections;

   -- Remote connection (connection.schema.table — same routing rules as all data queries)
   SELECT * FROM ProdOrch.eng.jobs;
   SELECT * FROM ProdOrch.eng.job_history WHERE job_name = 'nightly_etl';
   SELECT * FROM my_portal.eng.reports WHERE folder = 'Finance';
   SELECT * FROM my_portal.eng.permissions WHERE target_type = 'USER' AND target_name = 'jsmith';
   ```

   Full `SELECT` power — `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `INTO`, subqueries — applies
   immediately. Autocomplete discovers the `eng.*` catalog via the `eng.` prefix. The
   `EXECUTE portal BEGIN ... SHOW ... INTO #t; END` two-step collapses to a single `SELECT`.
   The `eng` namespace is a reserved connection name; the engine must reject `CREATE CONNECTION
   eng AS ...` at parse time.

   Parameterized catalog queries that cannot be expressed as a WHERE filter on a static virtual
   table use a table-valued function under the same schema:

   ```sql
   -- Full-text catalog search with fuzzy matching and relevance scoring
   SELECT name, path, owner, relevance_score
   FROM my_portal.eng.catalog_search('Q3 Sales')
   ORDER BY relevance_score DESC;
   ```

   The `SHOW LINEAGE EXPORT AS OPENLINEAGE TO '...'` form is a file-write operation, not
   data retrieval. Rename it to `EXPORT LINEAGE AS OPENLINEAGE TO '...'` alongside this change.
   REPL shortcuts such as `SHOW TABLES` may remain as display-only aliases that expand to the
   underlying `SELECT` at parse time and are never emitted by the formatter or autocomplete.

   **Complete `eng.*` virtual table catalog:**
   ```
   -- Session / engine state
   eng.connections          eng.connection_config    eng.variables
   eng.profile              eng.tables               eng.columns
   eng.views                eng.version              eng.locks

   -- Lineage and governance
   eng.lineage              eng.lineage_history      eng.tags
   eng.stewardship_gaps     eng.protected_data       eng.protected_data_suggestions

   -- Data quality
   eng.data_quality_rules   eng.data_quality_status  eng.data_quality_failures
   eng.stewardship_score

   -- Jobs and orchestration
   eng.jobs                 eng.job_history          eng.job_state
   eng.refresh_jobs         eng.host_metrics         eng.subscriptions

   -- Portal catalog
   eng.users                eng.reports              eng.favorites
   eng.recent_reports       eng.sessions             eng.permissions
   eng.usage_metrics        eng.operational_metrics  eng.audit
   eng.report_history       eng.report_dependencies  eng.share_links
   eng.embed_tokens         eng.saved_views          eng.alerts

   -- Table-valued functions (parameterized)
   eng.catalog_search()     -- fuzzy full-text portal catalog search
   ```

   Update the engine, parser, formatter, linter, LSP autocomplete, help, snippets, samples, and
   all documentation together with this change. Every `SHOW <data>` form must become a `SELECT
   FROM eng.*` form; every `SHOW ... INTO #table` pattern in the ROADMAP, guides, cookbook, and
   sample files must be rewritten accordingly.

#### P1 — Normalize inspection and target clauses

1. Remove the `SHOW TABLES ON <connection>` parser-only alias. The canonical form is
   `SELECT * FROM <connection>.eng.tables`; there is no `ON` variant.
2. Implement `eng.tags` as a globally enumerable virtual table over the current session's tag
   catalog. Remove `SHOW TAGS FOR SCRIPT` and `SHOW TAGS FOR TABLE <name>` as special forms;
   filter with `WHERE` instead: `SELECT * FROM eng.tags WHERE table_name = '#orders'`.
3. Remove the `SHOW <object> [qualifier] [target] [filter] [AT conn] [INTO #table]` ordering
   rule — it is superseded by `SELECT ... FROM [connection.]eng.<table> [WHERE ...] [AT conn]`.
   Reconcile connection config, report history/dependencies, bundle versions/files, refresh jobs,
   and effective permissions as `eng.*` virtual tables with `WHERE` filters.
4. Correct Portal share/embed syntax drift. Parser, formatter, docs, and configuration export must
   agree on one expiration clause; prefer structural `EXPIRES <timestamp>` over a second
   `WITH(EXPIRES_AT=...)` spelling.
5. **Retire `SHOW SMTP CONNECTIONS` in favour of `eng.connections`.** Carried over from the P0
   managed-connection work (2026-07-27): the bespoke SMTP store is gone, so the statement now
   lists the whole governed catalog and its name is actively misleading. Replace with a filter on
   the connector type, against a Portal-qualified catalog:

   ```sql
   SELECT * FROM my_portal.eng.connections WHERE connector_type = 'SMTP';
   ```

   Delete `ShowPortalSmtpConnectionsStatement`, its `SHOW SMTP CONNECTION[S]` parser branch in
   `SystemParser`, its `INTO #table` rewrite case, and the `PortalDataSource` handler that now
   points at `api/admin/connections`. `eng.connections` is already listed in the catalog inventory
   under the P1 `SHOW` retirement item, so this is a consumer of that work rather than a new
   surface.

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
| `SELECT * FROM eng.columns WHERE table_name = ...` | `SHOW COLUMNS FOR`, `SHOW SCHEMA FOR`, `DESCRIBE` — all retired; `eng.columns` is the single surface |

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
   Specifically reconcile `eng.tables`, `eng.tags`, Theme deletion, Portal share/embed expiry,
   Report-SQL lifecycle claims, managed connections, and refresh-job examples.

   **Root cause found (2026-07-27) — the doc lane cannot currently catch these.**
   `DocumentationSyntaxTests.ValidateDocumentationSnippets` validates snippets against the
   *linter's* `DefaultGrammar` state tree with `requireComplete: false`, not against the production
   parser. `DefaultGrammar`'s `DROP` rule is a single wildcard transition that consumes any token
   up to the semicolon, so **no `DROP` snippet can ever fail that test**. This is how
   `docs/reference/visuals-reporting/report/theme.md` shipped `DROP THEME corporate IF EXISTS;` —
   a form the parser has never accepted for `THEME` — inside the embedded runtime `HELP`.
   Two independent defects:
   - the doc test asserts against a second, more permissive grammar than the one that runs;
   - `Parser.Parse()` records a `SyntaxException` as a diagnostic and recovers, so any doc check
     built on "did it throw" is vacuous. It must assert `script.Diagnostics` is empty.
5. Update `docs/syntax-index.md`, statement references, connector references, administration
   guides, architecture contracts, help resources, snippets, migration guide, samples,
   configuration export, LSP grammar, and release notes as one atomic language change.

**Definition of done.** A user can predict a statement from the object model: the verb names the
operation, the object kind precedes its identity, implementation type follows `AS`, remote ownership
follows `AT`, and lifecycle modifiers occupy one position with one meaning. Portal and Orchestrator
reuse normal SMTP/WEBHOOK connections, multiple named refresh jobs can target the same report, no
unsupported lifecycle parses successfully, and every canonical statement round-trips through the
formatter and documentation test lane.

### Release-process RCI — issues found cutting v0.17.0 (scheduled last)

Thirteen process problems surfaced during this release. Four are already fixed (noted below); the
rest are listed in rough value order. The theme: **the gate's failures were mostly not product
defects**, they were the gate measuring the wrong thing, hiding things, or being impossible to run.

#### Highest value — a test lane that is red for weeks

- [ ] **Run the Docker integration lane in CI.** All 11 SFTP integration tests were red from the
      moment v0.17.0's host-key breaking change landed, and nothing noticed, because that lane is
      local-only and the only thing that runs it is a full release gate reaching phase 30. A
      security-relevant breaking change reached release day with its own tests broken. The lane
      needs only Linux containers, which GitHub runners provide. If a full lane per PR is too slow,
      run it nightly on the release branch.

#### Make the gate report the truth

- [ ] **Continue through independent phases instead of failing fast**, reporting all failures at the
      end (keep fail-fast only where output feeds the next phase, e.g. build -> test lanes). One
      npm-audit failure hid six VS Code phases and the entire Docker lane, so three unrelated
      problems surfaced one restart at a time — roughly 70 minutes each.
- [ ] **Fix the format phase's catch-22.** It says "commit the reformatted files, then re-run with
      `-Resume`", but committing is exactly what invalidates the resume fingerprint, so `-Resume`
      then refuses. Either emit the correct remedy ("rerun *without* `-Resume`") or, better, record
      the post-format fingerprint as the baseline — formatting is provably behaviour-preserving, so
      that turns a full restart into a resume.
- [ ] **Add a pre-commit `dotnet format` check on staged files.** Format drift has now cost a gate
      restart two releases running. Catch it at the commit that introduces it.

#### Make the gate runnable and reproducible

- [ ] **Document running the gate detached.** The agent harness caps managed background commands at
      10 minutes; the gate needs 60–90, so it is always killed inside whichever long silent phase
      spans the ten-minute mark — with no error, which reads like a hang. Two full cycles were lost
      before diagnosing it. `Start-Process pwsh -WindowStyle Hidden` teeing to a log works; add it
      to the release checklist.
- [ ] **Run the gate from a detached checkout of the exact release commit.** A concurrent session's
      roadmap commit broke an in-flight gate run (a `CREATE CONNECTION` example using an option that
      does not exist). It also makes the evidence honest: the checklist claims evidence comes from
      "the exact candidate commit", which a live working tree with another session committing to it
      cannot support.

#### Stop notes and docs drifting from the code

- [ ] **Enforce changelog coverage per feature.** The `[0.17.0]` section was written mid-sprint and
      never caught up: auditing 191 commits found ~12 shipped features missing, including
      *in-pipeline data quality* — the largest feature of the release — absent from the summary and
      highlights entirely. Prefer a `changelog.d/<branch>.md` fragment per feature branch that the
      gate concatenates, so notes cannot lag code.
- [ ] **Correct `CLAUDE.md`'s claim that CI runs only on `main` pushes.** It runs on pushes and PRs
      to `main` **and** `release/**`. Believing otherwise implies you must merge to `main` to get CI,
      inverting the intended order and re-creating the v0.16.0 failure mode.

#### Smaller items

- [ ] **Warn when `gpg.ssh.allowedSignersFile` is unset while `gpg.format=ssh`.** Commits verified as
      `N` (unsigned) purely because git could not check its own signatures; `main` requires signed
      commits, so a reviewer gets a false negative.
- [ ] **Consider installing the .NET SDK in WSL** so a Linux lane can run locally. Accepted for now
      — the CI `Enterprise Certification (linux)` matrix covers it.
- [ ] **Pause Dependabot during a release window.** Pushing `main` rebased both open PRs and
      re-triggered four CI/CodeQL runs that competed with the release-critical jobs for Windows
      runners.
- [ ] **Document the `ha-soak` command ordering.** `fault-run` requires `fault-plan` first and
      `validate` requires `evidence` first; the checklist lists only "`fault-run` then `validate`",
      so both fail on a first attempt.

#### Already fixed during v0.17.0

- [x] Scale-certification warm-up and replicate sampling — see the dedicated item below.
- [x] `DocSanityTests.EveryRegisteredFunction_HasAReferencePage` — 14 functions shipped with no
      reference pages at all, invisible to the embedded `HELP`. Now impossible to ship silently.
- [x] `DocSanityTests.SourceAndTooling_DoNotEmbedDeveloperSpecificPaths` — caught leftover debug
      code writing to a hardcoded developer path from the SLT runner.
- [x] Never use `git add -A` in this repo — a concurrent session's file was swept into a commit.
      Stage explicit paths.

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

`report-runtime.js` escapes every string field in the lineage-tree template but interpolates two
values raw, because the author treated them as numbers:

```js
if (node.durationMs    != null) timeStr = `[${node.durationMs}ms]`;
if (node.rowsProcessed != null) rowsStr = `(${node.rowsProcessed} rows)`;
```

Not exploitable today (both come from `evaluator.Telemetry`, which is numeric) and not introduced by
v0.17.0 — the same lines exist at `v0.16.0`. It surfaced only because `sync-assets` began copying the
runtime into `src/ETL-SQL.WorkstationEditor/wwwroot/`, a path CodeQL scans.

- [ ] Escape or coerce both values in the **canonical**
      `src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js`, then run
      `node .\scripts\sync-assets.js` so the four host copies match.
- [ ] Audit the rest of that template family for the same "strings escaped, numbers trusted"
      split — the inconsistency is the actual defect, not these two lines.
- [ ] Confirm alert 323 closes on the next `main` scan.

### Merge the deferred Dependabot action bumps

Two open Dependabot PRs were deliberately left out of v0.17.0. Both are one major behind and appear
**only in `ci.yml`**, in the Enterprise Certification job's evidence-upload step — neither occurs
anywhere in `release.yml`, so neither can affect a tag-triggered build or publish. Taking them
during the release would have changed the tag candidate and forced another full CI cycle for no
release benefit.

- [ ] Merge **#21** — `actions/setup-dotnet` 5 → 6 (`ci.yml:163`)
- [ ] Merge **#22** — `actions/upload-artifact` 6 → 7 (`ci.yml:176`)
- [ ] Re-check the pin inventory afterwards: `grep -rhoE "uses: actions/[a-z-]+@v[0-9]+"
      .github/workflows/*.yml | sort | uniq -c`

Contrast with `actions/attest-build-provenance`, which **was** merged into v0.17.0 (v2 → v4): it
runs in `release.yml` at tag time, gates un-drafting the release, and was two majors stale — a
failure there would have stranded the release as a draft mid-publish. The distinction to keep is
**does the action run at tag time**, not how old it is.

Watch item: if `upload-artifact@v6` is retired, the Enterprise Certification job still passes but its
evidence artifact silently stops attaching — the evidence checklist depends on that upload for the
Linux certification record.

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

The fix is to make the apparatus trustworthy, not to chase the numbers:

- [x] **Discard a warm-up run after every build** inside `Test-ScaleCertification.ps1` — done in
      v0.17.0. Removes most of the effect on its own.
- [x] **Default a full-tier run to 3 samples** (previously 1 for Smoke) — done in v0.17.0. Warm-up
      alone was not sufficient: Smoke still failed on a single sample, and passed at 3.
- [ ] **Refuse single-sample reports for regression decisions** in `Compare-CertBaseline.ps1`. The
      producer now defaults to 3, but the consumer should reject `samples == 1` outright rather than
      trusting its input — one sample read 717 ms where five read 888 ms on identical code.
- [ ] **Report the within-arm spread alongside every delta**, and treat a delta smaller than the
      spread as no result. Noise floor is ~2% with warm-up and ~56% without.
- [ ] **Run scale certification before the long test lanes**, or quiesce the machine first. Running
      it last guarantees the worst measurement conditions in the gate.
- [ ] **Add a same-worktree A/B mode** for comparing two commits, so version comparisons cannot be
      contaminated by comparing two directories in different thermal states — the exact error that
      produced the v0.17.0 false alarm.
- [ ] **Emit `CONFIG_FINGERPRINT` and `COMMIT_METADATA`** in every certification run so comparisons
      can verify they are comparing like with like.
- [ ] Confirm the `StreamingSelect` GC_PAUSE warning (+29%, warmed and reproducible) is acceptable
      given the data-quality work's per-row allocation, or reduce it. Warning only — elapsed and
      throughput are in band.

Do **not** re-bless the baselines. `baseline-smoke.json` and `baseline-standard.json` both pass when
measured correctly; an earlier bless of cold readings was correctly reverted in `e3fa80af`.


