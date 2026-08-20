# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## Versioning Policy

Version numbers follow [Semantic Versioning 2.0.0](https://semver.org/).
- **Pre-1.0.0 (`0.y.z`):** The engine runtime is in active development. Minor version increments (e.g., `v0.13.0` to `v0.14.0`) may introduce breaking changes or syntax deprecations, which are formally cataloged in [BREAKING_CHANGES.md](BREAKING_CHANGES.md). Patch version increments (e.g., `v0.14.1`) are strictly reserved for backwards-compatible bug fixes.
- **Production (`1.0.0` and beyond):** Upon reaching `1.0.0`, the public API, syntax grammar, and execution behaviors are considered stable. Breaking changes will only occur on major version increments (e.g., `v2.0.0`).

---

## [Unreleased]

- No unreleased changes yet.

## [0.18.0] — 2026-08-20

### Breaking Changes

- Ambiguous flat secret and connection commands move under `admin machine`: use
  `admin machine secret set|list|verify|rotate|disable|enable|delete` and
  `admin machine connection set|list|verify|disable|enable|delete`. These commands never mutate the
  separate Portal catalog stores; list output identifies the configured machine-local provider.

**Portal SMTP is an ordinary connector; `CREATE SMTP CONNECTION` is removed**

- SMTP now uses the connector grammar: `CREATE CONNECTION <alias> AS SMTP(...)` inside an
  `EXECUTE <portal> BEGIN ... END` block. The retired form is rejected with a diagnostic naming the
  exact replacement — it differed in five ways at once (statement shape, string-literal vs
  identifier alias, `WITH` vs `AS`, `FROM_ADDRESS` vs `DEFAULT_FROM`, and credential handling), so a
  generic syntax error would have left all of them to guess at.

- `DROP SMTP CONNECTION 'alias'` becomes `DROP CONNECTION [IF EXISTS] <alias>`.

- **Credentials are `SECRET:name` references, never values.** Configuring SMTP is now two steps:
  store the password in the portal secret store, then reference it from the connection. A literal
  password is refused by the catalog rather than stored.

- The `SmtpConnections` table is **dropped with no data migration**. Existing SMTP passwords are
  not carried forward; store each one as a secret and re-reference it. The `api/admin/smtp`
  endpoints are removed — `api/admin/connections` serves every connector type.

- Existence modifiers are canonical for every object kind: `DROP <kind> IF EXISTS <name>`. The
  post-name spelling (`DROP CONNECTION c IF EXISTS`), previously accepted for six kinds, is
  rejected with the canonical form.

**Report-object `ALTER` accepts only what it can actually change**

- `ALTER STYLE`, `ALTER NAVIGATION`, `ALTER THEME`, and report-scoped `ALTER DATASET` are refused by
  the parser. They previously parsed, linted, and completed successfully, then threw "ALTER not yet
  implemented" at execution — after a report script may already have done half its work. The
  diagnostic names the `CREATE OR REPLACE` form for that kind, spelled the way that kind accepts it.

- Each alterable kind now accepts only the clauses it can patch. `ALTER PAGE p (SOURCE = ...)`,
  `ALTER TEMPLATE t (TITLE = ...)`, and similar previously parsed and were then discarded in the
  handler: the statement reported success having changed nothing. They are now parse errors listing
  the clauses that kind does accept.

- `ALTER PAGE REFRESH` requires a whole number of seconds. `CREATE PAGE` silently treats an
  unparseable interval as "off"; on a patch the same silence would report success while leaving the
  previous interval running.

**Canonical language lifecycle and inspection surface**

- Lifecycle modifiers now have one supported position and one capability matrix across core,
  Report-SQL, and Portal objects. Unsupported `CREATE OR ALTER`, `CREATE OR REPLACE`,
  `CREATE IF NOT EXISTS`, `ALTER`, and `DROP IF EXISTS` combinations fail during parsing instead of
  producing statement shapes that cannot execute. Local connection upserts and report-object
  replacement now share the documented semantics.

- Local/report datasets consistently use `&name`; Portal catalog datasets retain quoted identities.
  Publish commands are identity-first (`PUBLISH REPORT|BUNDLE|DATASET <name> FROM <source>`), and
  typed or property-bag object definitions consistently use `AS`.

- Tags and imported lineage are metadata records managed through `INSERT`, `UPDATE`, and `DELETE`.
  Retired `CREATE TAG`, `CREATE LINEAGE`, and bare `TAG ... WITH (...)` forms are rejected, while
  automatically captured lineage remains immutable.

- Row-returning inspection commands are replaced by normal queries over `[connection.]eng.*`.
  `SHOW TAGS`, `SHOW COLUMNS`, `SHOW SCHEMA`, and `DESCRIBE` are retired, and lineage file export is
  now `EXPORT LINEAGE AS OPENLINEAGE TO <path>`.

- Function-style file/email aliases, `FOR EACH`, and conditional `WAITFOR (<condition>)` are
  retired. Use statement-form file/email operations, `FOREACH`, and `WAIT UNTIL`; `WAITFOR DELAY`
  and `WAITFOR TIME` remain supported.

- Portal share/embed expiration uses the structural `EXPIRES <timestamp>` clause, and compound
  resource kinds such as `SHARE LINK`, `SAVED VIEW`, and `EMBED TOKEN` are reserved for named
  lifecycle-managed resources.

### Added

- `scripts/Invoke-AcceptanceProfile.ps1` — seeds a small, reproducible acceptance profile into a
  running Portal and smoke-tests it. A folder, a self-contained report, and one user per role
  (Viewer, Publisher, DataSteward, OrchestratorManager).

  Everything goes through the public HTTP API, which is the point: the same script runs against
  `dotnet run`, a container, or a deployed environment, so "it passed locally" and "it passed in the
  image" become statements about the same checks rather than two scripts that happen to share a
  name. It needs nothing installed on the target.

  The profile is deliberately small. An acceptance dataset that takes ten minutes to seed is one
  people stop seeding, and a large one hides the failure it was meant to reveal among rows nobody
  reads.

  It is idempotent — re-running reports what already exists rather than failing or duplicating —
  handles the forced first-run password change automatically, and exits `0`/`1`/`2` for
  passed/failed/unreachable so a pipeline can tell "the Portal is down" from "the Portal is wrong".

  Publishing a report needs the `.rptsql` file under the Portal's script root, which an HTTP client
  cannot arrange. Pass `-ScriptRootPath` where the root is reachable and the script writes it;
  where it is not, the report is **skipped rather than failed**, because a check that fails for
  something the script itself said it could not set up is noise.

  Documented at `docs/administration/portal/acceptance-profile.md`, including the first-run
  configuration an empty Portal refuses to start without.

- Added an identity access simulator: `GET /api/admin/access-simulator/user/{id}?reportId=&datasetId=` explains what one identity can reach and **why**, composing roles, groups, folder and report ACLs, dataset grants, shared-connection grants, Studio capability, and row-level security into a single answer that names its sources. Each of those was already queryable on its own, which was the problem — reconstructing "why can this person open that report?" meant checking five surfaces and composing them by hand.

  Row-level security is explained by naming the identity tokens the script filters on and the values that would be bound for the user. The report is never run, and a test asserts that no data from it appears anywhere in the response: a tool for auditing who can see data must not become a way to see it.

  The report answer and its explanation are both produced by `FolderPermissionService`, so the diagnostic cannot drift from the enforcement it describes. Reading another identity's effective access is itself a privileged act and is audited as `SIMULATE_ACCESS`.

- `AdminPanelFailureStateTests` drives both panels with only their own request failing, which is
  the shape the real failure takes: one call rejected, the rest of the page fine.

- `BrowserRouteReachabilityTests` asserts every `/api/...` path the Portal's own JavaScript calls
  resolves to a route the Portal serves. The client turns a rejected request into a caught error,
  which renders as "nothing to show" or "temporarily unavailable" — so a renamed or mistyped route
  produces no symptom a reviewer would notice. It found nothing today; it is a guard, not a
  discovery, and its scope is deliberately narrow: existence only, not authorization and not the
  response shape.

- **Approver coverage completes the authorization matrix.** Approving is a *capability* rather than
  a role, so its rows live with the workflow they govern: approving requires `ReportApprove`,
  asserted **both ways** — the positive row alone would prove approval works without proving
  anything stops it — and an approver cannot publish, because reviewing a change and shipping it are
  separate authorities an organization needs to be able to give to different people.

- **A mechanical staleness audit of `docs/architecture`, recorded in `TODO.md`.** Every `src/…`
  path and backticked type name was resolved against the tree rather than read for plausibility.

  The wrong-statement rate turned out to be low — of ~16 flagged type references, all but the three
  above were false positives (role names, framework types, TypeScript classes, test-only types), and
  every cited source path resolves.

  **The real staleness is omission, and it is concentrated in `Engine.md`**, which documents the
  v0.10-era engine accurately and has not grown with it. It mentions the external spill engines 69
  times and the following zero times: data-quality rules, the `Columnar*Plan` fast-path family,
  row-level security, and `SECRET:`/organization-policy enforcement. All four were confirmed
  engine-level, not Portal-only.

  That matters more than a stale type name: data-quality rules **pin execution to the local row
  pipeline** — the columnar fast-path gates deliberately exclude rule-carrying statements — so a
  reader using `Engine.md` to understand dispatch and fast paths cannot see a constraint governing
  both.

  `Orchestrator.md`, `Lineage.md`, `Connectors.md`, `Reporting.md` and `Portal.md` were checked and
  need no action.

- **`Engine.md` now documents artifact storage** — the seam every host writes scripts, snapshots,
  datasets, maps and key material through, and which had appeared in no architecture page at all.
  Covers the `ArtifactArea` set, the providers, and the two decorators that carry the guarantees:

  - `Keys` is not just another area. Providers treat it as secret — owner-only permissions on write
    and no local-copy leasing — so a caller cannot obtain key material on disk the way it can a
    snapshot.
  - `GuardedArtifactStorage` enforces the deployment's security guardrails at the single storage
    boundary, reusing `SecurityService`'s extension lists rather than keeping a second copy.
  - `FencedArtifactStorage` applies database-backed **write-epoch fencing**. On shared storage
    without native fencing, a writer must claim the artifact's epoch through `IWriteEpochStore`
    before a create, replace, move destination or delete; an older token is refused and *the byte
    write never happens*. This is what stops a node that has lost its lease but not yet noticed from
    overwriting newer work — and it is why HA needs artifact roots genuinely **shared** rather than
    merely identical, since two nodes writing to separate directories never contend for the same
    epoch.

- **`Engine.md` now documents the observability conventions.** `ObservabilityConventions` holds the
  shared, deliberately low-cardinality tag and metric names. The reason they exist is the part worth
  writing down: they keep free-form names, file paths, SQL text, parameter values and connection
  strings *out* of telemetry. That is a cost control — high-cardinality labels are what make a
  metrics backend expensive — and a disclosure control, because a label travels wherever telemetry
  goes and is not covered by the redaction applied to logs and support bundles.

  Both gaps were found by `EngineSubsystemCoverageTests` while it was being written, and both are
  closed by writing the pages rather than by relaxing the inventory. Its known-gap list is now
  empty; the test pinning it stays, so a future gap has to be added on purpose.

- **`AstRoundTripPropertyTests` — no clause may disappear when a statement is serialized back to
  SQL.** The round-trip tests that existed were written per feature, by whoever added the feature,
  so a clause added later had none. That is how `ON FAILURE` came to be dropped entirely by
  `ToSql()`: the script still parsed and routed its `@fail: 'QUARANTINE'` rows nowhere.

  Rather than compare ASTs — which differ in source positions and would need a bespoke comparer per
  node — it asserts the weaker but broadly applicable property that every keyword in the input
  survives serialization, since a dropped clause always loses its keyword. Sixteen statement forms
  are covered, and reintroducing the original defect makes it report
  `ToSql() dropped ON, SCRIPT, THROW, TO, WITH`.

  Keywords the serializer legitimately normalizes away (`AS`, `INNER`, `OUTER`, `ROWS`) are listed
  explicitly with a reason each, so the list cannot quietly grow to silence a real failure. Running
  it over the sixteen forms found no further dropped clauses.

- Added an operator view of durable remote audit delivery at `GET /api/admin/audit/collector`: queue depth and queued bytes, the age of the oldest undelivered event, terminal failures, last attempt, last success, last error, and the thresholds any of those readings is compared against. These signals already existed in health, Prometheus, and fleet status, which is fine for a dashboard and no use to someone mid-incident deciding whether to raise a threshold or go and fix the collector.

  Fail-closed state is produced by asking `AuditDeliveryGate` itself whether the next mutation would be refused, rather than re-deriving its thresholds. A second copy of that rule would eventually disagree with the one that actually blocks writes, and the operator would be reading a reassurance that is not true.

- Added `POST /api/admin/audit/collector/test-delivery`, which posts a synthetic event to the configured collector through the real delivery path — same endpoint resolution, same authentication, same body shape. A probe that took its own path would prove the probe works, not the delivery. It carries no audit content, reports the endpoint without its query string (which can carry a token), redacts transport failures, and is itself audited.

- An **`Author`** grant on folders and reports, for the person who maintains a report without
  administering the folder it lives in. An Author may rewrite a report's script, content and
  metadata, and run it. They may **not** move it to another folder, delete it, publish a new report
  into the folder, create share links or embed tokens, or change any ACL. Moving a report changes
  what two folders contain and deleting one changes what a folder contains — neither is an act on
  the report's content, which is the only thing an Author was given authority over.

  It is available wherever `Read`/`Execute`/`Manage` already were, ordered between Execute and
  Manage in the pickers.

- `AuthorizationMatrixTests` — the Portal's authorization rules asserted as data, one grant × one
  operation at a time, so a privilege change cannot ship by accident: a widened grant fails a
  `denied` row and a narrowed one fails an `allowed` row. The negative rows are the point, since a
  suite that only proves people can do things proves nothing about what stops them.

  Writing it surfaced two properties of the model that were previously discoverable only by reading
  about forty enum comparisons plus scattered `[Authorize(Roles=…)]` attributes:

  - **Authorization is two-dimensional.** A *role* decides which class of operation you may perform
    at all; an *ACL* decides which resources you may perform it on. The axes are not
    interchangeable, and conflating them is how a grant comes to mean more than intended.
  - **`Manage` on a folder is authority over the reports in it, not over the folder itself.**
    Reading or re-granting a folder's ACL, creating a subfolder, and deleting a folder are Admin-role
    acts. Without that split the highest ACL grant would be self-propagating: whoever held it could
    hand it out, so the set of people with access could only ever grow.

  The report-scoped case is driven through the real path — request access, admin approves — because
  there is no endpoint that grants a report ACL directly, and a shortcut through the database would
  prove the ACL works while proving nothing about how one comes to exist.

- Added an architecture test that inventories every place the Portal compares an object's `CreatedBy`/`OwnerId` against the caller, each with the reason it is safe, and fails the build on any comparison that is not inventoried. Treating authorship as standing permission is what made a v0.17.0 regression leave authors full access to everything they had created after being removed from every group — deprovisioning that did not deprovision. A hand review of that diff cleared it; tests caught it. The inventory can only shrink or change deliberately, and the three known dataset short-circuits are pinned in it as open.

- `SharedAssetLineEndingPinTests` asserts every file under
  `ETL-SQL.ReportRuntime/Resources/Shared` is pinned to LF, asking `git check-attr` so the answer
  reflects real attribute resolution. `.gitattributes` already described the rule in a comment; the
  comment did not stop the omission, and the cost of finding it was a full CI run.

- **`ColumnRuleCatalogPropertyTests` — every `@expect` rule must be able to fail.** The suite was
  thorough at "does this rule catch bad data" and thin at "is this rule wired up at all", and those
  look identical from outside: a rule that never runs reports exactly what clean data reports.
  Three defects in one session had that shape — a composite rule naming an unprojected column
  skipped every row, `CASTABLE AS` with an unknown type accepted everything, and both per-row rule
  switches ended in a `default` that returned "passed".

  Each of the eleven rule forms is driven end to end against a row that violates it, and the run
  must record a failure. A reflection test pins the catalogue, so a new `ColumnRule` record cannot
  be added without a case and therefore cannot ship silently unenforced — the same shape as
  `EngineSubsystemCoverageTests`.

  Both halves were verified by sabotage rather than assumed: removing a rule from the catalogue
  reports it as uncovered, and changing a case's row to one that satisfies its rule reports that the
  rule recorded no failure.

- **`test-lane.ps1 -Lane spill`** — re-runs the engine and SLT suites with spill, sort and batch
  thresholds set to a handful of rows.

  This exists because the columnar spill path was unreachable by any lane. The thresholds default to
  10,000–1,000,000 rows; the fuzzer runs against a three-row table, SLT files insert two to five
  rows, and unit tests use inline literals. Nothing in the suite was ever large enough to spill, so a
  spill defect could only be found by a sample or a customer — which is exactly how this one was
  found. Lowering the thresholds turns every query the corpus already contains into spill coverage.

  `BatchSize` is set to 7: deliberately not round, and not a divisor of the corpus's row counts, so
  batch boundaries fall *inside* logical groups. Boundaries that always land between groups hide the
  cross-batch defects the lane is for.

- `ColumnBatchSchemaStabilityTests` states the invariant where it is established — per-batch type
  inference — rather than where it was previously enforced, which was an exception thrown by the
  spill writer on data large enough to spill. It covers both ways batches diverge (a wholly NULL
  column, and a value arriving as text in a later batch) and asserts that adopting the earlier type
  does not invent values.

- `ColumnBatchAdapter.LogicalSchemaOf` captures a batch's schema for callers that must keep it
  stable across batches.

- `EveryStudioCapability_AppearsInTheConfigurationReference` guards that last class of drift.
  Capabilities are granted by typing their name into `Portal:Studio:RoleCapabilities`, and the
  filter rejects an unknown name rather than storing a typo — so a capability missing from the
  reference is one nobody can grant deliberately, and nothing anywhere reports it.

- `CriticalSurfaceSnapshotTests` — snapshots of the Portal's critical surfaces, captured as
  **accessibility trees** rather than pixels.

  An aria snapshot records what a page *is* — headings, landmarks, controls and their accessible
  names — rather than what it looks like. That choice is deliberate on three counts: it does not
  churn on fonts, GPU, or platform anti-aliasing, so it runs anywhere without a tolerance nobody can
  justify; it is a text diff, reviewable in the pull request that causes it rather than by opening
  two images; and it fails for the changes that matter — a heading that stopped being a heading, a
  button that lost its name — which is exactly the class of regression a pixel diff reports as a few
  grey pixels nobody investigates.

  Baselines sit beside the tests. `ETLSQL_UPDATE_SNAPSHOTS=1` regenerates them, and an updated
  baseline is a claim that the new structure is correct — a review decision, not a mechanical one.

- The Admin dataset permissions panel now shows grants made directly to a user alongside group grants, each labelled with its principal type, and can revoke either. `GET /api/datasets/{id}/acl` carries a `principalKind` on every entry, and `DELETE /api/datasets/{id}/acl/user/{userId}` revokes a direct grant and invalidates that user's sessions. This completes the dataset half of "authorship is not permission": a creator's Owner grant was enforced and revocable in the database, but invisible in the product — a grant an administrator cannot see is a grant they cannot account for.

- **A four-path deployment-profile review for the release.** `Deployment_Profile_Standards.md` has
  prescribed one since it was written — "a release claim must name the profile and transition it
  actually proves" — and no release had produced one. `v0.18.0-deployment-profile-review.md` is
  that review: driven from the release's changelog fragments rather than from memory, grouped into
  six capability areas, each stating how Solo, Team, Enterprise and SaaS accomplish it.

  The summary is the finding. **v0.18.0 is a Portal and Enterprise release**, and most of what it
  added has no Solo form because Solo has no Portal. That is a legitimate answer on one condition —
  the underlying evidence must stay reachable without the Portal — and the release meets it: every
  governance and quality surface it added reads `eng.*`, which the CLI, Report Player and
  Orchestrator serve from the same code. The review states where that holds and where it does not,
  rather than colouring cells green.

  **No matrix cell moved to Green.** The release strengthens evidence behind existing Green cells
  and adds acceptance lanes that make them re-testable. The SaaS column is unchanged and remains
  Red for every concern touched; the Enterprise happy path is not evidence for a mutually untrusted
  tenant boundary.

  Three things it records that were not written down anywhere: the Portal governance dashboard and
  `eng.stewardship_score` use different scoring models and will not agree — compare them knowingly;
  recovery custody stays on the host in every profile, however large; and `Portal:Topology:ExpectedMode`
  defaults to `Auto`, which classifies a Team deployment on PostgreSQL as HA and holds it out of
  load-balancer rotation until told otherwise.

- Added the end-to-end proof that deprovisioning deprovisions: one identity creates a report, saved views, anonymous share and embed links, and a dataset, then loses its group and finally its account, and every surface it left behind is checked. It runs as a single scenario on purpose — the regression this guards against was not one broken function but five surfaces that each looked reasonable in isolation, so what has to hold is the property across all of them at once. The two phases revoke different things: group removal takes everything reached through the group, including the anonymous links, while a grant made directly to a person survives it and is cascaded away only when the account itself is deleted.

- `ArchitectureDocReconciliationTests` — checks the architecture document's mechanically checkable
  claims against source: every seeded role, every persisted entity, every named authorization
  policy, and every API area is documented.

  Deliberately limited to claims that can be verified. Prose about intent cannot be checked from
  source, and a test that pretended to would either be vacuous or would block every honest
  rewording. It found substantially more drift than a reading pass had.

- **`CASTABLE AS <type>`** — an `@expect` rule asserting the value would convert, for the ingestion
  case where everything arrives as text: `CASTABLE AS DATE`, `CASTABLE AS DECIMAL(18,2)`. It runs
  the engine's own conversion, the one behind `TRY_CAST`, so a value the rule accepts cannot fail a
  later cast — the two agree by construction rather than by two implementations happening to match.

  Two things the shared converter does not do on its own, and the rule now does:

  - **A declared width is enforced.** `Cast` ignores `DECIMAL(18,2)` and `VARCHAR(50)` widths
    entirely, so without this the declaration would read as a constraint while checking only "is a
    number" and "is a string".
  - **An unknown type name is rejected at parse time.** `Cast` returns the value unchanged for a
    type it has no converter for, which would have made `CASTABLE AS BANANA` accept every row —
    a validity rule that reports clean because it never checked anything.

- **`EXISTS WITH (<cols>) IN <table>(<cols>)`** — a composite referential-integrity `@expect` rule.
  The existing `EXISTS IN table(col)` is single-column, so on any table whose key is only unique
  within a scope it accepts the rows the check exists to catch: `EXISTS IN dim_customer(CustomerId)`
  passes a CustomerId that is real but belongs to a *different* TenantId, and reports the load as
  clean. The two column lists pair positionally, so the reference table's columns need not share the
  source's names, and a mismatched arity is a parse error rather than a silently truncated check.

  Runtime coverage includes the cross-tenant row itself, a companion test pinning that the
  single-column form still accepts it (so a regression back to single-column probing fails loudly
  rather than passing), NULL key parts, and tuple-part collision — `("ab", "c")` must not match a
  reference tuple of `("a", "bc")`.

- The rule-cost harness now attaches each rule to a column whose values **satisfy** it. Measuring
  rules that reject every row measured the failure-reporting machinery — describing the failure,
  allocating the row-failure record, recording a sample — rather than the cost of having the rule.
  With that corrected, `NOT NULL`, `NOT BLANK`, `LENGTH`, `IN` and `MATCHES` all sit within ~1 MB of
  a rule-free statement over 50,000 rows; the rules that cost anything are the ones that call the
  evaluator per row (`BETWEEN` +28 MB, `EXPR` +61 MB) and `UNIQUE`, which spills (+380 MB).

- **`NOT IN (<list>)` and `NOT MATCHES <regex>`** — negative membership and pattern `@expect` rules,
  for the placeholders an upstream system writes when it does not know (`'UNKNOWN'`, `'N/A'`) and
  for content that must never reach a rendered surface. Both were expressible with `EXPR`; as named
  rules the intent carries into lineage, diagnostics and policy review. Great Expectations makes the
  same pair available.

  Each parses through the same code as its positive form, so an invalid regex or a `NULL` in the
  list is rejected in either direction rather than only when written positively. `SET CASE_SENSITIVE`
  applies unchanged.

- **`NOT BLANK`** — an `@expect` rule rejecting empty and whitespace-only strings. Expressible
  before with a regex or by repeating the column name in `EXPR`; as its own rule the intent is
  legible in diagnostics, autocomplete and policy review. It skips NULL like every rule except
  `NOT NULL`, so `'NOT NULL, NOT BLANK'` is the full "a value is required" check.

- **`LENGTH BETWEEN <min> AND <max>`** and `LENGTH >= <n>` (with `<=`, `>`, `<`, `=`) — character
  count rules, a standard validity category in Great Expectations and Soda. Every form lowers onto
  one inclusive range, so the runtime carries a single predicate rather than one per operator, and
  a range no value can satisfy (`LENGTH BETWEEN 10 AND 5`, `LENGTH < 0`) is rejected at parse time
  rather than quarantining every row. Length is the rendered value's character count, matching
  `LEN`.

- **`ON FAILURE QUARANTINE TO <table> WITH (HANDLING = SCRIPT)`** — quarantine for rows the running
  script remediates, reroutes, or discards itself. The rows still leave the main output and still
  carry their `__dq_*` context, so a later statement in the same run can read the capture table and
  act on each cause differently; per-run quality metrics still record the counts.

  What the mode removes is the hand-off. No replay manifest is written, so the target does not
  become a Portal steward-queue item and `REPLAY QUARANTINE` cannot target it; no enclosing section
  label is required, and the linter stops recommending a durable target. Those three requirements
  all exist to serve remediation *after* the run — asking someone to remediate rows the script
  already fixed is worse than not asking.

  `HANDLING = STEWARD` states the existing behavior explicitly; omitting `HANDLING` keeps it.
  `HANDLING` on a non-QUARANTINE clause is a syntax error, since `WARN` diverts no rows.

  `ON FAILURE` `WITH (...)` now takes several comma-separated options, so `RETENTION` and `HANDLING`
  can appear together.

- **`BETWEEN <lower> AND <upper>`** — an `@expect` rule whose bounds are full expressions, so a
  range can be typed or relative: `BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate`. The existing
  comparison rules accept only decimal literals and can express neither. Bounds are evaluated per
  row and compared with the engine's type-aware comparison, so dates compare as dates rather than as
  rendered text.

  A NULL bound makes the range unknown and skips the row, matching SQL's own `BETWEEN`: a rule that
  failed every row because a variable was unset would report the data as broken when the script is.

  The bound separator is found at parenthesis depth zero, so a lower bound containing its own `AND`
  — `IIF(a = 1 AND b = 2, …)` — is not cut in half.

- **Draft → review → publish for report scripts**, opt-in behind
  `Portal:Studio:RequireApprovalToPublish` (default **off**). Saving a script previously wrote
  straight over the running report, so "save" and "publish" were the same act and review could only
  ever happen after the fact. A draft is what makes the gap between authoring and publishing
  representable.

  The proposed script lives in the database rather than the artifact store, deliberately: a draft is
  not yet a script — nothing should execute it, serve it, or list it beside real ones — and keeping
  it out of the script directory makes that structural instead of a naming convention everyone has
  to remember.

  **Separation of duties is absolute.** An author can never approve their own draft, whatever
  capabilities or roles they hold, **including Admin**. A four-eyes control that the most privileged
  account can bypass fails exactly when it is needed, because the account that gets compromised or
  leaned on is the privileged one.

  Three further rules follow from an approval being about *content*, not about a draft id:

  - Editing a draft revokes any approval **and** any review in progress, returning it to the author.
    Otherwise a trivial change could be approved and the body swapped afterwards, or a reviewer could
    have content change mid-read — either way a reviewer's name would end up on something they never
    saw.
  - Every decision records the script hash it was made against, so "was this reviewed?" is answerable
    for the version that actually shipped.
  - Publishing is refused when the live script has moved past the draft's base. The approval was for
    a change against a version that is no longer there, and publishing anyway would silently discard
    whatever landed in between.

  Every mutation takes `If-Match` with the draft's version, and the decision trail is append-only —
  a reviewer who approved and later changed their mind is a different history from one who only ever
  rejected, and that distinction is what a post-incident review is looking for.

- A `ReportApprove` Studio capability, separate from `ReportPublish` so that reviewing a change and
  shipping it can be given to different people.

- **`EngineSubsystemCoverageTests` — a guard for the failure mode architecture documentation
  actually has: omission.** A wrong type name is caught the moment somebody follows it; a subsystem
  nobody wrote down is invisible. `Engine.md` described the external spill engines 69 times and
  data-quality rules, columnar plans, row-level security and adaptive execution zero times, for
  three releases, and nothing reported it.

  It inventories every code-bearing directory under `ETL-SQL.Engine` and `ETL-SQL.Core` and asserts
  set equality against a declared inventory. A new subsystem fails the build until someone says
  which page documents it, or records why none is needed. Where coverage is claimed, the named page
  must still contain a marker for it — so a page dropping a subsystem is caught too.

  **Deliberately not a text search.** Matching directory names against the prose was tried and is
  useless in both directions: `Data`, `Common` and `Services` match incidental English everywhere,
  while `Planning` reads as undocumented even though its types are described by name. The test does
  not infer coverage; it forces a decision.

  It found two undocumented subsystems while being written, now pinned by set equality so they
  cannot grow quietly: `Core/Observability` (the correlation and trace tags every log scope and
  audit record is keyed on) and `Core/Storage` (`IArtifactStorage`, the seam every host writes
  artifacts through and the thing HA requires to be shared).

  Known gaps are recorded rather than failed. Turning existing debt red only invites weakening the
  inventory to get green, and an inventory that launders omissions into approvals is worse than
  having none.

- Added the departmental Environments workflow. `GET /api/admin/environments/plan` derives a full deployment plan from an environment id — databases, artifact root, key ring, service identity, service and unit names, the port block, and the per-environment key requirements — following the naming and port conventions in `Departmental_Isolation.md`. Deriving every resource from the id is what makes a plan checkable rather than a document someone has to follow carefully.

  `POST /api/admin/environments/validate` checks a proposed environment against what this Portal can see: its own environment, the environments named for fleet visibility, and the machine registry. Any shared resource is reported as a collision rather than a warning, because sharing one is enough to break isolation.

  Two boundaries are held deliberately. The Portal **generates plans and never applies them** — creating databases, accounts, key rings and endpoints belongs to a separately authorized deployment plane, since an environment able to provision another is not isolated from it, and the plan states that in the artifact rather than leaving it to the reader. Plans are also **secret-free**: keys appear as requirements at named configuration keys, never generated and never valued, so a plan is safe to review, store, and hand to whoever does the provisioning.

- Added `GET /api/admin/environments/current`, which measures this environment against the isolation contract and links to the read-only fleet workspace. Resources the process cannot observe from inside — a shared database login, two environments running under one OS account, whether a key is unique across environments — are reported as **unknown** rather than assumed isolated. A verification that quietly assumes the answer is worse than one that admits the gap.

- Added `EnvironmentIsolationTests`, which runs two deployments and proves the model rather than describing it: catalogs and search do not merge, a resource id from one environment is meaningless in the other, and a token minted in one is refused by the other while still working where it was minted.

- Added the read-only Fleet/Operations workspace at `GET /api/fleet/workspace`: every configured environment polled at once, merged into one report with compatibility metadata, policy/configuration/version divergence findings, migration state, grouping and filtering, plus an upgrade preflight or postflight report. The aggregation had been built but had nothing to aggregate — no configuration named the environments, so it was machinery with no way in. `Portal:Fleet:Environments` is that way in.

  Naming an environment grants visibility, never authority: the workspace issues one scoped read-only `GET /api/fleet/status` per environment and nothing else, and a departmental deployment is not administered from another one's Portal. Per-environment tokens are never echoed, only counted. An unreachable environment is reported as unreachable rather than failing the whole view, because a partial outage is exactly when the view is needed.

- Added a guided configuration export workflow. `GET /api/admin/configuration/export/plan` returns what leaves this Portal, what will not, and what must be moved separately, without the script body — the export endpoint already computed all of that and wrote it only to the audit line, so the only way to learn what an export omitted was to read the file.

  `POST /api/admin/configuration/validate` now returns a per-resource plan of `Create`, `Match`, or `Collision` alongside its findings. Findings carry only collisions, because that is what needs a decision; a plan needs the whole picture, or an operator cannot tell an empty target from an identical one.

  Approval is enforceable rather than advisory: passing `?acknowledgedPlan=<hash>` to the export refuses with `409` when the configuration changed after the plan was reviewed. The hash is derived from the plan contents rather than the script text, so cosmetic churn does not invalidate a review while a real change to what would be promoted always does. The audit records the acknowledged plan, or that none was.

- `FolderPermissionEscalationTests` asserts both directions at the HTTP level — `Author` is refused
  and `Manage` succeeds — because denying everyone would have satisfied the negative case while
  removing the feature.

- The Portal governance dashboard is now a durable, authorized, audited surface rather than a visual
  prototype, and ships as the **Overview** page of the Governance module.

  Eight Portal-owned tables hold governance *workflow* state — findings, decisions, glossary terms,
  steward badges, asset reviews, suppression categories, scan runs, and scoring settings. Asset
  metadata deliberately stays where it already lives: `.etlsql`/`.rptsql` sources and the lineage
  catalog. A dashboard that became the source of truth for ownership or classification would be a
  second place to change it, outside source control and outside review.

  **Every decision is version-scoped.** Ignoring a finding or accepting a risk records the asset
  version it was made against; when the asset changes, the suppression stops applying and the finding
  reopens. Suppression categories can also carry an expiry, so "temporary, removed next sprint" is a
  promise with a date on it. A suppression that outlives the thing it was granted for is not
  governance — it is a permanent exemption nobody remembers granting.

  **Scores are explainable.** Each asset returns its deductions alongside its score: the rule key, the
  points, and the reason. The UI never reconstructs the arithmetic, so it cannot reconstruct it
  differently.

  **Findings reconcile themselves.** A scan updates existing findings rather than replacing them, so
  decision history survives; an asset whose newer version passes the rule resolves automatically. No
  one closes tickets by hand.

  Authorization splits three ways, because these are three different authorities:
  `GovernanceViewer` and above can read (deliberately wide — a steward blind to other stewards' work
  cannot cover for them); `DataSteward` and above can decide, review, and assign badges;
  `GovernanceManager` or `Admin` can run scans and change thresholds, enabled checks, glossary
  content, and suppression categories. Whoever can lower the bar is not whoever works against it.
  Every mutation writes an audit row, and settings changes record the value **before** as well as
  after — "who lowered the threshold" is unanswerable from the new value alone.

- **An HA topology diagram** separating what ETL-SQL coordinates from what the operator's
  infrastructure provides. During an incident a node returning 503 is usually reporting a failure
  from the other side of that line, and the document previously described the boundary only in
  prose.

- **`HaAndSecurityDocReconciliationTests`** guards the claims that can be checked against source:
  every emitted finding code and `checks` key is documented; every topology and load-balancer
  setting appears in the configuration reference; every test named in the Automated Coverage Map
  still exists; and every `ha-soak` subcommand a runbook tells an operator to type is defined by the
  CLI. A coverage map naming a deleted test claims a certification nobody performed, and a runbook
  step is followed by typing what it says.

- The `Auto`-mode trap is asserted, not argued:
  `AutoMode_TreatsAConfiguredKeyRingAsHighAvailability_AndFailsClosedWithoutPostgres` drives the real
  `/healthz` endpoint through the case.

- **The read-only fleet boundary is now enforced.** The Enterprise Security Review Packet approves
  fleet aggregation as status polling and explicitly does not approve remote mutation;
  `FleetAggregation_ExposesNoMutatingRoutes` fails the build if a mutating route is added. A trust
  boundary stated only in a document lasts until the first convenient `POST`.

- The security review packet's scope and trust-boundary table now cover the Portal authority
  surfaces this release added — Studio capabilities, the draft review path and protected branches,
  and the disclosure surfaces (support bundle, configuration export, access simulator, posture
  endpoints) — each with the evidence that constrains it, plus the review decisions they require.

- Added identity-provider diagnostics at `GET /api/admin/identity/diagnostics`: OIDC reachability and startup validation findings, LDAP configuration, the claim value each provider-managed group expects, how many federated users have landed in no mapped group, and **break-glass readiness** — whether any active local administrator could sign in with the identity provider unreachable. An estate that federates every account, administrators included, is one provider outage away from nobody being able to correct the provider's configuration, and that is worth knowing before it happens rather than during.

  Configured secrets are reported as presence flags. A test asserts the configured client secret appears nowhere in the response at all, not merely that the obvious field omits it.

- Added `POST /api/admin/identity/diagnostics/group-mapping-test`, which resolves claim values against the configured group mappings without anyone signing in and names the values that match nothing. A claim that maps to no group is sign-in working while authorization quietly does not — the kind of gap normally found by a user reporting they cannot see something.

- Added dataset at-rest key posture at `GET /api/admin/datasets/at-rest-key/posture`: the per-version inventory of encrypted caches, a rotation preflight, verification that rotation finished, and rollback guidance. Rotation itself is unchanged.

  Preflight is the reason it exists. A cache encrypted under a key version that is no longer configured can be neither rotated **nor read**, and the only way to discover that was to start the rotation and read the failure list afterwards. Those datasets are now named beforehand, with the reason and the remedy. Key *versions* are non-secret identifiers and are named; key material never appears — a key is reported as configured or not, and a test asserts the configured key value is absent from the entire response.

- Added secret and connection posture at `GET /api/admin/credentials/posture`, which resolves the two against each other: which connections reference which secrets, which references do not resolve, when each secret was last rotated, which secrets nothing references, and which secrets a configuration export would require the promotion target to supply.

  The failure this exists for is invisible on either page alone. A connection referencing a secret that was renamed, disabled, or never created appears healthy in the connections list and healthy in the secrets list; the break lives only in the join between them and surfaces the first time something runs. No secret value is read to build the view — references are matched by name, because resolving them would mean decrypting every secret to render a page.

- `MsiUpgradeHelperTests` pins the guard for the class of bug that broke the gate: a property read
  resolving to zero or several values now throws with the values printed, instead of returning
  something a later `-ne` silently mis-handles. Mutation-verified by disabling the guard.

- Added recovery and host-identity posture at `GET /api/admin/operations/posture`, covering backup freshness, restore-drill evidence, and host enrolment consistency in one read-only view.

  Backup custody, the restore itself, and host enrolment all stay outside the running Portal — they own key material and an OS-protected bootstrap the Portal deliberately does not have. What the Portal can now do is notice when the evidence they leave behind is missing, stale, or inconsistent, and every finding names the command that fixes it rather than just reporting a problem.

  Host enrolment is checked by comparing the host's own enrolment against the Portal's machine registration: tenant or enrollment-id drift, a revoked registration, a host enrolled but never registered, a client certificate that is not the one the Portal expects, and certificate expiry with advance warning. Each side looks healthy examined alone, which is exactly why they are compared.

- `etl-sql admin restore` and `--validate` now record their outcome under job-state `admin-restore`, mirroring what `admin backup` already did, so the Portal can show when an archive was last proven readable and not only when one was last written. A backup nobody has ever restored is a hope rather than a recovery plan, so "never proven readable" is reported as a finding instead of a blank.

- `BrowserApiContractTests` — exercises the real endpoints and validates their responses against the
  same `critical-api-contracts.json` the browser client validates against. The contract already
  existed and was already enforced, but only *in the user's session*: a server-side rename reached
  production and a `TypeError` on somebody's screen was the first thing that noticed. The contract
  file is read rather than restated, because a C# copy of the field list would be a second source of
  truth that agrees with the browser's until the day it quietly does not.

- `RoleJourneyTests` — Viewer, Publisher, DataSteward and OrchestratorManager journeys through a real
  browser, asserting in both directions: the surfaces a role can use are offered, and the ones it
  cannot are absent rather than merely guarded. A navigation that offers what it cannot deliver gets
  a 403 when pressed and reads as the product being broken rather than as a permission the user
  lacks. Hiding the entry point is not enough on its own, so navigating directly to `/admin.html` is
  asserted to be refused too.

- `ContainerBuildContextTests` — guards `.dockerignore` against the Dockerfiles in both directions.
  Excluding something a Dockerfile copies breaks the image build for whoever builds a container next;
  failing to exclude something nothing copies costs nothing visible at all, which is why `tests/`
  had been shipping ~14 GB of fixtures to the Docker daemon on every build.

- Browser sessions now record failed HTTP requests with method and URL. The browser's own console
  error for a failed request says only "the server responded with 403" — no URL — which is the
  difference between a finding someone can act on and one they have to reproduce by hand.

- Added `GET /api/admin/policy-authority/impact`, which answers the question asked immediately before pressing activate: what happens when I do? Policy Authority already had every verb — validate, publish, activate, canary, roll back — and no consequence.

  **Fleet impact** separates registered machines from reachable ones: a machine not seen for over 24 hours will not pick a policy up until it checks in, so a large stale count means the rollout is narrower than the fleet count suggests. **Approval state** distinguishes a recorded reviewer from a second pair of eyes — a version whose reviewer is its own author is reported as such. **Collector consequences** warn when activating a policy that requires remote audit delivery against a collector that is not currently healthy, which starts refusing security-sensitive mutations with HTTP 503; both halves of that were already known separately, and this joins them so the answer is not discovered by activating. **Machine links** list the version each machine actually receives — the canary version when it is in the targeted group, the active one otherwise, and none when revoked.

- `js/dialog-a11y.js` — shared dialog behaviour: focus moves in on open, Tab stays inside, focus
  returns to the opener on close, and Escape dismisses. It watches for the `style`/`class` changes
  the Portal uses to show a dialog, so a new dialog gets the behaviour without its author needing to
  know the module exists. This existed as three near-identical copies inside `index.html`,
  `admin.html`, and `orchestrator.html`, and not at all in `studio.html` — three copies is not
  redundancy, it is three chances to fix a bug once and still ship it twice.

- `PortalDialogAccessibilityTests` — a source-level sweep over every page and JS module asserting
  every modal overlay is a semantic, named, modal dialog, that no page presents a dialog without
  focus management, and that closed dialogs are hidden by `display`/`visibility` rather than by
  opacity alone. It covers the dialogs no browser test happens to open, which is where this
  regresses. The detector matches overlay classes by *pattern* rather than by a list of known names:
  its first version passed with 31 green assertions while three unmarked dialogs sat behind a
  prefixed class the list did not contain.

- `PortalAccessibilityTests` — a browser lane running every page at both 1440px and 390px, asserting
  what only a browser can compute: the accessible name of every visible interactive control (derived
  the way the accessibility tree derives it), no horizontal page overflow at phone width or at 200%
  text, closed dialogs not tab-reachable, both colour schemes free of text that blends into its
  background, `prefers-reduced-motion` honoured, forced-colours substitution not opted out of, and no
  status chip whose meaning is carried only by colour. Every failure names the offending elements,
  because "3 controls have no accessible name" is a finding nobody can act on.

- `BrowserSession` now records `console.error` output alongside thrown exceptions. The two catch
  different failures: an exception stops a code path, a console error usually does not — which is
  exactly why it survives review.

- `StaticAssetCachingTests` pins both halves of the policy, since each is a mistake someone could
  make in good faith — widening `no-store` back over the assets to be safe, or relaxing the
  documents to make the app feel faster. It asserts a real conditional request returns `304` rather
  than trusting the header alone.

- Added an opt-in Playwright browser lane (`scripts/test-lane.ps1 -Lane browser`) covering the critical Portal journey end to end in a real Chromium: first-run sign-in through the forced password change, creating a user, creating a folder, publishing a report into it, and running that report until rendered rows appear. The lane runs against a Kestrel-hosted Portal on a loopback port, fails on any unhandled JavaScript exception, is excluded from the default filter by `Category=Browser`, and is wired into the pre-release gate and CI.

- Added `eng.data_quality_rules(job)` over a `PORTAL` connection, so a steward can ask which `@expect`/`@fail` rules protect each target and column in a job someone else runs, without shell access to the machine that runs it. It projects the same seven columns as the engine-local `eng.data_quality_rules` table, so one SELECT reads the same shape either way, and joins directly against `eng.data_quality_failures` to separate rules that are failing from rules that are protecting silently. The job name is required: rules bind to the statement that declares them, so there is no catalog-wide rule list.

- `js/portal-states.js` — a shared vocabulary for the four states every Portal surface has to
  render: **loading**, **denied**, **failed**, **empty**, plus `statusChip`.

  They look almost identical on screen — a mostly blank panel — which is exactly why they get
  conflated, and why the difference has to be carried by wording rather than layout. A user who
  cannot tell "you may not see this" from "the service is down" from "there is nothing here" reads
  all three as the last, because it is the only one that needs no action from them. Each state emits
  a `data-portal-state` marker so a test can assert *which* state a surface reached rather than
  inferring it from whatever text happens to be present.

  Extracted from the governance module's pattern rather than invented, and guarded by
  `PortalStateVocabularyTests`.

- Added an online-safe support bundle to the Portal. `GET /api/admin/support-bundle/review` returns every section as a reviewable document — health, deployment identity and versions, migration state, catalog counts, audit-outbox state, and the redacted Portal configuration — together with the redaction note and an explicit list of what it leaves out. `GET /api/admin/support-bundle` downloads it. Both are audited.

  Two properties make it safe to expose: it collects counts, versions and states rather than content — no report data, no dataset rows, no log bodies — and all text passes through the same redactor the CLI bundle uses. Tests assert that a report's name and title, the JWT secret, and the dataset at-rest key are absent from the entire response.

  `?acknowledgedContent=<hash>` refuses with `409` when the disclosure changed after review. The hash covers the deployment and its configuration rather than live counters: reviewing the bundle audits the review, which moves the outbox counts the bundle reports, so hashing everything would make every review stale the instant it was made and the check would degrade into noise an operator learns to bypass.

  The CLI's `etl-sql admin support-bundle` remains the recovery path for when the Portal is unavailable — it reads host files and configuration the Portal cannot.

- **Protected branches for Portal-originated commits.** `Portal:SourceControl:ProtectedBranches`
  (empty by default; exact names, or a prefix when the pattern ends in `*`) names branches a commit
  may not land on without an approved draft behind it.

  This is what the draft-approval workflow is *for*. Protecting a branch without a review path only
  blocks people; providing a review path without protecting anything only asks nicely. Together they
  mean a change reaching a protected branch has been read by someone other than its author.

  The reviewer is written into a `Reviewed-by:` commit trailer alongside the script hash, so the
  review outlives the Portal's database — someone auditing the branch a year later reads it from
  `git log` rather than needing the Portal to answer "who approved this?".

  Three details that decide whether the protection is real:

  - The branch is read **inside the repository lock**, immediately before committing. Checking it
    outside the lock protects nothing, because it can change in between.
  - Approval is matched on the **published script hash**, not on recency. A draft that was approved
    but never published cannot lend its approval to whatever happens to be on disk now.
  - An unknown branch — detached HEAD, or git unavailable — is treated as unprotected. Failing open
    here is deliberate and narrow: the commit still passes every other check, and treating "I could
    not tell" as "protected" would turn a diagnostic gap into an outage.

  Refused commits are audited as `COMMIT_REPORT_SCRIPT_DENIED`. An attempt to put an unreviewed
  change on a protected branch is exactly the event an operator wants to see, and a bare 409 would
  leave no trace of it.

- `ColumnQualityCostTests` (Performance lane) reports what each `@expect` rule shape costs against
  the same statement with no rules, and what `QUALIFY` costs against the same windowed query
  without it. It reports rather than asserts a budget: the value is knowing which shapes are
  expensive and catching one that gets much worse.

- **Quarantine preview session startup is now a measured number rather than an intuition.**
  `QuarantinePreviewStartupMeasurement` times the per-request `ExecutionSession` that
  `GET /api/data-quality/quarantine/rows` builds: **~0.8 ms median, ~1.2 ms p95**, stable to 0.1 ms
  across three consecutive runs.

  The number is scoped narrowly on purpose — construct, execute, dispose, excluding the quarantine
  target's own connector read, because that read is what a preview mostly costs and is not what a
  reusable session would change.

  It reports rather than gates. Scale certification on this repository has produced a 56% spread
  between warmed and cold measurements of the same commit, wide enough to swamp any threshold worth
  setting, so the harness asserts only an order-of-magnitude structural ceiling and writes the real
  figure into the decision record.

- The Portal data-quality queue can now read the rows behind a quarantine capture, where previously
  every target was view-only. This means the web tier opens the source connection and returns raw
  captured data, so it is gated four ways and the queue names the first gate that stops it:

  1. **The capture must have recorded its provenance.** Captures now write the shared-connection
     alias, connector type, and a catalog-backed flag into the replay manifest at the moment the
     rows are written. Portal never works out where a target lives after the fact — that would mean
     opening a production connection on an inference. The fields are nullable and appended, so
     manifests written by an older engine still deserialize; absent provenance classifies the target
     as view-only, which is what every pre-existing capture gets.
  2. **`Portal:DataQuality:AllowConnectionPreview` must be on**, and it defaults to **off**.
     Upgrading never silently starts opening production connections from the web tier.
  3. **The caller must hold a grant on that shared connection.** `DataSteward` gates the feature;
     the connection ACL gates the data. Steward access alone is deliberately not sufficient —
     quarantined rows are raw source rows carrying whatever the source carried, and letting one
     capability stand in for a grant creates an authority that accumulates implicitly and cannot be
     revoked where it was granted.
  4. **The capture must be self-consistent.** A manifest whose target names one alias while its
     provenance records another is refused rather than reconciled; picking either one would be wrong.

  The connection Portal opens is the manifest's, resolved as `SHARED:<alias>` — never an alias taken
  from the request — so policy, secret resolution, and redaction apply exactly as they do to any
  script using that connection, and the engine's own catalog authorization still runs underneath.
  A missing, disabled, and ungranted connection share one wording on purpose: the catalog does not
  disclose the existence of connections a caller cannot use.

  Every successful read is audited as `READ_QUARANTINE_ROWS` with the target, connection, status
  filter, and row limit. Reading production data is a data-access event, not a page view. The
  existing row cap, 15-second timeout, caller execution identity (so row-level security and PII
  controls apply unchanged), and error redaction are all preserved.

  The queue listing and the row endpoint resolve readability through the same code path, so the list
  cannot offer a row editor that the row endpoint then refuses.

- `GET /api/data-quality/jobs/{jobId}` resolves a submission on the namespace it actually belongs
  to, and reconciles as it reads: a non-terminal record is refreshed from the job channel and the
  outcome written back, so the answer outlives the browser that asked for it.

- Both submission paths now write a durable record to job state
  (`dq:quarantine-submission:<kind>:<target>`) — one per kind per target, bounded on purpose. The
  audit log remains the history of who submitted what; this answers the operational question, which
  is whether something is in flight against this target right now and how the last one ended.

- **A forgotten job reports `Unknown`, never `Failed`.** The in-process channel holds job state in
  memory and answers "Job not found." once the process has restarted. Passing that through would
  tell a steward their replay failed when it may well have completed, and the natural response to a
  failed replay is to run it again. `Unknown` is treated as terminal — further polling cannot
  produce an answer — and is styled as neither success nor failure, because neither was observed.
  A sandbox fixture covers that state alongside the completed one.

- `SandboxStoryTests` — drives every UI-sandbox story and fixture through a real browser, asserting
  each mounts without throwing, logs nothing to the console, and renders something into the stage.

  The sandbox already imports the **canonical** component sources, so mounting a story exercises the
  same file the Portal ships without needing a Portal, a database, or a login. It had only ever been
  run by a person clicking through it, which meant a broken fixture stayed broken until someone
  happened to open that one — and the fixtures people open least are the failure states, exactly
  where a rendering bug is least likely to be noticed and most likely to matter.

- **`GET /api/portal/navigation`** — which top-level entry points to offer *this* caller, computed
  once on the server from roles, module state and Studio capabilities. Six pages used to derive this
  from JWT claims in five different spellings of the same decision, and the two destinations above
  cannot be derived from a claim at all.

- **`js/portal-nav.js`** applies the answer and never computes one. A client-side guess is what it
  replaces, and a wrong guess that *shows* an entry is worse than one briefly missing, so there is
  deliberately no fallback rule. It stamps `data-nav-applied` when the answer has been applied —
  "hidden because you may not have it" and "not decided yet" are identical in the DOM, so without
  the marker an absence check races the fetch and goes green for the wrong reason.

- **`PortalNavigationVocabularyTests`** keeps it one vocabulary: no page may set a server-decided
  destination's visibility itself, every page carrying the top bar applies the shared answer, and
  every destination the server decides has somewhere to land on every page. Copy-paste is the
  natural thing to do when adding a page, so the invariant is enforced rather than remembered.

- **`NavigationVisibilityTests`** covers the rule server-side and in both directions — including
  that holding *some* Studio capability is not the same as holding `StudioAccess`, and that hiding
  Studio unconditionally would fail, since the negative assertion alone would accept exactly that.

- `scripts/Invoke-SmokeParity.ps1` — runs the same acceptance profile against a locally-hosted
  Portal and the production container image, then **compares the two check by check**.

  Parity is a comparison, not two independent green runs. A container run that quietly skips checks
  the local run performed would otherwise report success while proving less, and that is invisible
  in any output which only says "passed". So both sides emit per-check JSON, and any check present
  in one and absent from the other — or with a different outcome — is a parity failure even when
  both runs exit zero.

  Both targets get identical configuration and a bind-mounted script root, so a difference in the
  results is a difference in the product rather than in the harness. The local side is pinned to
  `ASPNETCORE_ENVIRONMENT=Production`, because `appsettings.Development.json` overrides environment
  variables and would otherwise have the two sides reading different configuration.

- `Invoke-AcceptanceProfile.ps1` gained `-ResultsPath`, emitting every check and its outcome —
  including **skips** — as JSON. Recording skips is what lets a comparison notice that one target
  checked less, which is the failure mode the parity run exists to catch.

  Verified end to end: **7 local checks against 7 container checks, same checks and same outcomes**,
  including a report that actually executes on both targets.

- `EveryPhysicalTypeTheAdapterCanProduce_HasAColumnBuffer` enumerates the logical-to-physical type
  map and pushes a value of each through the adapter, so a type added to one side cannot go missing
  from the other — which is how both this gap and the UUID one arose. Companion round-trip coverage
  pins negative and over-24-hour spans specifically, since those are the reason the encoding is text.

- A sandbox fixture covering the case that motivated the change: two structured rows differing only
  in target table, plus one counts-only row. Reproducible without a Portal, a database or a login.

- Studio capabilities can now be granted to a group and to a service account, not only mapped to a role in configuration. Previously changing who may publish, commit, or push meant editing `Portal:Studio:RoleCapabilities` and restarting, and could not be expressed for anything narrower than an entire role.

  `GET` and `PUT /api/admin/groups/{id}/studio-capabilities` manage a group's set and reject an unknown capability name rather than storing a typo that would read as a successful grant and do nothing. Grants are resolved at sign-in and at refresh and carried as `studio_capability` claims, so the per-request check stays a claim lookup; changing a group's capabilities signs its members out, exactly as changing an ACL does, rather than leaving a live session holding authority that was just withdrawn.

  Service accounts carry their own capability set, capped by their owner's at token issue in the same way their roles already were — an account that could exceed its owner would be a way to retain authority the owner had lost.

- Studio authority is now visible where it is reviewed. `GET /api/admin/permissions/effective/user/{id}` reports the user's roles, the Studio deployment mode, and the capabilities those roles resolve to, alongside the folder and report permissions it already returned — Studio authority is a separate axis from resource permission, and folder `Manage` does not imply the right to publish, commit, or push. Capabilities are reported as empty when Studio is disabled, since listing configured grants a deployment cannot honour would overstate what the user can do.

- Audited Studio mutations now record the capability that authorized them, on the audit row, its outbox message, and the outbox payload. Reviewing a publish or a commit no longer means inferring the authority from the route it came in on.

- `TRANSACTIONAL=ON` for `FLATFILE`, `JSON`, `XML`, `EXCEL`, and `PARQUET` now stages every output
  phase beside the target and commits with one replacement rename. Unique stages, cancellation and
  failure cleanup, 24-hour crash-residue reconciliation, and prior-target preservation are covered by
  focused certification and documented in the connector reference/snippets.

- SFTP `ATOMIC_UPLOAD=ON` now uses the server POSIX rename extension, supports execution
  cancellation, and never deletes the prior target before replacement. Servers without the required
  protocol support fail safely instead of silently weakening the guarantee.

- Added governed Studio row preview for authorized shared-connection tables and intermediate `#temp`
  tables. Source previews pass through tenant-scoped catalog ACL and schema validation before the
  server constructs the query; temp previews replay only their read-only materialization prefix.
  Preview results are cancellable, audited, redacted, and bounded by configurable row, byte, and
  wall-clock limits, with provenance shown in the shared results pane.

- Wired Portal Studio to its durable collaborative edit leases. Existing reports acquire and renew a
  five-minute session, show the owner/expiry state, pause saves on contention or disconnect, recover
  after expiry/reconnect, and release on navigation. Atomic lease updates no longer advance the
  report content version, and report authorization plus signed-tenant matching fence lease keys.

- Consolidated all six authenticated Portal headers into `portal-header.js` and migrated the Reports,
  Admin, Orchestrator, Studio, and responsive-drawer focus lifecycles to `dialog-a11y.js`. Server-owned
  navigation gating, branding, identity, themes, status/state vocabulary, keyboard containment,
  Escape dismissal, and focus restoration now attach to one shared shell contract.

- Added an explicit quarantine-preview session startup measurement. Warmed complete session cycles
  measured 0.8 ms median and 1.1 ms p95, so the Portal retains safer single-shot execution rather
  than pooling identity- and policy-bearing preview state; the measurement is the future polling gate.

- Expanded the engine-surface corpus across dataset export/publication/stewardship, OpenLineage
  export, file-sourced `MERGE`, `TRANSFORM`, a real SQLite target, ZIP compression, and Unicode
  flat-file round trips. Corpus files can opt into a minimal Portal registry and assert bounded
  output-file existence/content; all five files also pass under the deterministic spill settings.

- Expanded the shared SQL logic corpus with deterministic math, leap/month/quarter date boundaries,
  Unicode/string, null/type, regex/JSON, and cross-dialect alias/row-limit results. All 45 eligible
  files pass both normally and with the deterministic low-threshold spill configuration.

- Replaced the EBNF conformance smoke check with strict fixed-seed acceptance/rejection assertions,
  unresolved-reference validation, and minimized counterexample reporting. A dedicated cross-platform
  `ebnf` lane is now part of release and pre-release validation without joining smoke/fast; enforcing
  it corrected stale report-object, dataset, visual-source, and connection grammar forms.

- Added the platform/tenant identity separation contract. The product had one `Admin` role, which in
  a host-fixed deployment is the tenant's own administrator, so there was no platform principal to
  separate or audit. `PlatformAccessGrant` introduces one that holds authority over no tenant by
  default: a grant must name the operator, the authorization it hangs off, a reason, and an expiry,
  and expiry is checked when the grant is used rather than when it was issued. Impersonation is
  structural rather than policed — no factory takes a platform principal and yields a tenant-user
  identity, so "act as this tenant's user" cannot be expressed, and platform scope stays
  distinguishable from a tenant's own users in the resulting context.

- Added a cross-tenant negative-test contract that every future shared, multi-tenant surface must
  satisfy before it ships, following the abstract-contract pattern already used for artifact storage.
  It covers a caller naming another tenant's scoped identifier, an unscoped name resolving across
  tenants, colliding logical ids, cross-tenant overwrite, enumeration leakage, and the case where one
  tenant name is a prefix of another. The product is host-fixed today, so there is nothing shared to
  point it at yet; the guard exists first deliberately. Writing it found a gap in the tenant context
  API on its first run — there was no way to derive a tenant's key prefix for an enumeration, since
  scoping a key correctly rejects an empty id — so `ScopePrefix` was added.

- Added the server-derived tenant context contract (`ETL_SQL.Core.Multitenancy`), the foundation the
  SaaS isolation work builds on. `TenantId` is a validated value type rather than a bare string, so a
  server-derived tenant and a caller-supplied one are distinguishable at every call site, and
  `TenantContext` has no public constructor and no parse-from-request factory — every construction
  path names a server-owned origin, making "the caller told us which tenant" inexpressible rather
  than merely discouraged. Platform-scoped access to a tenant must name the authorization that
  permitted it. Caller-supplied identifiers are checked against the context rather than parsed into
  one, so naming a resource you own is possible while selecting the tenant is not, and tenant-scoped
  keys keep equal names and equal numeric ids in different tenants from colliding. Tenant
  provisioning now shares this one definition instead of its own copy.

- Adopted server-derived tenant context across the shipped Managed Dedicated cross-deployment
  surfaces. SaaS onboarding now requires a short-lived attributed authorization from current signed
  organization policy and treats `--tenant` only as a mismatch assertion. Host-fixed Portal identity
  is included in reviewed export-plan hashes and support evidence, so a caller cannot relabel a SaaS
  portability bundle or select another tenant through support tooling. Shared SaaS remains explicitly
  uncertified.

- Completed Managed Dedicated identity separation and delegated administration evidence. Tenant
  administrators retain the host-fixed Portal `Admin` role and can delegate only the narrow
  `admin.identity` automation allowlist, while platform onboarding remains a signed, expiring grant
  that cannot mint tenant sessions and now writes its own attributed audit receipt. SaaS onboarding
  can bootstrap one tenant-owned HTTPS OIDC registration through the Enterprise identity contract;
  its client secret is never accepted or persisted and must be injected at deployment.

- Added the `SaaSToEnterpriseExit` certification lane: the customer exit journey from Managed
  Dedicated SaaS to a self-hosted Enterprise deployment. It is the only lane that runs backward, and
  deliberately not a promotion — promotion preflight refuses backward moves (`DP001`) and directs the
  operator to an explicit export/restore workflow, which is the portable tenant bundle. The lane
  certifies that workflow end to end: a signed, tenant-encrypted bundle that verifies against the
  published operator key and decrypts with the customer's own key, byte-identical to the source, with
  target preflight stating every binding the target owes before anything mutates. A companion test
  verifies a bundle moved to cold storage using nothing from the exporting deployment but its
  published key.

- Added non-mutating bundle preflight with distinct exit codes per failure kind, so a runbook can
  tell an inauthentic bundle from one that merely needs bindings the target has not supplied yet.

- Added `etl-sql admin tenant validate` and `admin tenant preflight`. These are the customer-side
  verbs: someone handed a portability bundle can verify it with the shipped binary and a published
  operator key, with no account on the deployment that produced it. `validate` states plainly when it
  checked integrity but not authenticity, so a green result is never mistaken for a verified
  signature. Exit codes are distinct per failure kind — invalid, signature unverified, bindings
  required, not found — so a runbook can branch on them. `preflight` additionally lists what the
  target must supply and what will not travel at all.

- Completed the tenant portability CLI with `etl-sql admin tenant export` and `admin tenant import`.
  Export composes and signs the reviewed Portal configuration, optional Orchestrator package, and
  source artifacts; SaaS exports require tenant-recipient encryption. Import performs signature,
  integrity, binding, and collision preflight before replaying the declarative Portal bootstrap
  through the engine, and it always imports Orchestrator workloads disabled. Secrets and private-key
  passphrases are accepted only from environment variables or machine `SECRET:` references, while
  the new `admin.portability` scope exposes only the read-only reviewed export endpoints.

- Added tenant bundle import. The Portal half is applied by the engine executing the bundle's
  declarative script — the path an operator already uses — rather than through a new mutating Portal
  endpoint. Nothing mutates until preflight passes, so an inauthentic, tampered, or under-bound
  bundle cannot half-apply, and imported Orchestrator objects always arrive disabled, which is not
  configurable. Encrypted bundles are verified after decryption against the plaintext hash recorded
  at export. Collisions refuse by default; `proceed` is the only alternative, because a script
  executed as a whole offers no seam at which a single colliding object could be skipped or renamed.

- Tenant portability bundles are now composed from the exports that already exist — the Portal
  configuration export, the Orchestrator promotion package, and portable source artifacts — rather
  than leaving an operator to correlate three formats by hand. The reviewed export plan is carried
  through: the composer acknowledges the plan hash when downloading, so a configuration change
  mid-export fails the export instead of producing a bundle that differs from what was reviewed. The
  plan travels beside the script as its own payload, because the script does not say what was left
  out of it: skipped resources and content-manifest entries become manifest exclusions carrying a
  remediation, and required secrets become binding requirements the target must satisfy.

- Tenant portability bundles are now signed and encrypted. The exporting operator signs the manifest
  with a detached OpenPGP signature — an authenticity claim, verifiable offline against a published
  key — while payloads are encrypted to the tenant's own recipient key, which is the separate
  confidentiality claim. Verification runs before the manifest is parsed, so a bundle that fails it
  yields no metadata at all rather than findings derived from content that was never trustworthy. A
  SaaS-sourced export cannot be written unencrypted. Components carry both a stored-bytes hash, so a
  customer can verify integrity without holding any key, and a plaintext hash for after decryption.

- Defined the tenant-export signing-key lifecycle and public distribution process. Operators
  publish an HTTPS OpenPGP keyring, immutable per-fingerprint keys, and a lifecycle index, while
  customers authenticate first use through a second fingerprint channel and retain the exact
  verification material with the bundle. Routine rotation has a 30-day prepublication window and
  bounded signing rollback; emergency compromise stops exports, publishes revocation, and requires
  re-export unless independent immutable audit evidence proves pre-compromise signing.

- Added the `etl-sql.tenant-bundle/v1` portability bundle format and its standalone validator, the
  first slice of the tenant portability contract. The validator is the piece that makes a customer
  exit real: it verifies a bundle's schema, payload hashes and lengths, dependency graph, and
  reconciliation counts with no contact with the source deployment, so an export stays checkable
  after access to it is gone. It rejects tampered payloads, truncated bundles, manifest paths that
  escape the bundle root, unknown schema versions, and resolved secret material. Only the minimum
  configuration/artifact export mode is implemented; a declared-but-unimplemented mode throws at
  write time rather than producing a bundle whose mode overstates its contents.

- The Enterprise certification lane now proves the eight prerequisites a hosted deployment builds on
  — verifiable caller identity, per-object authorization, shared PostgreSQL/artifact providers,
  scoped secret and policy authority, durable audit, HA fencing, backup/restore, and
  upgrade/promotion — in one run against one commit. All eight were already implemented, but the
  lane covered three; the rest had passing tests wired into no lane, or were proven only inside a
  transition lane, so a hosted claim meant correlating three lanes by hand. `certification.json`
  gains a `hostedPrerequisites` array and `certification.md` a table, and the lane **fails naming
  any prerequisite left unproven**, including one whose phases never ran because an earlier phase
  stopped the run. Because it now exercises shared PostgreSQL providers, the Enterprise profile lane
  requires Docker, as the Team-to-Enterprise lane already did.

- Added fail-closed deployment-profile certification bundles. Profile, transition, upgrade, and
  Managed Dedicated lifecycle tests now emit concrete scenario evidence; the runner aggregates
  topology, artifact hashes, target-owned mappings, continuity identifiers/counts, negative proof,
  and rollback outcomes into JSON and Markdown. A stable release claims index keeps profiles and
  transitions separate, rejects dirty-worktree release evidence, and records Shared SaaS as
  `NotCertified` rather than inheriting a Managed Dedicated result.

- **Cookbook recipe 28 — end-to-end lineage across two scripts.** A CSV loads into an EDW table
  through several transformations and exports its lineage; a separate report script, in its own
  session and with different connection aliases, imports that document and shows the CSV as the
  origin of every report column, transformations included. Backed by two runnable samples the sample
  suite exercises, on SQLite so it needs no infrastructure.

- Added a shared load-aware test wait with condition-specific timeout diagnostics and optional JSONL
  timing evidence. Timing-sensitive Portal and Orchestrator slices now use observable waits, CI
  rejects new bare deadline helpers, scheduler tests isolate their throttle databases, and the real
  Portal hosted-service pipeline runs in a separate `portal-hosted` process. Three deliberate-load
  repetitions retained the existing budgets with at least 14x measured headroom.

- Added audited Orchestrator recovery from the last completed author-declared checkpoint. Failed or
  cancelled persistent runs expose the checkpoint label without exposing the opaque session handle;
  in-process, one-shot, warm-runner, and custom-template paths all honor named-label resume. The
  Portal explains ineligible runs and intentionally offers no statement-index resume.

- Added an audited one-run Orchestrator backfill form with optional input-variable overrides. The
  values flow through in-process, one-shot, warm-runner, and custom argument-template execution
  paths without changing the saved job; audit records retain only normalized names and counts. A
  concurrent trigger for the same job now returns `409 Conflict` instead of silently discarding its
  override set.

- Added an Orchestrator triage run drill-down that joins script-hash integrity, normalized
  counts-only quality failures, and the durable normalized statement timeline in one expandable
  operator evidence view. Run-level history reads avoid scanning unrelated executions, and the UI
  distinguishes loading, missing telemetry, and read failures from a clean run.

- Added the remote `admin service-account list|create|update|rotate-secret|revoke` lifecycle. New
  secrets require a create-new `--secret-out` file and are never printed; service identities can
  delegate only within their own current owner, scope, role, and Studio-capability authority.

- Completed the Orchestrator statement flight recorder across in-process, one-shot, and warm-runner
  execution paths. Statement text is normalized and capped before serialization, failed statements
  are retained, and configurable maintenance keeps failed-run detail longer than successful-run
  detail.

- Added Dialect-Aware function transpilation rewrites for common SQL functions:
  - `ISNULL(val, default)` transpiled to `COALESCE(val, default)` on Postgres and Oracle targets.
  - `YEAR(val)`, `MONTH(val)`, and `DAY(val)` transpiled to `EXTRACT(YEAR/MONTH/DAY FROM val)` on Postgres and Oracle targets.
  - `LEN(val)` transpiled to `LENGTH(val)` on Postgres and Oracle targets, and `LENGTH(val)` to `LEN(val)` on MSSQL targets.
  - `SUBSTRING(val, start, len)` transpiled to `SUBSTR(val, start, len)` on Oracle targets.
  - `SYSDATE` function call transpiled to bare `SYSDATE` (without parentheses) on Oracle targets.

- Created `DialectTranslationMatrixTests` to define a regression-preventing compile translation test matrix for Postgres, MSSQL, and Oracle targets.

- Improved the shared visual Designer with searchable grouped visual discovery, clearer neutral
  palette hierarchy, labelled authoring actions, actionable dataset/page/canvas empty states, and
  responsive laptop/tablet layouts that retain both canvas and inspector access.

- Added a shared accessible feedback system across Portal, ReportPlayer, Workstation, and VS Code
  report surfaces. Native browser alerts, prompts, and confirmations were replaced with live-region
  toasts and focus-trapped dialogs that provide validation, destructive-impact text, and
  non-secret audit-action events; a repository contract prevents regressions.

- Added one dependency-free, sanitized Markdown renderer for Portal Docs and connector Help.
  Both surfaces now render tables, admonitions, fenced code, safe links, and copy actions
  consistently; raw HTML is escaped and unsafe link protocols are rejected.

- Added a read-only Portal data-quality rule inventory beside job trends. It lists parsed output
  protections even when they have never failed, and trend aggregation now consumes normalized
  durable target/column/rule/action/owner/count rows with legacy display-string fallback only for
  older history.

- Connected Portal quarantine replay and disposition submissions to durable execution status.
  Submitted job IDs persist for the browser session, poll through Pending/Running to a terminal
  Completed/Failed/Cancelled result, show sanitized failure evidence, and refresh affected rows or
  queue state only after terminal completion.

- Added first-class `#governance/stewardship` and administrator-only `#governance/audit` routes.
  Governance navigation and internal mode changes now preserve the selected durable evidence view;
  direct non-admin Audit navigation is safely redirected to Stewardship.

- Added an Admin **Operations** control room that connects fleet and workload signals to pending
  report-access decisions, full service-account lifecycle and audit history, token-safe anonymous
  share/embed inventory and revocation, and native failure/backup/capacity service schedules and
  durable run history. Operational sources fail independently, one-time client secrets are removed
  from the page when dismissed, and host deployment/recovery actions remain outside the Portal.

- Added a generated Portal browser response contract for critical users, folders, reports, and
  execution-job APIs. The API client validates responses before page code consumes them, the
  generator has a drift-check mode, and a dependency-free test proves casing or required-field
  mismatches fail explicitly. Admin Users now consumes and posts the canonical `username` field.

- Added one shared Portal session identity model across Reports, Admin, Docs, and Orchestrator.
  Recognizable username/name/email claims now win over internal JWT subject IDs, role checks share
  one case-insensitive implementation, shell identity elements retain the immutable subject only as
  non-visible metadata, and the Audit table consumes its canonical `username` response field.

- Removed the prototype Governance dashboard from the production route graph. Governance now opens
  the durable Quarantine Queue and exposes only Quarantine and Lineage navigation; overview scores,
  exceptions, badges, glossary terms, and settings backed by static/browser-memory demo records are
  unreachable until authorized durable APIs replace them. A static guard prevents their return.

- Reworked first report execution into one preflight and one Run action. The report identity remains
  visible before a snapshot exists, required parameters are validated with accessible labels before
  enqueue, execution polls through Completed/Failed/Cancelled terminal states, and export or
  subscription controls remain disabled until a successful snapshot exists. Embedded slicers,
  date and relative-date pickers, sliders, multi-selects, search fields, checkboxes, text boxes,
  number boxes, and generated parameter forms now expose programmatic accessible names from the
  visual or parameter identity across every synchronized report host.

- Added one responsive global navigation drawer across Reports, Admin, Docs, and Orchestrator. At
  narrow widths it replaces the clipped top navigation, incorporates each workspace sidebar,
  blocks and removes background content from interaction, traps focus, restores focus on close,
  supports Escape and overlay dismissal, and retains identity, theme, and sign-out actions. A
  dedicated 390px UI-sandbox story and dependency-free shell contract pin the behavior.

- Added shared narrow-viewport patterns across Portal workspaces: dynamic tables receive contained
  horizontal scrolling and stacked action cells, forms and command groups collapse without fixed
  minimum widths, Admin and Orchestrator tabs remain scrollable, Orchestrator status cards form a
  two-column grid, and Docs content and tables stay within the viewport. The responsive sandbox
  fixtures exercise tabs, forms, tables, actions, and both sidebar/no-sidebar shells at 390px.

- Added explicit Portal Studio deployment modes and server-side authoring capabilities. `Disabled`
  removes Studio and authoring routes; `CatalogOnly` permits only granted catalog operations and
  forcibly removes external ingress/source operations; `SourceControlled` permits those operations
  only through separate grants. Designer analysis, schema, run, preview, save, snapshot, report
  source read/write, path-based publish, upload, commit, and automatic push are all independently
  fenced by `StudioAccess`, `ScriptRead`, `ScriptPreview`, `ScriptRun`, `ScriptSave`,
  `ReportPublish`, `ScriptIngress`, `SourceCommit`, and `SourcePush`. Role mappings are empty and
  deny access unless configured; Admin/Publisher names do not bypass capability checks.

- Added a first-class, catalog-scoped Portal Studio home. It groups only authorable reports by
  governed folder, offers equal Code and Design entry lanes, creates new reports through an
  internal catalog artifact rather than raw upload, and never returns the backing script path.
  Capability-aware navigation disappears outside authorized sessions; `Disabled` mode returns 404
  for both Studio entry pages and every authoring API, while `CatalogOnly` removes external source
  controls from Administration and routes editing through Studio. The shared designer now presents
  explicit accessible Code/Design tabs, and desktop/mobile UI-sandbox fixtures plus static and
  integration tests pin the workflow and interactive trust boundary.

- Replaced the empty report-library landing state with a consumer home backed by the existing
  favorites, recently viewed, featured, and popular catalog APIs. One fuzzy global search spans
  folders, report metadata, ownership/stewardship, certification, and lineage terms. Compact cards
  now use intentional catalog icons and one latest activity line (`Viewed`, `Updated`, failure,
  cancellation, running, or first-run readiness) instead of repeating three contradictory
  never-run statuses; the same concise presentation is used in folder and catalog lists.

- Added fail-closed deployment-profile certification for Solo, Team, Enterprise, SaaS, supported
  promotion paths, and N → N+1 upgrades. The cross-platform PowerShell runner composes focused
  suites and retains commit-bound JSON/Markdown summaries plus exact phase logs. A versioned journey
  fixture now defines positive and negative proof, portable versus host-owned state, and continuity
  identifiers for pipelines, rebinding, scheduling, quality/stewardship, reports, identity,
  backup/restore, promotion, topology growth, upgrades, SaaS transfer, and tenant isolation.

- Added executable lifecycle drills for N → N+1 in Solo, Team, Enterprise, and SaaS and for every
  supported promotion path. Each drill creates a versioned export/restore point, fences scheduled
  jobs, performs cutover, reconciles artifact hashes plus job/history/quality/lineage continuity,
  and proves rollback into a separate scheduler-fenced store; Portal/Orchestrator upgrade coverage
  also migrates populated N schemas to HEAD and composes coordinated backup/restore proof.

- Checked-in workspace policy now enforces required `SCRIPT` tags and materialized-output `COLUMN`
  tags as local lint errors. Workstation automation returns a non-zero exit before target writes when
  `@owner`, `@steward`, or another required tag is absent, while failing `@expect` rules with
  `@fail: THROW` retain the same non-zero runtime gate; both paths are covered by the Solo
  certification lane.

- Certified the Team/SME quality loop without Portal: real SQLite run history supplies three-run
  `HISTORICAL` baselines, out-of-band `ASSERT JOB` results trigger both SMTP- and WEBHOOK-typed
  notification sinks, and the Team certification lane composes those checks with scheduler retry,
  dispatch, and durable quality-history coverage.

- Added signed organization metadata policy with `REPORT`, `DATASET`, and `COLUMN` required-tag
  scopes. Portal report creation and script replacement now verify the active tenant/environment
  envelope and parsed dataset lineage before catalog mutation, failing closed on missing tags or an
  invalid policy. Enterprise certification proves an OIDC-authenticated Publisher cannot publish a
  dataset missing required `@classification`, and that rejection creates no report row.

- Added secret-safe deployment promotion for the complete currently eligible state surface. The
  Portal bootstrap now preserves folder/report catalog ownership alongside identities, governed
  connections, `SECRET:` references, ACLs, reports, subscriptions, and alerts. The provider-neutral
  `etl-sql.orchestrator-promotion/v1` package exports/imports jobs, schedules, notifications,
  ownership attribution, quality history/failures, lineage, and tags while rejecting raw
  credentials and leaving resolved secrets, tokens, caches, and keys behind.

- Added `admin promotion export`, `validate`, and `import` with repeatable target bindings,
  duplicate/dangling-reference/collision checks, exact historical timestamps, idempotent replay,
  and scheduler-safe disabled jobs at the target. Documented and tested the supported Solo → Team
  and Team → Enterprise journeys, including preflight, backup, fencing, cutover proof, and rollback.

- Added admin-only Portal bootstrap validation at `POST /api/admin/configuration/validate`. It
  applies target bindings in memory and reports parse failures, raw credentials, duplicates, unused
  mappings, and same-name/different-state collisions across identities, folders/owners, governed
  connections, and reports without mutating the target catalog.

- Added direct Solo/Enterprise SaaS tenant onboarding through `admin promotion saas-onboard`.
  Onboarding fixes tenant authority at the host boundary, copies only hashed portable artifacts,
  imports eligible catalog/quality/lineage state with jobs disabled, stages the Portal bootstrap,
  creates disjoint database/artifact/key/cache/queue/audit/telemetry/support roots and secret
  namespaces, applies runtime concurrency settings, records resource limits, defaults support and
  activation off, and refuses to overwrite an existing tenant. All staged paths now resolve beneath
  that fixed root and staged bytes are rejected above quota. SaaS certification proves negative
  cross-tenant reads for lineage, quality failures, schema-only PII results, security-event queues,
  Portal audit/outbox rows, and runtime security caches.

- Added `etl-sql admin promotion preflight`, a mutation-free deployment-profile inventory with the
  versioned `etl-sql.deployment-preflight/v1` JSON contract. It hashes portable scripts, policies,
  and operational evidence; identifies exportable catalog state and required `SECRET:`/`SHARED:`
  target bindings; summarizes ephemeral state; records protected files without reading, hashing,
  sizing, or logging their contents; and fails closed on raw credential literals, unsafe traversal,
  unsupported scale, or backward profile transitions.

- Added schema-only `etl-sql scan --pii` for supported local files/directories and cataloged
  database tables through credential-safe `SHARED:` aliases. It applies the nearest
  `etlsql-policy.json`, retains file/line remediation locations, emits a versioned JSON contract,
  enforces bounded recursion/file counts, and never reads or reports row values or credentials.

- Added one versioned, transparent stewardship scoring service shared by CLI scans, local engine
  catalogs, Orchestrator APIs, and remote/Portal consumers. `eng.stewardship_score` exposes global,
  job, and table component numerators, denominators, percentages, counts, timestamps, policy
  weights, and definition version without an opaque composite; `eng.stewardship_gaps` preserves
  source locations and reconciles exactly to every component total. Current lineage wins over
  history and the newest durable remediation wins over older catalog entries.

- Added portable Data Quality Health and Stewardship Scorecard `.rptsql` operator reports over the
  shared `eng.*` contracts, plus a runnable one-person workflow with checked-in policy, tagged and
  quality-gated pipeline, local SQLite Orchestrator schedule, optional transition notifications,
  and a copy-pasteable guide. Shared fixtures cover empty/first/clean/warn/quarantine/critical/stale,
  missing-tag, unowned-protected-data, and recovery states; acceptance tests pin workstation,
  Orchestrator, and Portal count/score parity and the steward-only quarantine boundary.

- Published the normative four-profile deployment standard with Green/Yellow/Red/justified-N/A
  evidence for every required capability, smallest-safe forms for enterprise-oriented features,
  regulated/air-gapped/high-volume/HA/DR/residency overlays, and mandatory feature-design and
  release portability reviews. Current gaps—especially SaaS isolation and incomplete promotion
  certification—remain explicitly Red or Yellow rather than being presented as shipped claims.

- Added the versioned workspace-root `etlsql-policy.json` contract and JSON Schema for required tags
  with scope/exclusions, regex-based protected-data suggestions, and default warning/failure
  thresholds. CLI runs discover the nearest workspace policy and fail with path/line/column
  diagnostics when JSON, scopes, regexes, or threshold relationships are invalid.

- Added `etlsql run --quality-summary` for stable counts-only terminal evidence and
  `--output-json <path>` for a versioned CI artifact containing run totals, normalized rule counts,
  and structured column metrics without failed sample values; errors are secret-redacted.

- Added the no-Portal quality catalog: `eng.data_quality_status` provides canonical current/local
  run identity, timing, status, processed/warned/quarantined totals and percentages, failed-rule
  count, observed freshness, and redacted errors; `eng.data_quality_failures` provides normalized
  counts by run/target/column/rule/action. Both tables are queryable through a remote
  `ORCHESTRATOR` connection, while `eng.job_history` now exposes its persisted quality totals.

- Added `ASSERT JOB ... WITH (FAIL_ON_WARN = TRUE)` so warned rows can fail unattended CLI and
  orchestrated runs with a reliable non-zero exit code without requiring a notification channel.

- Documented and pinned the no-service Task Scheduler/cron/CI quality workflow and the optional
  local SQLite Orchestrator progression for scheduling, history, baselines, managed notifications,
  and recovery state; notifications remain optional for exit codes and queryable evidence.

- Added Windows MSI in-place upgrade certification on `release/**` pushes and version tags. The
  elevated ephemeral runner installs the prior release, writes a sentinel into the registered
  install location, upgrades to the candidate, rejects side-by-side installs, verifies preserved
  data and the installed CLI version, then uninstalls and retains verbose logs plus JSON evidence.

- Added one governed managed-connection lifecycle across Portal and Orchestrator admin blocks,
  including create/alter/test/show/drop dispatch, Orchestrator REST administration, redacted
  configuration inspection, disabled-definition export/import, impact analysis, `WHAT_IF`,
  fail-closed authorization, and redacted security audit. SMTP and WEBHOOK catalog lifecycle and
  Portal-to-Orchestrator notification delivery use the same contract.

- Added the queryable `eng.*` catalog for session state, lineage/tags, governance, data quality,
  connections/tables/variables/views, diagnostics, jobs/history/state/metrics, bundles, Portal
  catalog data, and parameterized catalog functions. The `eng` schema is reserved, connection
  configuration is redacted, `SELECT *` lint is off by default, and completion discovers catalog
  tables after `eng.`.

- Added complete executable-statement `ToSql()` serialization, a generated statement-surface
  inventory, canonical parser → formatter → parser coverage, retired-form rejection tests, and
  production-parser validation for documentation, help, snippets, and samples. Syntax indexes,
  references, administration guides, architecture contracts, migration material, configuration
  export, LSP grammar, and release notes now follow the same canonical contract.

- `DROP BUTTON [IF EXISTS] <name>` and `ALTER BUTTON <name> (...)`. `BUTTON` previously had `CREATE`
  only, even though the engine already removed buttons on `DROP` and `CREATE BUTTON`'s
  duplicate-name error told the author to "use CREATE OR ALTER or DROP BUTTON first" — advice the
  parser then rejected. `ALTER BUTTON` patches `TITLE`, `TOOLTIP`, `OPTIONS`, `ACTIONS`, and `STYLE`,
  and enforces the same `ON_CLICK`-only rule as `CREATE BUTTON`.

- `ALTER PAGE` can patch `VISIBLE` and `REFRESH`; `ALTER CONTAINER` can patch `VISIBLE` and `ICON`.
  These are fields the objects have always had, reachable until now only by redefining the object
  and restating its whole layout.

### Changed

- **The administration docs are readable by deployment profile.** They were a mechanical split of
  three large manuals and still read like one: 28 files carrying orphaned section numbers that
  started at `4.1` or `## 6.` with no sections 1–3 anywhere, and content that mixed profiles
  silently — a Solo reader working through "Secrets and Keys" met the Portal JWT secret and the
  Orchestrator API key, neither of which exists on a workstation.

  - **All orphaned numbering removed** across 28 files, along with the duplicate `# Title` /
    `## Title` pairs the numbers were hiding, and the resulting heading-level skips.
  - **New `docs/administration/by-profile.md`** gives Solo,
    Team, Enterprise and SaaS each an ordered path through the same task-oriented pages. The docs
    stay organised by task — a fact still lives in exactly one place — and this is the other axis.
  - **A `## By deployment profile` band on the eleven pages where behaviour genuinely differs**,
    saying plainly what each profile does and which are **N/A**. Reference-only pages did not get
    one; a band that says "same for all profiles" trains readers to skip the band.

- **Fourteen dangling `§` cross-references now point somewhere.** The split left references like
  "see §9 below" and "(§11.3)" aimed at sections of the old monolithic manual that no longer exist
  under those numbers — dead navigation that no link checker catches, because they were never
  links. Each is now a real link or has been reworded.

- **Four genuinely broken anchors fixed**, including a link into a heading that had been deleted as
  a duplicate.

- **Generated section indexes no longer describe pages by their first heading.** Fifteen pages had
  no prose between the title and the first section, so the generator quoted things like
  "## 8. Backup & Maintenance" as the description. Each now opens with a sentence saying what the
  page is for, which improves the page and the index together.

- **An `@expect` rule the runtime does not implement now fails the statement instead of passing
  every row.** The two per-row rule switches each ended in a `default` that returned "passed", so a
  `ColumnRule` record added without its runtime arm would have reported the data clean. The two
  switches were also full copies of each other — one existed only because `EXPR` needs the
  evaluator — so they are now one predicate with a thin async wrapper for that single form.

- **`Engine.md` now documents four engine subsystems it had never mentioned** — data-quality rules,
  the columnar plan family, row-level security, and `SECRET:`/organization-policy enforcement. It
  had described the v0.10-era engine accurately and stopped growing with it: 69 mentions of the
  external spill engines, zero of any of these.

  Extended rather than split into new pages. The document is organised by mechanism and these are
  mechanisms; splitting would have put the fast-path *disqualifiers* in a different file from the
  fast paths, which is the exact confusion that prompted the work. The new sections explain how the
  pieces fit and link to `DataQualityRules.md` and `RowLevelSecurity.md` for detail rather than
  restating it.

- **Removed the governance dashboard's demo fallback and browser-memory workflow state.** The
  previous module substituted a hard-coded set of assets whenever its API call threw, and kept
  findings, decisions, glossary terms, badges, and scoring thresholds only in the browser. Both
  failures are invisible from the outside: the page renders, the numbers look plausible, and nothing
  on screen marks the estate being described as fictional or the decisions as unsaved.

  The dashboard now renders four states honestly and separately, because collapsing them is how a
  governance surface lies: **loading** (no claim made yet), **unauthorized** (a view you cannot see,
  naming the roles that grant it), **failed** (we asked and could not find out — nothing is invented
  to fill the gap), and **empty** (we asked, and the answer is genuinely nothing). A fifth
  distinction gets its own banner: **never scanned** is not *no findings*, and a KPI tile reading
  zero cannot tell those apart on its own.

- Extracted the stewardship posture calculation out of `CatalogController` into
  `StewardshipProjection`, so the governance scan and the stewardship view answer "is this asset
  missing metadata?" from one definition. Two copies would let the queue and its findings disagree
  about the same asset with no way for a steward to tell which is wrong.

- Replaced the 2,200-line governance sandbox story with one that imports the real module and injects
  a mock API, matching how every other story works. The old story re-implemented the entire UI, so
  it could look correct while the shipped module was broken — and its fixture data sat in the repo as
  a ready-made source of fake governance records.

- **Every guide now says who it is for.** A one-line `> **Applies to:**` banner names the deployment
  profiles the guide covers. Deliberately a line rather than the four-row table the administration
  docs use: most guides describe the *language*, which is identical from a workstation to SaaS, and
  a four-row table repeating "same for all profiles" would bury the two guides where it genuinely
  differs — `portal-user.md` and `catalog-search.md` need a Portal, and each now names the Solo
  alternative instead of leaving a workstation reader stuck.

- **`data-stewardship-impact.md` no longer requires a Portal it does not need.** Its prerequisites
  said "Portal or Orchestrator must persist lineage", which reads as *deploy a service first*. The
  CLI writes lineage on its own during a plain `etl-sql run` — measured, not assumed: running
  `protected_data_audit.rptsql` through the CLI returns 177 rows on a workstation with no service
  anywhere. Corrected, and the one genuinely Portal-dependent bullet now says so.

- **Nine guides carried orphaned section numbering** from the same manual split as the
  administration docs — `sample-guide.md` alone had 39 numbered headings. Removed, along with a
  duplicate `# VS Code Extension` / `## VS Code Extension` pair.

- **The MSI upgrade gate no longer needs a 26-minute CI run to find a typo.** Its first real
  execution failed on pure logic — a multi-value read that turned a comparison into an array filter
  — after twenty-odd minutes of downloading a previous release and building an installer. Nothing
  about that bug needed an MSI, elevation, or an install.

  - Non-elevated logic moved to `scripts/MsiUpgrade.Helpers.ps1`, side-effect free on load.
  - **`Test-MsiUpgrade.ps1 -StaticChecksOnly`** runs the upgrade contract — same `UpgradeCode`,
    ascending `ProductVersion` — with no elevation and no install, in about a second, on any
    machine. The workflow runs it as its own step before the install sequence, so a failing log says
    which half broke.
  - The push trigger is **path-filtered** to the installer, its scripts and `Directory.Build.props`.
    A documentation change previously paid the full 26 minutes for nothing.

  This matters more than convenience: the elevated half has no local path on Windows Home, where
  Windows Sandbox and Hyper-V are unavailable. Pushing everything testable out of it is what makes
  the script maintainable at all.

- `tests/` and `artifacts/` are excluded from the Docker build context. `docs/` and `snippets/` are
  deliberately not: both images copy them for the embedded runtime help.

- The browser lane shares one Portal host and one Chromium across all its test classes
  (`ICollectionFixture`) instead of building them per class, and `PortalBrowserFactory` now stops the
  Kestrel host and waits before disposing it — `IHost.Dispose()` only signals shutdown, so teardown
  had been racing the deletion of the temp directory it was still using. Both are real fixes; neither
  resolved the lane's intermittent startup failure, which survives across separate processes and is
  recorded with the current diagnosis in `docs/releases/flaky-test-stability.md`.

- **Portal static assets are now revalidated rather than re-downloaded.** Every response, static
  assets included, carried `Cache-Control: no-store`, so each page navigation refetched roughly
  3.4 MB — about 1.9 MB of it vendored libraries (`echarts`, `tabulator`, `arrow`) that had
  not changed since install.

  The policy is now split by what a response is. Documents and API responses stay `no-store`: they
  carry catalog contents, identity and report data, and none of that belongs in a browser cache or
  an intermediary. The asset roots (`/js/`, `/css/`, `/designer/`, `/img/`, `/maps/`) get
  `no-cache, must-revalidate`, which is not "do not cache" — it permits storage and requires
  revalidation on every request, so the browser sends its ETag and receives a 304 instead of the
  file. Staleness risk is nil: an upgraded Portal returns a new ETag and the browser refetches.

- **Removed 71 inert `?v=0.17.0` cache-busting query strings** from the Portal pages. With
  `no-store` in force nothing was cacheable, so they had never done anything — they implied a
  mechanism that was not there. Correspondingly, `Set-Version.ps1` does not need to rewrite them and
  no version-agreement check is needed.

- Moved the support-bundle redaction rules to `ETL_SQL.Core.Common.SupportBundleRedactor`, with the CLI builder now delegating to them. Two hosts producing support material from two nearly-identical rule sets would eventually diverge, and redaction that is *almost* the same in two places is worse than none: it yields two artifacts that look equally safe and are not. Behaviour is unchanged.

- **The reusable read-only preview path is deliberately not built.** The threshold for revisiting it
  is a 250 ms median or 500 ms p95 — where per-poll overhead becomes a visible fraction of a
  one-second poll interval. The measurement is roughly 300× under that, so the optimisation would
  buy about a millisecond per request while requiring the parsing, linting, policy, RLS, timeout,
  row-cap and redaction guarantees to be re-established across a shared session. Those guarantees
  are the whole reason the preview may read raw quarantined rows at all.

  Polling and dashboard refresh are therefore not blocked by session cost. Recorded with its trigger
  in `DataQualityRules.md` alongside the other demand-triggered scale items.

- Session-local (`#temp`) quarantine targets keep their existing view-only reason. They are the case
  worth stating: the manifest outlives the run but the table does not, and a preview session
  auto-creates the table empty — a steward offered a row editor would read "no rows" as "nothing was
  quarantined".

- **The pre-release gate now states what it actually verifies.** Three phases in
  `Test-PreRelease.ps1` described their lanes in the abstract while the lanes had grown well past
  the description — a gate whose coverage you have to infer from test filters is one nobody can
  review.

  - The **browser lane** phase now names the critical journey, the four non-Admin role journeys,
    the accessibility and responsive checks at 1440px and 390px, the accessibility-tree snapshots,
    and the sandbox story mounts.
  - The **Portal lane** phase now names the release-acceptance journeys it already carried: the
    role/permission authorization matrix, departmental environment isolation across two
    deployments, policy authority and distribution, module gating, Studio capabilities, and the
    browser API contract.
  - A new **local/container smoke parity** phase runs under `-IncludeDockerIntegration`, comparing
    the two targets check by check rather than accepting two green runs.

- Hardened the local release gate so independent phases continue after unrelated failures, dependent phases skip when prerequisites fail, formatter drift gives an accurate rerun instruction, and release-process setup documents detached exact-commit validation plus staged-file formatting hooks.

- The three `admin_operations` templates now declare the failure they are supposed to produce when
  run as shipped — `backup_and_report` because an uninjected variable is an error rather than a
  silent default, `capacity_report` and `daily_failure_digest` because the SMTP password is still
  the `ENC:` placeholder. Each asserts its exit code and message, so the guardrail is covered rather
  than the sample being known-broken.

- Publishing a report by script path requires `Portal:Studio:Mode=SourceControlled` **and** the
  `ReportPublish` capability; `RequireStudioCapability` answers 404 in any other mode. Without those
  settings the acceptance profile silently seeds no report, three checks vanish, and the run still
  exits 0 — documented, because a green run that checked less is the most misleading outcome
  available.

### Removed

- **Chart.js is no longer bundled.** The library was superseded by Apache ECharts and had since become
  entirely inert: no `<script>` tag loaded it, and no `new Chart(` or `Chart.register` call site existed
  anywhere in the tree. It survived only as a 203 KB blob replicated across five host directories by the
  shared-asset sync, plus entries in the third-party inventory, notices, and SBOM generators.

  Removing it drops 203 KB from every runtime that serves report assets — Portal, ReportPlayer,
  Workstation editor, and the VS Code extension — and removes a dependency the project was still
  declaring, and would still have been obliged to audit and patch, without using it.

  The harvested WiX fragment (`src/ETL-SQL.Installer/wwwroot.wxs`) was updated to match. It is
  regenerated by `heat.exe` on every MSI build, so the entries would have disappeared on their own, but
  the checked-in copy no longer references files that do not exist.

### Fixed

- **The folder permissions panel could show one folder's grants under another folder's name.**
  Opening a folder's permissions sets the heading immediately and then loads the ACL; the table was
  only written on success, so a failed load left the previous folder's rows in place. The panel was
  not blank, it was confidently wrong, and wrong about access control specifically — an
  administrator could read another folder's grants as this one's, and the Revoke buttons still
  carried the other folder's group ids while the revoke call sent this folder's.

- **A failed group-membership read rendered as a group with no members.** "Nobody is in this group"
  and "we could not find out" lead an administrator to opposite actions, one of which is deleting
  the group or granting its access elsewhere.

  Both panels now clear before the request and render the shared `failedState` after one — the
  four-state vocabulary exists precisely so a failure is never shown as an emptiness.

- **Three dialogs were announced as just "dialog"** — the governance quality-trend modal and two in
  the data-quality queue (trend and row editor). Each already had a visible `<h2>` title; none was
  linked to it, so a screen-reader user was told a dialog opened and nothing about which job or
  target it concerned. All three now use `aria-labelledby`.

  Caught by `PortalDialogAccessibilityTests` on modals added after that guard was written, which is
  the case it exists for.

- **Three architecture docs named interfaces that no longer exist.** `Engine.md` listed
  `ICryptoService` and `ISecurityService` for crypto and security — neither is in the tree — and
  `LanguageServer.md` told contributors to implement `ICodeActionHandler`, which this server does
  not implement. Corrected to the types that are actually there: `CryptoUtils`, `SecretRedactor`,
  `ISecretLifecycleProvider` and `IEnterpriseEnrollmentProtector`; and the four OmniSharp handler
  interfaces the language server really uses.

- **`FolderPermission` comparisons no longer depend on the enum's numeric order.** The values are
  persisted as integers in every ACL row, so `Author` had to be appended as `3` rather than inserted
  in its rightful place between `Execute` (1) and `Manage` (2) — inserting it would have renumbered
  `Manage` and silently reinterpreted every grant already in force, with no migration able to detect
  it because the rows stay valid and merely mean something else.

  That left declaration order lying about authority. Roughly forty `permission >= FolderPermission.Manage`
  comparisons would each have granted `Author` everything `Manage` has, and four integer `Max`
  operations picking the strongest of several grants would have chosen `Author` over `Manage`,
  *downgrading* anyone who held both.

  `FolderPermissions.Rank()` now defines the ladder (Read < Execute < Author < Manage) independently
  of the stored value; `AtLeast()` replaces every ordinal comparison and `Max()` every integer max.
  The conversion was done in two phases — behaviour-preserving first, verified against the full
  suite, then the deliberate grants one gate at a time — and `FolderPermissionOrderingTests` fails
  the build if any production file compares permissions ordinally again, because writing `>=` here
  is the natural thing to do and silently escalates.

- **The MSI upgrade gate could never have passed.** `Get-MsiProperty` in `Test-MsiUpgrade.ps1`
  returned `Object[]` — `('', '{GUID}', '')` — because two COM calls emitted to the pipeline
  unsuppressed. PowerShell's `-ne` against an array is a *filter*, not a comparison, so the
  UpgradeCode check reported "UpgradeCode changed" for two identical codes and failed the run.

  Reproduced against the shipped v0.16.0 and v0.17.0 MSIs rather than inferred from the log: the
  reader now returns a single trimmed `String`, and identical codes compare equal. This was the
  gate's first ever execution, which is exactly what it was built to discover — though it found a
  defect in itself rather than in the installer.

- **`feedback.js` was missing from the `.gitattributes` LF pin list**, so a Windows CI checkout
  converted it to CRLF, the canonical and host copies stopped being byte-identical, and
  `sync-assets.js -Check` failed the build for a file whose content was correct. It was the only
  shared asset not pinned — added when the feedback dialogs were unified, without the matching
  attribute line. The file's own comment predicted this failure mode in advance.

- **A spilling query could fail when a column's CLR type varied between batches.** Engine rows are
  dynamically typed, so the same column can hold a `DateTime` in one batch and the same instant as a
  formatted string in the next — or be entirely NULL in one batch, leaving no type evidence at all.
  `ColumnBatchAdapter` inferred each batch's types independently from that batch's own values, while
  the columnar spill writer locks its Arrow schema on the first batch and rejects every later one
  that disagrees. The result was `Column batch field N ('JoinDate', utf8) does not match spill field
  'JoinDate' (timestamp)` partway through a large write.

  Both spilling paths now establish the logical schema once for the whole relation and build every
  later batch against it. Fixes the `flatfile_sink` and `window_sink` samples.

- **Four settings added this release were missing from the configuration reference** — the document
  an operator actually opens when configuring a deployment. They existed in guides and architecture
  prose, which is not where anyone looks to set a value:

  - `Studio.RequireApprovalToPublish` — the draft → review → publish workflow
  - `SourceControl.ProtectedBranches` — branches a Portal commit may not reach unreviewed
  - `DataQuality.AllowConnectionPreview` — the quarantine row-preview kill switch
  - `ReportApprove` — the tenth Studio capability, absent from the capability list operators copy

- **The governance dashboard's KPI tiles were unreadable to a screen reader.** Five tiles rendered
  as sibling `div`s collapsed into one undifferentiated run of text: *"0/0 Governed assets 0% at or
  above 80 0 Below threshold Need follow-up…"*, with no number attached to any label. Each tile is
  now a list item carrying its whole meaning in an accessible name.

- **The governance state banners were anonymous bold runs**, so a user navigating by heading could
  not find the most important sentence on the page — that the estate has never been scanned, or that
  they are looking at a denial rather than an empty estate. They are now headings.

  Both were found by the new snapshots on their first run, on code added earlier in this release.

- Fixed the PostgreSQL model snapshot, which had not been regenerated since the alert-notification and share-link-name migrations. Any new PostgreSQL migration scaffolded against it re-proposed operations those migrations had already applied — a migration that would have failed against every migrated database — and one entity carried an index over a column it does not have.

- **`docs/architecture/Portal.md` said three Identity roles were seeded. There are eight** — five of
  them security-relevant, including every governance role. An architecture document that is
  confidently wrong is worse than a missing one: a missing document sends people to the code, and a
  wrong one stops them.

  Also corrected there: the authorization model is **two independent axes** (a role decides which
  class of operation, an ACL decides which resources); folder `Manage` is authority over the reports
  in a folder, **not** over the folder itself; and `FolderPermission` must never be compared
  ordinally, because `Author` is stored above `Manage`.

- **Eleven API areas were entirely undocumented** — branding, OIDC, service accounts and tokens,
  both policy-authority surfaces, configuration promotion, Studio, designer, docs, and fleet — along
  with the governance, report-draft and data-quality endpoints added this release, and three
  persisted entities.

- **A composite rule naming a column the statement does not project now fails instead of passing.**
  Row lookup by name yields NULL for an absent column and a NULL key part skips the rule, so a
  single typo in `UNIQUE WITH (TenantId, BokingRef)` produced a rule that reported clean because it
  never ran on any row. Both `UNIQUE WITH` and the new `EXISTS WITH` now reject an unprojected
  column at statement start, naming the column.

- **`EXISTS IN` probed its reference key set with a linear scan.** The set was built with the right
  comparer but queried through `Enumerable.Contains` with an explicit comparer, which bypasses the
  `HashSet` and walks every key — making a dimension lookup O(rows x keys) per statement. It now
  probes the set's own comparer.

- **`IN`/`NOT IN` rendered the row's value once per candidate.** The pairwise comparison converted
  both sides on every comparison, so an N-item list materialized the row's value N times per row.
  Each literal's rendered text and decimal form are now prepared once per rule, and the row's own
  text at most once per row — and only when some pair actually reaches the string path.

- **The compiled-regex cache for `MATCHES` was keyed by the rule record**, so every lookup hashed
  the whole pattern string to find an entry that never moves. It is now keyed by rule instance.

Both matter most where rows *fail*: a passing row was already close to free, but a quarantine-heavy
load runs the comparison to exhaustion on every row.

- **The formatter silently dropped `ON FAILURE` clauses.** `ToSql()` on a quarantining SELECT
  returned a statement whose `@fail: 'QUARANTINE'` tags routed nowhere, which is a hard error on the
  next run — the mirror image of the comment-stripping failure the symmetric clause/rule check
  exists to catch, and just as quiet at the point where it happened. A round-trip test now covers
  all three clause forms and both `WITH` options.

- **A claim I had recorded about the engine was wrong, and checking the source caught it before it
  reached the document.** The note said "the columnar fast-path gates exclude rule-carrying
  statements". They do not. Three `!HasDataQualityRules(...)` guards protect **SQL pushdown** —
  work sent to a remote database never reaches `ColumnQualityValidator`, so a statement carrying
  `@expect` is kept local. The native columnar `SELECT … INTO` is guarded separately on
  `!DataQuality.TracksNullCounts`, because a columnar batch copy never visits the values that
  null-counting needs. Same principle, two distinct mechanisms.

  Both are recorded as correctness constraints rather than tuning, because removing either to
  recover throughput silently stops enforcing the feature it protects.

- Two behaviours documented for the first time while verifying the above: `RecordPlanDecision` /
  `PlanDecisionReasonCodes` record *why* a fast path was declined, so a slow query does not have to
  be explained by guesswork; and **administrators bypass `HAS_GROUP` / `HAS_ROLE` by default**, so a
  row-level-security filter does not restrict an admin.

- **`Engine.md` now covers adaptive execution, and says the thing that matters about it.** Nine
  files under `Core/Adaptive` and no architecture page mentioned them. The accurate statement is
  narrower than their presence suggests: `AdaptiveExecutionController` computes bounded setpoint
  advice and `Evaluator` holds an advisor, but **no execution pipeline reads it**, so the subsystem
  records what it would do without changing how anything runs.

- **`AdaptiveExecutionController.md` said "DRAFT — no implementation yet"** while Slice A was
  implemented and wired into the evaluator. Corrected against the source, including the part still
  outstanding: pipelines opting in at safe boundaries.

- Governance mutations reloaded data without redrawing, leaving the steward looking at the state
  before their change — which reads as the change having failed. Caught by the new browser lane test.

- Five parallel dashboard reads on a cold database raced to create the singleton settings row and
  returned a 500. The unique index makes the race safe: one insert wins and the losers read the
  winner's row. Deliberately not serialised behind a process-local lock — Portal runs multi-node, and
  the other node is not holding your lock.

- **Two Governance views were offered to roles that cannot open them.** *Overview* needs
  `GovernanceRead` and the *Quarantine Queue* needs `DataQualityStewardAccess`, but both were shown
  to every signed-in user. Only *Audit Evidence* was gated. Both are now revealed to the roles their
  APIs accept, matching the pattern Audit already used.

  The Governance section itself stays visible to everyone, and that is deliberate: *Lineage Search*
  and *Stewardship* are open to any authenticated user, and tracing where a number came from is
  exactly what a report consumer needs them for.

- **Clicking Governance routed everyone to the quarantine queue** — so a report consumer's first
  click on the section landed them on the one view they are refused. The landing view is now the
  first one the user can actually use, resolved in a single place so the top-level link, the bare
  `#governance` hash, and the sidebar cannot disagree.

- **Deep links to a Governance view a role cannot use now redirect rather than opening.** Hiding a
  navigation entry does nothing for someone who was sent a link, which is how these URLs mostly get
  reached.

- **The README generator published non-prose as page descriptions.** It skipped `/*` and `//` but
  not HTML comments, headings, blockquotes or code fences, so two guides described themselves as
  `<!-- SearchPortalCatalogStatement -->` (an AST-name marker), several as their first section
  heading, and one as `ETL-SQL run nightly_load.etlsql --log` — the first line inside a code block.
  It now skips all of those and tracks fences, which fixes the generated indexes across the whole
  of `docs/`, not just guides.

- **Half the `/healthz` readiness finding codes were undocumented.**
  `PortalTopologyReadinessService` emits six; the HA certification document listed three "such as"
  examples. The three missing ones included `ha-requires-session-affinity` and
  `ha-requires-orchestrator-postgres` — both of which hold a node out of load-balancer rotation. All
  six are now documented in a table with cause and remedy, because a finding code is what a 503 says
  about itself and the string an operator greps for mid-incident.

- **`Portal:Topology:*` was absent from the configuration reference entirely** — five settings that
  decide whether `/healthz` returns 200, missing from the document an operator opens to configure a
  deployment. The same class of drift the previous reconciliation found in the Studio settings.

- **`ExpectedMode: Auto` can hold a working node out of rotation, and nothing said so.** `Auto`
  infers `HighAvailability` from PostgreSQL *or* a configured `Portal:Storage:KeyRingPath`, and never
  infers `Departmental`. So a single-node SQLite Portal that merely moved its key ring off the
  default path is classified HA, `RequirePostgresForHa` applies, and `/healthz` returns 503 with
  `ha-requires-portal-postgres` — a node that is otherwise working, that the load balancer stops
  routing to. The inference is right (a shared key ring is a multi-node signal) but the contract it
  turns on is strict, and a departmental deployment on PostgreSQL is the common case. Now stated in
  the HA certification document, the configuration reference, the HA administration guide, and as a
  **Required** step in the production-readiness checklist.

- **The Studio capability probe was itself role-gated.** `GET /api/studio/session` exists to answer
  "what may this user do in Studio?", and the shell calls it on every page load to decide whether to
  offer Studio at all. It sat behind the controller's `Admin,Publisher` requirement, so for every
  other role the answer was a 403 rather than an empty capability list — a console error on every
  sign-in, and a capability check that could not be asked without already holding the capability.

- **The browser test lane's intermittent total failure is root-caused and fixed.** The
  security-event outbox defaults to a machine-wide SQLite database under `LocalApplicationData`,
  opened before the host is built. A previous test process still shutting down held it, so the next
  host failed to start at all and every test reported a millisecond. Each test factory now gets its
  own file. Worth knowing beyond tests: two Portal or Orchestrator processes on one host share that
  database in production too.

- **Four search boxes had no accessible name.** The admin user filter, the docs dictionary search,
  the governance asset filter, and the quarantine queue search all relied on a `placeholder`. A
  placeholder is not an accessible name — most screen readers announce the control as just "search
  box", and the hint disappears the moment the user starts typing, so the one clue about what the
  field searches is gone precisely when it is needed.

- **`studio.html` presented a dialog with no focus management at all.** Opening it left the keyboard
  user behind it, Tab walked straight out into the page the dialog was supposedly blocking, and
  closing it dropped focus back at the top of the document.

- **Three governance dialogs were dialogs only to sighted users** — no `role="dialog"`, no
  `aria-modal`, no accessible name, no focus trap. An overlay marked up as a plain `div` is announced
  as ordinary page content: the user is never told a dialog opened, and the content behind it stays
  reachable, so the "modal" blocks a mouse user and nobody else.

- Fixed the forced first-run password change signing the user into a dead session. Changing a password invalidates every session for the account, so the Portal sent the user into the app holding an already-invalidated token and silently bounced them back to the login page — the first thing a new deployment does looked like a failed sign-in. The new password is now exchanged for a fresh session before entering the app, and a failure to re-authenticate says the password *was* changed instead of reporting a password-change error.

- Fixed unhandled promise rejections from the report catalog's view transitions. Navigating faster than the animation skips the in-flight transition, and its rejected `ready`/`finished` promises were left unhandled on the page. A throw inside the update callback still surfaces.

- **The connection catalog showed the same message for a 403 and an unreachable service.** "Could
  not load connections" reads as a fault to report, when the answer may simply be that this account
  may not see the catalog. The two are now distinct, and the failure case offers a retry.

- **Escaping in the shared states happened two frames from the interpolation it protected.** Caller
  values were escaped by an inner helper, which works but is invisible at the call site and would
  double-escape anything a caller sensibly escaped itself. The rule is now simply: escape at the
  point of use. `PortalStateVocabularyTests` fails on any caller-supplied value interpolated raw —
  one unescaped interpolation would be an injection point on every surface that adopts the
  vocabulary, which is the cost of sharing it.

- **`QUALIFY` re-derived a constant lookup key on every row.** To let `QUALIFY rnk <= 1` reference a
  windowed column by its alias, the engine bridges each alias to the window-result column. That
  bridge walked the column's expression tree and serialized the window call back to SQL text, then
  upper-cased it, once per row per windowed column — arriving at the same constant string every
  time. It is now resolved once per statement.

  Measured on 50,000 rows over 500 partitions: the allocation `QUALIFY` adds on top of the same
  windowed query fell from 249 MB to 165 MB (−34%), and total statement allocation from 556 MB to
  472 MB. Wall time over its own baseline went from ~1.70x to ~1.16x, though timing on this bench
  carries roughly 15% run-to-run noise while the allocation counters are exact.

- **Plain `UNIQUE` built and spilled a full-row identity string it never read.** The pre-pass writes
  a per-row identity so `UNIQUE_FIRST`/`UNIQUE_LAST` can break ties on the order key. Plain `UNIQUE`
  fails every row of a duplicated group, so it never asks which row to keep — but the identity was
  computed regardless, and it is a rendering of the entire row (a fresh dictionary of the row's
  columns, sorted by name, concatenated), then written to spill and read back to be discarded.

  Measured on 50,000 rows: `UNIQUE` allocation fell from 636 MB to 483 MB (−24%). `UNIQUE_FIRST` is
  unchanged, correctly — it is the shape that needs the identity.

- **The pre-pass entry for a `UNIQUE` rule was found by a linear scan, per row.** The scan compared
  rules by record value, so two rules written identically cost a deep `Expression` comparison on
  every row. It is now a reference-keyed lookup.

- **Data-quality replay and disposition tracking had never worked.** The queue polled
  `GET /api/jobs/{id}` — the report-execution namespace, backed by `PortalExecutionJobs` — using a
  job id that came from `IJobChannel` and was never in that table. Every poll answered 404, the
  client treated the failure as transient, and it retried once a second for as long as the tab
  stayed open. No submission ever reached a terminal state on screen; the panel promising that jobs
  "remain here until their durable execution reaches a terminal state" showed "status temporarily
  unavailable" forever.

- **A submission's outcome was known only to the browser that made it.** Tracking lived in that
  browser's session storage, so closing the tab lost it. A second steward looking at the same
  quarantine target could not tell that a replay was already in flight — and the obvious next move
  is to submit another replay of the same production load.

- Corrected a stale doc comment on `MarkdownRenderer` that described the embedded chart comment as
  `<!-- CHART:{...} -->` carrying Chart.js config. The renderer emits `<!-- ECHART:{...} -->` containing
  ECharts option JSON, and has since the ECharts migration.

- **The VS Code designer sandbox fixture had never worked.** Its webview imported `renderDesigner`
  from the designer module, which exports `createDesigner`. The import threw and the fixture rendered
  nothing. Found by the new automation on its first run.

- **The security-event outbox was missing from the departmental isolation contract**, and its
  default is a **machine-wide** path under `LocalApplicationData` shared by every ETL-SQL process on
  the host. Two environments on one machine therefore wrote their security events into a single
  queue — a cross-environment leak of exactly the records isolation exists to keep apart, and the
  only resource in the contract whose default is *wrong* rather than merely unset.

  It is now:

  - a planned isolated resource in `GET /api/admin/environments/plan`, with the
    `ETLSQL_SECURITY_EVENT_OUTBOX_PATH` override named;
  - reported in the current-environment evidence, so an operator can see whether their own
    deployment has set it;
  - documented in `Departmental_Isolation.md` and `security-events.md`, including the co-located
    Portal/Orchestrator case;
  - pinned by a test, because a plan that lists databases and key rings while omitting this one
    reads as complete.

  Found empirically rather than by review: it was what made the browser test lane fail whenever two
  processes started back to back, one unable to open the file the other still held.

- **Studio was offered to every signed-in user, including roles holding no Studio capability.**
  Pages revealed the entry whenever the Studio capability *probe* succeeded — but that probe was
  deliberately opened to every authenticated user so that asking "what may I do in Studio?" would
  stop being an error for the roles that may do nothing. The probe answering is not the answer being
  yes. A Viewer, DataSteward or OrchestratorManager saw a Studio link that only leads to a 403.
  Found by a red test across all three roles before anything was changed.

- **The Docs link was offered on deployments where `/docs.html` returns 404.** Whether Documentation
  is enabled is a server fact with no token claim behind it, so no amount of care in the page could
  have got this right. Two pages did not even carry the `docsNav` hook the others gated on.

- **Governance nav gating in `docs.html` was one role wider than every other page.** It admitted a
  role named `Orchestrator`, which does not exist in the Portal's role set — a copy of the rule that
  had drifted and that nobody could see without diffing six pages against each other.

- **Accessibility-tree snapshot baselines were never committed.** A blanket `*.txt` in `.gitignore`
  swallowed `tests/ETL-SQL.Portal.BrowserTests/Snapshots/*.snapshot.txt`, so the baselines existed
  only on whichever machine last generated them. Updating one is supposed to be a review decision
  visible in a diff; it was invisible. That is how the governance sidebar baseline went stale and
  stayed stale.

- **Three browser assertions left behind by the governance sidebar rework.** The sidebar snapshot
  still described the old menu, `RoleJourneyTests` still required a `Stewardship` entry that was
  deliberately removed, and a dashboard test still clicked an in-page tab strip that no longer
  renders — so it had been timing out rather than testing anything. All three now match the shipped
  design.

- **`--silent` printed "Linting failed:" and discarded every reason it failed.** `ILogger.WriteLine`
  derives its log level from the console colour — red is an error, yellow a warning — and silent
  mode keeps only errors. The lint reporter wrote its header red and each diagnostic yellow, so a
  silent run produced a non-zero exit code with no explanation. The colour had quietly become a
  severity decision.

  The lines explaining a fatal error are now emitted at error level. This also repairs the sample
  gate's `@expected-error` check, which reads that output and therefore could not verify a lint
  failure at all — the mechanism existed and silently did not work for the largest class of sample
  failure.

- **`eng.variables` emitted each variable's raw CLR value**, so the view's `value` column held a
  number in one row and a string in the next, and any columnar materialization of it —
  `SELECT … INTO`, or a spill — failed on the first value that did not fit the type inferred from an
  earlier row. The column is documented as text and already carried `*******` for a masked value, so
  it is now rendered consistently with invariant formatting; `data_type` still reports the original
  type. Fixes the `diagnostics_ssh_sink` sample.

- **A `TIME` column aborted a spilling query.** `ColumnBatchAdapter.GetPhysicalType` maps `TIME` to
  `TimeSpan`, but the columnar spill writer had no case for it and failed the whole write with
  `Native spill writing does not support 'TimeSpan' columns` — the same shape as the UUID gap fixed
  earlier. Spans are stored round-trip as text rather than an Arrow time type, because a `TimeSpan`
  may be negative or exceed 24 hours and Arrow's time types model a time of day. The row-based path
  also labelled spans `Json`, so they came back as bare strings; they now carry `TIME` and restore
  as spans. Fixes the `golden_workflow.rptsql` sample.

- **The data-quality trend showed which rules fired but not where.** The target table, the action
  the rule took and the rule's owner were collected by the engine, persisted, queried, grouped and
  serialised — and then dropped by the browser, which rendered only column, rule and count.

  The visible cost was worse than missing detail: two columns with the same name in different
  target tables (`Email` in `warehouse.Customers` and in `warehouse.Leads`) rendered as two
  identical-looking rows with different numbers, and nothing on screen said which was which.

- **Legacy runs no longer pass as fully recorded.** History written before per-rule capture has only
  the compact `column:rule=count` string, which cannot express those three fields. Those rows are
  now marked `countsOnly`: they are never merged with structured rows — summing them would
  attribute a legacy run's failures to a target table that run never named — and they render as
  *unavailable* rather than blank, because an empty Owner cell reads as "nobody owns this rule",
  which is a different and more alarming claim than "this run did not record it".

- **The Studio capability probe still answered 403 to most roles**, despite an earlier attempt to
  open it. `GET /api/studio/session` exists to answer "what may this user do in Studio?", and the
  Portal shell calls it on every page load — but an action-level `[Authorize]` does **not** override
  a class-level `[Authorize(Roles = …)]` in ASP.NET Core; both apply. Only `[AllowAnonymous]` takes
  an action out of that policy, so the endpoint now uses it and restates the authentication
  requirement explicitly.

  With that fixed, every non-admin role loads the report library with **no failed requests at all**.

- **`PARTITION BY` returned bucket-wide window values once a partition spilled.** The external
  window engine hash-partitions rows into buckets, and its partition-replay path scanned a whole
  bucket once and wrote that single aggregate onto every row in it. That is sound only when a bucket
  *is* the logical partition. Buckets are hash partitions, so with a `PARTITION BY` of higher
  cardinality than the bucket count — the ordinary case — one bucket holds many partitions and every
  row received the bucket's aggregate instead of its own.

  `SELECT COUNT(*) OVER (PARTITION BY customer_id)` over a large table therefore returned silently
  wrong numbers: no error, no warning. Reached whenever a bucket exceeded `WindowSpillThreshold`
  (default 10,000 rows) with `COUNT`/`SUM`/`MIN`/`MAX`/`AVG` and no `ORDER BY` or frame.

  Both scan passes now fold one accumulator set per partition key, and the replay pass looks up each
  row's own key. The columnar fast path builds the key from batch ordinals and declines — falling
  back to the row scan — when a partition expression is not a plain column it carries, so the
  optimization is kept rather than traded away for correctness.

  Keys are rendered to text through one shared helper, because the scan may read a value from a
  column batch while the replay reads it from a materialized row: the same column can come back as
  `long` one way and `int` the other, and boxed equality would then file one partition under two
  keys — reintroducing the defect in a subtler form.

- Stabilized two full-solution load-sensitive tests. Metadata refresh coverage now waits by repeatedly
  exercising the public stale-cache path until the previous refresh slot is reusable, and live-object
  scale coverage uses a minimal private connector/evaluator instead of process-wide application state
  and asynchronously seeded mock databases.

- Regenerated the CLI reference for the shipped Gateway daemon and protected resource-administration
  commands, and restored the active release's explicit Enterprise evidence-checklist contract.

- Retired completed roadmap entries for deployment profiles, Orchestrator administration, the
  Secure Outbound Gateway, hardened SaaS execution, compound quality rules, native script testing,
  and declarative watermarks. Removed shipped sub-phases from the still-open tenant-portability and
  report-builder fidelity tracks so `ROADMAP.md` now describes only future work.

- Audited and retired verified completion records from `TODO.md`, and reopened two claims whose
  implementation evidence was incomplete: production end-to-end Gateway operation and Shared-fleet
  drain/replacement. Removed the already-delivered Control Plane Dashboard and API load/soak phases
  from `ROADMAP.md`.

- Made Portal Gateway enrollments durable on SQLite and PostgreSQL, including one-time token hashes,
  optimistic-concurrency consumption, tenant partitioning, schema migrations, and backup-surface
  classification. Added the token-authenticated bootstrap endpoint and the Portal WebSocket broker
  route.

- Fixed a Gateway broker race that acknowledged a session before registering it, allowing a client
  to receive `HelloAck` while the routing registry still reported the Gateway offline.

- Added restart-durable file persistence to the Gateway outcome ledger. Committed outcomes and
  ambiguous writes retain their reconnect decisions across daemon recreation.

- Completed the executable Secure Outbound Gateway path: one-time Portal bootstrap, machine-protected
  ECDSA workload identity with signed-challenge proof, foreground daemon and OS service packaging,
  protected local resource administration, bounded connector execution, and authority-evaluated
  `SHARED:` alias routing over the typed broker data plane.

- Added a Shared SaaS hostile-isolation certification lane spanning shared state, artifacts, cache,
  queue, audit, PII/stewardship, lineage, paths, keys, checkpoints, Gateway, sandbox, quotas,
  telemetry, support, restore, identity, and resource exhaustion. Added fleet drain placement that
  preserves in-flight tenant work, shifts new work to ready nodes, and cannot lower isolation tiers.

- Made sandbox fair-share admission cluster-global. Weighted fair ordering was process-local, so on a
  multi-node deployment whichever node polled first took every freed slot; selection now happens in
  the durable ledger as weighted fair queuing on virtual time, and a heavier tenant weight buys a
  proportional share rather than the ability to starve a lighter one.

- Enforced the sandbox admission and runtime limits that were declared but not applied: CPU cores,
  block-I/O (on hosts that declare a throttle device, refusing the work where they cannot), connector
  concurrency, per-attempt processed rows, per-tenant interactive sessions in Shared deployments, and
  a queue-depth ceiling that now holds across the whole fleet rather than per process.

- Added capability delivery to sandboxed workloads. Server-issued handles resolve through the
  governance secret provider, namespaced per tenant, and are bind-mounted read-only into the
  attempt; a host that cannot resolve a granted capability refuses the work instead of running
  without it.

- Added fleet release rollout for Managed Dedicated deployments: eligibility and compatibility
  planning, deterministic waves, and a sequencer that will not open the next wave while an earlier
  one is draining or after failures exceed tolerance. Each cutover still requires its own signed,
  tenant-scoped authorization.

- Added graceful execution-node drain. A node can now leave rotation without dropping work —
  in-flight reports finish, new ones are refused, and `/healthz` reports draining — where previously
  the only way to stop a node cancelled everything it was running.

- Fixed sandbox checkpoints being unreadable to any later attempt. Session state was sealed with key
  material generated inside the attempt's own single-use scratch, so a resumed run reported "no saved
  session found"; the server-mounted per-tenant key is now authoritative, and checkpoint resume across
  separate sandboxes is verified on a hardened runtime.

- Added the on-premises Gateway runtime and its typed WebSocket transport in a new
  `ETL-SQL.Gateway` project. The Gateway dials out and never listens, refuses any scheme but the
  typed protocol and any non-TLS broker off loopback, bounds inbound frames, narrows cloud-supplied
  limits by the resource's registered ones, and returns a fixed message when a local provider fails
  so a host, user, or password in the provider's exception never crosses the wire. The frame model
  has no field for a host, port, scheme, path, or command, so a compromised cloud side cannot ask the
  Gateway to reach an arbitrary destination.

- Added the Gateway typed-operation contract and durable outcome ledger. Operation bounds have no
  unlimited representation and a resource's registered limits can only narrow them. Reconnect follows
  one rule: an ambiguous write is never retried blindly nor reported as safely failed, a dropped
  in-flight write is ambiguous rather than assumed not to have happened, a dropped read may simply
  re-run, and a committed outcome cannot be downgraded by a late report.

- Added the Secure Outbound Data Gateway enrollment, resource registry, and authority model. A
  tenant-issued enrollment is consumable exactly once and stores only a hash of its one-time token,
  so the record cannot enrol a Gateway; expired, revoked, consumed, and cross-tenant presentations
  are indistinguishable to a caller. The Gateway-local registry holds the only copy of a resource's
  target and credential reference, discovery can propose but never approve, and the published
  projection carries neither. Routing is authorized in one place and only when execution tenant,
  capability tenant, Gateway identity tenant, catalog binding, resource ownership, actor grant, and
  policy version all agree; revoking a Gateway or disabling a resource denies on the next evaluation
  with no grace window.

- Added the Secure Outbound Data Gateway binding model. A `SHARED:` catalog alias can now resolve to
  a Gateway binding — connector type plus immutable Gateway and resource IDs — instead of a direct
  target. A Gateway-bound entry cannot store a physical endpoint or a credential; those stay on the
  on-premises Gateway, and the catalog store refuses an entry that carries either. Resolving a
  Gateway-bound alias fails closed while no Gateway data plane runs, rather than falling back to a
  direct connection, and a script can neither add a binding to a direct alias nor bypass one.

- Added a non-bypassable infrastructure egress fence. Connectors can no longer reach cloud instance
  metadata endpoints, link-local node services, the container runtime host bridge, or cluster service
  discovery in any deployment topology — the default `AllowedHosts: ["*"]`, an unenrolled host, and a
  mid-run policy change cannot relax it. The fence is applied at connection creation, on every dynamic
  REST URL including redirects, and again per resolved address at socket-connect time, so obfuscated
  address forms, DNS rebinding, and port scanning are all covered. Loopback and RFC 1918 private
  ranges are unchanged. Operators can exempt exact hosts/addresses through
  `Security:EgressFenceExemptions` or authoritative policy; wildcard exemptions are rejected.

- Added `Security:DeniedEgressRanges` so an operator can declare this deployment's own off-limits CIDR
  ranges — hosting control plane, internal management networks, other tenants' subnets. Ranges are
  enforced at connection creation and per resolved address, across IPv4 and IPv6 at any prefix length,
  and cannot be exempted. Malformed ranges fail policy validation instead of being dropped silently.

- Added signed Managed Dedicated tenant upgrades with running-release verification, exclusive
  boundary locking, scheduler fencing, durable admission drain/reconciliation, atomic capacity
  assignment, exact rollback snapshots, interrupted-cutover recovery, and idempotent audit receipts.

- Certified Managed Dedicated data-asset isolation across physically disjoint lineage, quality,
  scan, cache, outbox, quarantine, report, dataset, snapshot, subscription, share/embed, and export
  stores, including equal numeric-ID collision tests and tenant-admin/author boundary evidence.

- Added signed, retention/legal-hold-aware Managed Dedicated tenant deletion with explicit execution,
  reparse/root safeguards, non-payload boundary digesting, atomic service removal, and an external
  durable Started/Completed receipt attributed to the platform operator and approval.

- Added Managed Dedicated split-custody recovery with tenant-bound archive pairs, foreign-tenant row
  refusal, explicit recovery-environment tenant matching, actual provisioned key/artifact paths, and
  post-restore job/admission fencing so recovered work cannot silently resume.

- Added a counts-only tenant usage ledger at the Orchestrator scheduler boundary. Tenant-bound job
  attempts persist idempotent row, memory, CPU, and duration measures from the immutable job tenant
  binding without payload content, and metering failure cannot authorize, retry, or alter execution.

- Added a durable Shared tenant resource registry for alias, gateway, resource, run, object,
  storage, queue, and index namespaces, with server-derived tenant scope and composite isolation on
  both SQLite and PostgreSQL.

- Added Managed Dedicated platform-support approval: a human tenant Admin can grant one named
  operator short-lived, purpose-bound access to an exact reviewed support disclosure without
  creating a tenant session or platform superuser; approvals, refusals, and downloads are audited.

- Fixed concurrent Orchestrator startup against an existing SQLite database so additive schema migrations treat a column added by another process as success instead of stopping the scheduler.

- PostgreSQL Portal migrations now add the report and draft edit-session lease columns used by
  collaborative Studio sessions. Multi-process PostgreSQL startup no longer fails after the SQLite
  lease migration advances the shared model, and a provider-specific migration contract prevents
  future SQLite-only lease changes.

- Optimized `MERGE` now falls back to authoritative SQL equality when its type-strict hash key does
  not find a candidate, preventing compatible cross-representation keys such as integer `2` and CSV
  string `"2"` from being treated as unmatched and inserted as duplicates.

- CI now validates the complete sample library on both Windows and Linux, using each platform's
  native validator for two passes so persistent sample side effects and cross-platform drift are
  caught on ordinary pushes and pull requests rather than only during the Windows pre-release gate.

- **Typed spill persistence now identifies heterogeneous-column failures at the boundary that
  rejects them.** Arrow conversion errors report the chunk, column, one-based row, inferred sink
  type, and actual CLR type without echoing the value; failed buffered flushes still close all
  writer streams.

- **The low-threshold spill lane now completes under bounded memory in deterministic fresh-host
  shards.** It records exact method manifests and per-shard TRX evidence, detects cross-shard test
  identity overlap, and distinguishes execution-time theory expansion from discovery counts. The
  certified run passed all 6,058 engine results plus all 7 SQL logic wrappers without a host crash.

- **`BULK INSERT` no longer silently transposes recognizable header-bearing input.** Complete
  headers map requested targets by name, `MAPPING = 'POSITION'` explicitly selects ordinal mapping,
  forgotten headers fail before writing, and fallback/width ambiguity is counted in the completion
  diagnostic. The file-operations reference now documents the actual parser/runtime contract.

- **Row-invariant data-quality `BETWEEN` bounds are evaluated once per statement.** A conservative
  deterministic-expression classifier keeps row-dependent, volatile, subquery, and unknown-call
  bounds on the per-row path; allocation coverage pins invariant validation near the rule-free
  baseline.

- Portal tenant-boundary startup and tests now agree on server-derived tenant identity: invalid
  tenant credentials receive the structured JWT challenge, first-run administrators inherit the
  configured host tenant, and dataset-root diagnostics name the current configuration key. The
  N-to-N+1 upgrade drill seeds historical schemas directly so it tests migration rather than the
  current model's write requirements.

- Invalid session-save callers now fail explicitly instead of silently discarding state, and the
  low-threshold spill lane uses an isolated session root so checkpoint attempts cannot leak into a
  developer profile or sandbox-denied LocalAppData path.

- **Release coverage and test-lane ownership are now enforceable locally.** CI and both pre-release
  drivers use one fail-closed 70% line-coverage gate instead of duplicating YAML logic or allowing an
  unparseable report to skip enforcement. Engine routing now depends only on explicit categories;
  scale, billion-row, and deployment certification stay in focused release runners. A structural
  audit rejects lane gaps, milestone-era test names, and feature tests stranded at the project root,
  and the existing suite has been reorganized under durable product areas.

- **Sample certification now distinguishes intended failures from regressions.** Samples that
  demonstrate a fail-closed guardrail can declare both an expected exit code and an exact error
  fragment; the Windows and POSIX runners require both to match. Validator processes also use
  isolated session and security-event stores instead of mutating or contending with the user's
  machine state. Native Arrow spill now round-trips `GUID`/`UUID` columns, and the Docker alias
  lifecycle sample uses the matching pause/resume operations.

- **The sample-scripts release gate could never fail.** `Test-AllSamples.ps1` printed per-script
  failures and a red summary, then exited 0; the gate judges a phase by its exit code, so the phase
  reported Passed regardless. The POSIX twin always exited non-zero — only the Windows script the
  gate actually runs was missing it. Both now fail properly, and both take a pass count: sample
  output is gitignored, so a sample that writes to a persistent store passes on a clean checkout and
  fails for anyone who runs it twice, which a single pass structurally cannot detect. The
  pre-release gate runs two passes.

- **A tag comment before a column alias was a syntax error.** `amount /* @d: … */ AS total` — the
  form the lineage reference documents, and the natural reading order — failed to parse for every
  expression shape; only the trailing placement worked. Tags now attach from either side of the
  alias and merge when present on both.

- **`eng.lineage` dropped the transformation on every renamed column.** One hop is observed twice —
  by static analysis at parse time and by the engine as it executes — and each observation knows
  something the other does not: only the engine has resolved connections, so the physical source is
  on its entry, while the classification of the expression is on the analyzer's. Collapsing the two
  for display picked one and discarded the other, so any column whose name differs from its source
  lost its transformation — exactly the columns most likely to have one. The observations are now
  merged rather than chosen between, into a copy, so rendering a chain cannot rewrite the session's
  recorded lineage. Re-recording a hop also now fills in the physical identifier the earlier
  pre-connection observation lacked.

- **`first + ' ' + last` was classified as arithmetic, not string concatenation.** `+` is
  overloaded and the classifier has no types, so a string literal in the chain is the only evidence
  available — but it was looked for only in the immediate operands, and a left-associative parse
  puts it out of reach in any concatenation of more than two parts.

- **OpenLineage documents read back with a different `transformation_kind` than they were written
  with.** ETL-SQL's twelve kinds map many-to-one onto OpenLineage's subtype vocabulary
  (`StringOperation` and `FunctionCall` both become `FUNCTION`), so import could not recover the
  original. Exports now also carry the exact kind alongside the standard subtype, which standard
  consumers ignore and our own import prefers.

- **SQLite connection declaration and pushdown parameter binding.** Declaring a connection probes it
  for schema, and the columns lookup threw when no table was bound — so `CREATE CONNECTION … AS
  SQLITE(…)` failed outright against a database with no tables, including every new one. Separately,
  the query compiler emits `@p0` while the connector registered `$p0`; Microsoft.Data.Sqlite binds
  by name, so any pushdown carrying a literal was rejected for a missing value. Both date to the
  connector's introduction, and made the shipped SQLite sample unrunnable in v0.14.0 through
  v0.17.0.

- Moved smoke and optional Standard scale certification ahead of the long pre-release test lanes so
  baseline comparisons measure warmed binaries before sustained lane and container activity heats
  or pressures the machine; `Test-PreRelease.ps1 -Explain` reports the same execution order.

- Added a same-worktree commit comparison harness for scale investigations. It measures both refs in
  one directory with interleaved arms, identical copied runner logic, rebuilds and discarded warm-ups
  per sample, restores the original checkout, and reports within-arm spread beside the median delta.

- Removed TUI completion and lint latency that could leave suggestions or diagnostics nearly a
  minute behind typing, and stopped showing the opt-in `SELECT *` warning when it is disabled.

- `docs/reference/visuals-reporting/report/theme.md`, which is embedded as runtime `HELP`,
  documented `DROP THEME corporate IF EXISTS` — a form the parser has never accepted for `THEME`.

### Security

- Report alerts are now re-authorized against their owner before every evaluation, the same way subscription delivery already was. An alert kept firing after its author was deactivated or lost their last grant on the report, and an alert notification carries the value that crossed the threshold — so a departed author's alert went on pushing report data into the channel they had chosen, and disabling the account did not stop it. Unauthorized alerts are now skipped whole rather than evaluated with the dispatch suppressed, so a `TRIGGERED` transition is never recorded against a notification nobody received.

  Found by auditing revocation across connections, subscriptions, alerts, and saved views. The other three were already correct: subscriptions re-authorize at delivery, saved-view routes resolve report permission before narrowing to the caller's own rows, and shared connections have no authorship path at all.

- Dataset access no longer treats authorship as standing permission. `DatasetPermissionService` and `ReportDependencyService` short-circuited on `CreatedBy == userId`, so removing a user from every group — or from the directory — left every dataset they had ever created fully open to them, with no revocation gesture that could undo it. Permission resolution now reads grants only: a dataset's creator, and the author of the report that owns it, receive an explicit `Owner` grant in the new `DatasetUserAcls` table when the dataset is registered, so access can be revoked by deleting a row. Deleting a user cascades their grants away, and transferring ownership before deleting a user now moves the grant along with `CreatedBy`. The migration backfills a grant for every dataset that already has a creator, so nobody loses access on upgrade.

  Per-user dataset grants are a sibling table rather than a nullable `UserId` on `DatasetAcl`, because relaxing that column is an `AlterColumn` — rejected by the rolling-expand migration contract and implemented by SQLite as a table rebuild. The rule and its per-resource mechanisms are recorded in `docs/architecture/decisions/AuthorshipIsNotPermission.md`.

  One behavior change worth knowing: deleting the report that owns a private dataset used to revoke the report author's access, because that access was derived from `OwningReport.CreatedBy`. It no longer does — their grant is durable and explicit. The orphaned dataset therefore stays reachable by its author rather than becoming administrator-only, and the grant can be revoked.

- **An `Author` folder grant conferred `Manage` authority.** `FolderPermissionService.HasPermissionAsync`
  compared permissions with `>=`, which reads the enum's *storage* value. `Author` is stored as `3`
  and `Manage` as `2` — Author was appended rather than inserted so that adding it would not
  renumber every ACL row already in force — while its authority ranks *below* Manage. So
  `Author >= Manage` was true, and every folder-level check gated on `Manage` admitted an Author.

  Demonstrated before it was fixed: a Publisher-role user holding only `Author` on a folder could
  `POST /api/studio/reports` into it and receive `201 Created`. Publishing a new report into the
  folder is one of the acts the Author grant is explicitly defined not to permit. The same check
  gates dataset moves between folders and several folder routes.

  Fixed by using `AtLeast()`, which ranks rather than compares.

- **The guard that was supposed to prevent this could not see it.**
  `NoProductionCode_ComparesPermissionsOrdinallyAgainstAnythingAboveRead` matches a *literal*
  `FolderPermission.Execute|Manage|Author` on the line. The offending comparison was
  `effective.Value >= required` — two variables, no literal — so it read as clean. A second check
  now covers variable-to-variable comparisons in any file that deals in folder permissions, and it
  names the exact line when it fires.

  `DatasetPermission` was checked and is unaffected: its storage order is its authority order, so
  `>=` is correct there.

- **Cleared four npm advisories across the VS Code extension and its UI package**, which had been
  failing the CI audit gate. All were transitive **dev** dependencies — the toolchain, not anything
  the extension ships:

  - `brace-expansion` (high, DoS) — reached through `eslint`→`minimatch` and `mocha`→`minimatch` in
    the extension, and `eslint`→`minimatch` in the UI package.
  - `undici` (high, five advisories including response desynchronisation and cross-user information
    disclosure) — reached through `jsdom`.
  - `postcss` (moderate, arbitrary `.map` read via attacker-controlled `sourceMappingURL`) — reached
    through `vite`.

  Fixed in the `overrides` blocks, not just the lockfiles. **The stale overrides were the actual
  defect**: their floors were set to whatever was current when they were written, and that version
  is the one that later became vulnerable — `undici: ">=7.28.0"` admits exactly 7.28.0, and
  `brace-expansion: ">=5.0.6"` admits 5.0.6 through 5.0.8. A lockfile-only fix would have re-broken
  on the next resolve. Floors are now `>=8.10.0` and `>=5.0.9`.

  No direct dependency was added or changed — only override floors and the resulting lockfile
  entries — so the third-party inventory is unaffected.

  The UI package's two advisories had never been reported by CI, because the extension audit runs
  first and failed the job before that step was reached.

  Verified rather than assumed after the bumps — `undici` moved a major version under `jsdom`:
  extension compile, lint and its 6 integration tests; UI lint, build and its 13 unit tests.

- Orchestrator management now requires a short-lived Portal-signed caller assertion in addition to
  the service API key on network-reachable listeners. Durable owner and user/group/service ACLs
  protect `JOB`, `SCHEDULE`, and `NOTIFICATION` reads and mutations, including catalog statements
  submitted through the ad-hoc script API. `READ`, `EXECUTE`, variable `OVERRIDE`, and `MANAGE`
  remain separate capabilities; history, quality, and triage evidence is filtered by job read
  authority, and mutation security events use the verified human or service principal.

- Portal SMTP credentials move from an encrypted value in a bespoke table to a `SECRET:` reference
  in the governed connection catalog, which enforces reference-only credentials on write, encrypts
  target and options at rest, and carries per-connection use ACLs, ownership, an audit trail and a
  usage ledger. The Portal no longer materializes the plaintext: subscription and admin-notification
  delivery scripts carry the reference and the engine resolves it when the connection opens, so the
  credential is never written into script text that could reach logs, execution history, or error
  detail. This is a hardening of an exposure that previously required active scrubbing — no exploit
  path was identified.

- **Known gap:** because the Portal no longer holds the plaintext, it can no longer redact a
  credential that an engine error echoes back; pattern-based redaction does not match a bare
  password in free text. Net exposure is lower than before, but this specific mitigation is absent.
  Tracked for a fix at the point where `SECRET:` references are resolved.

- Expanded the security-boundary documentation and pinned its required contracts with
  `SecurityBoundaryDocTests`.

### Known Issues

- `npm audit` fails the VS Code extension job on two high-severity advisories in transitive
  dependencies (`brace-expansion`, `undici`). Pre-existing dependency drift rather than a code
  change; Dependabot has branches open. Recorded here so the red build is not mistaken for a
  regression from this release's work.

- If the MSI job becomes a **required status check**, it needs a companion always-succeeds job. A
  path-filtered workflow reports *skipped* rather than *success*, and a required check that never
  reports will block every unrelated pull request. Recorded in `TODO.md` beside the setting itself.

## [0.17.0] — 2026-07-26

### Added

- Added `ASSERT JOB <name> (<predicates>) [ON FAILURE ALERT <connection>] [ON CRITICAL_FAILURE THROW]`,
  asserting on the run's own metrics rather than a query result: `ROW_COUNT`, `NULL_PERCENT(<col>)`,
  qualified `NULL_PERCENT(<target>.<col>)`, `FRESHNESS(<col>)`, `QUARANTINE_PERCENT`, and
  `WARN_PERCENT`, each comparable against a literal/interval or against a historical baseline with
  `WITHIN <fraction> OF HISTORICAL`; supported historical metrics also accept
  `WITHIN <n> SIGMA OF HISTORICAL`. Metrics are collected in-stream during the run (never a post-run
  re-scan), so write-only sinks are supported. Historical baselines use the mean of recent completed
  runs and skip themselves below a configurable minimum (`Engine:DataQuality:MinHistoryRuns`,
  default 3; sigma default 10) so new jobs do not alert-storm. Per-column null metrics are persisted
  to job history for target-aware `NULL_PERCENT ... OF HISTORICAL`. Failures can post a counts-only
  summary through a webhook connection — sample data is never included — and optionally fail the run.
  Orchestrator-hosted alerts are transition-based: pass→fail alerts, repeated fail→fail runs are
  suppressed until `Engine:DataQuality:AlertRealertHours` elapses (default 24), and fail→pass sends
  a recovery notification.

- Added column-level data-quality rules: `@expect` / `@fail` tags declared inline on SELECT columns,
  routed by a trailing `ON FAILURE <ACTION> [TO <table>] [WITH (RETENTION = '…')]` clause. Rules
  cover `NOT NULL`, `UNIQUE` (plus `UNIQUE WITH (cols)` and `UNIQUE_FIRST/LAST BY <expr>`),
  `MATCHES <regex>`, `IN (<list>)`, `EXISTS IN table(col)`, `EXPR <predicate>`, and numeric
  comparisons; actions are `THROW`, `WARN` (aggregated diagnostics, optional row capture), and
  `QUARANTINE` (row diverted to a capture table with the `__dq_*` provenance columns). Failing rows
  are captured pre-projection so stewards see the cause, `@pii` values are masked in diagnostics and
  logs, and per-run quarantined/warned counts are persisted to job history and surfaced on the
  execution result. Rules are validated at lint time (malformed rules, non-sink QUARANTINE,
  orphaned clauses, missing section labels) and appear in editor completions.

- Added the first quarantine-remediation v2 foundation: orchestrator-hosted jobs now persist a
  replay manifest when rows are quarantined, recording the job, script path, section label, source
  table, quarantine target, replayability flag, non-replayable reason, and captured input schema
  fingerprint. Single-table labeled quarantines are marked replayable; join-source quarantines are
  captured normally but marked non-replayable until the v3 provenance design lands.

- Added data-quality quarantine disposition enforcement for `UPDATE`: `__dq_*` evidence columns are
  immutable except `__dq_status`, warn rows cannot be released, and quarantine statuses follow the
  v2 lifecycle (`quarantined` may become `released` or `discarded`; `released` may become
  `replayed` or `discarded`).

- Added `REPLAY QUARANTINE <table>` replay support for v2 single-table quarantines. The statement
  resolves the orchestrator replay manifest, rejects missing or non-replayable quarantine targets
  with clear errors, builds a source stream from rows marked `released` with `__dq_*` evidence
  columns stripped, and resumes the recorded section label with that stream substituted for the
  original source. After a successful replay, consumed rows move from `released` to `replayed`;
  replay is fenced by the orchestrator cluster-lock store so concurrent stewards cannot consume the
  same released row set twice.

- Added the first Portal data-quality quarantine queue surface. `/api/data-quality/quarantine`
  exposes orchestrator replay manifests with replayability filters, and the Governance sidebar now
  includes a Quarantine Queue view with target/search filters and copyable `REPLAY QUARANTINE`
  statements for steward workflows.

- Added Portal quarantine replay submission. The quarantine queue can now submit replayable manifests
  through the configured Orchestrator job channel, rejects blocked or tampered manifest targets, and
  reports the submitted replay job id back to the steward.

- Added Portal quarantine disposition submission. `/api/data-quality/quarantine/disposition`
  accepts explicit row ids plus optional source-column edits, builds a guarded `UPDATE` that leaves
  `__dq_*` evidence immutable, and submits it through the Orchestrator job channel for release or
  discard workflows.

- Added the Portal quarantine row editor. `/api/data-quality/quarantine/rows` previews capped
  quarantine rows for Portal-resolvable targets, the Quarantine Queue can open an inline row grid,
  and stewards can edit source columns then submit release or discard actions without touching
  immutable `__dq_*` evidence. Targets whose producing connection or session-local table is not
  available inside Portal are labeled view-only with the reason and copyable review SQL instead of
  opening a row editor that would fail or misleadingly return an empty temp table.

- Hardened the data-quality `UNIQUE` pre-pass for larger inputs: projected key records now spill
  into hash partitions and reduce partition-by-partition instead of keeping the full key map in
  memory. Duplicate lookup is keyed by rule occurrence, so identical `UNIQUE` rule text on different
  columns no longer collides.

- Added an opt-in data-source capability for connector-side data-quality retention pruning.
  `WITH (RETENTION = '...')` capture targets now use the connector capability when available, with
  SQLite-backed quarantine/warn tables deleting rows older than `__dq_ts` through a bounded
  connector-side `DELETE`.

- Added a write-only `WEBHOOK` connector (aliases `SLACK`, `TEAMS`) that POSTs each inserted row as a
  JSON payload — Slack/Teams message shaping via `FORMAT`, custom bodies via `BODY_TEMPLATE`, and
  opt-in retry policy. The endpoint URL is treated as a credential: `SECRET:` references resolve on
  `URL` for webhook connections, and the URL is masked to scheme + host in `SHOW CONNECTION`, logs,
  and error messages. Every request and redirect hop passes egress-policy validation; only 307/308
  redirects are followed so a delivery is never silently downgraded to a body-less GET.

- Added a design-time script DAG/Flow preview for `.etlsql` and `.rptsql` authoring surfaces, derived
  from parsed script text and wired into existing shared DAG rendering paths.
- Added report-designer ergonomics for keyboard deletion, save shortcuts, escape-to-clear,
  grid nudging, undo/redo, duplication, multi-select movement, container detachment, container
  collapse, tab/accordion child assignment, dynamic column mapping suggestions, and dataset-column
  drag-and-drop mapping.
- Added a business-consumer Portal home experience with favorites, recently viewed reports, featured
  reports, popularity sections, and permission-aware catalog discovery.
- Added fuzzy and synonym-aware Portal catalog search with match reasons across titles,
  descriptions, tags, folders, and report metadata.
- Added self-service report access requests, report-owner/admin approval and denial endpoints, and
  report-level ACLs so approvals can grant one report without broadening folder access.
- Added published-report metadata headers with owner/contact, freshness, last-refresh state, and
  interactive tag badges that navigate or post catalog-search intents.
- Added stale-report refresh requests: users with `Execute` can start a refresh, while read-only
  consumers create an audited owner request without bypassing permissions.
- Added one-click "My Default View" saving for current report parameter/slicer state, updating a
  single per-user default saved view.
- Added `DATE_SUFFIX` and `SUFFIX_SEPARATOR` file-operation options for common dated archive names
  on copy/move flows.
- Extended `SHOW SCHEMA`/`DESCRIBE` lookup so file-based connections can expose schema metadata to
  authors and agents.
- Added `SHOW PROTECTED DATA [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` to inventory protected lineage tagged as PII, PHI, PCI, sensitive, confidential, or restricted from local, Portal, or Orchestrator catalogs.
- Added `SHOW PROTECTED DATA SUGGESTIONS [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` for reviewable classifier findings from column names, source-column names, catalog metadata hints, and supported sampled values without automatically changing tags.
- Added `SHOW PORTAL AUDIT [ACTION '...'] [LIMIT n] [INTO #temp]` for script-first Portal audit review, including steward-impact lineage events.
- Added `samples/08_Reporting/protected_data_audit.rptsql` as a starter protected-data stewardship dashboard.
- Added Portal Lineage Audit mode for a steward-focused workflow that combines protected inventory, classifier suggestions, metadata queues, stale protected assets, inferred impact, steward-impact audit rows, and audit outbox health.
- Added tag-driven governance policy lint and Portal runtime gates for public dataset stewardship metadata, restricted/confidential public datasets, protected dataset exports, and `@quality=gold` promotion metadata.

**Stewardship catalog impact analysis**
- Added `/api/catalog/impact` for upstream, downstream, and bidirectional impact analysis by table,
  column, job, script, dataset, report, subscription, owner, and steward.
- Added Portal Lineage Impact mode and pre-publish report validation impact summaries so publishers
  can review affected reports, datasets, subscriptions, jobs, owners, and stewards before changes.
- Added auditable `STEWARD_LINEAGE_IMPACT` hooks for report execution and persisted ad hoc
  interaction lineage changes that affect steward-owned assets.
- Added [Data Stewardship and Impact Analysis](docs/guides/feature-guides/data-stewardship-impact.md) as the
  operator and publisher usage guide.

**Data prep helpers**
- Added `GENERATE CALENDAR FROM <start> TO <end> INTO #temp`, materializing a full date dimension
  (`DateKey`, ISO week, fiscal year/quarter, month/day names, and boundary flags such as
  `IsMonthEnd` / `IsQuarterStart`).
- Added `FILL_DATES(#source, DATE_COL = …, GAPS_FILL = …, BY_GROUP = …) INTO #temp` to fill missing
  daily rows per group, copying existing rows unchanged.
- Added `COMPARE DATASETS #source WITH #baseline KEY (…) [EXCLUDE (…)] INTO #diff`, writing only
  inserted/updated/deleted rows with `_change_type`, `_changed_columns`, and `<column>_old` /
  `<column>_new` pairs.
- Added 14 productivity functions: `SAME_PERIOD_LAST_YEAR`, `START_OF_MONTH`, `END_OF_MONTH`,
  `START_OF_QUARTER`, `END_OF_QUARTER`, `START_OF_WEEK`, `END_OF_WEEK`, `SAFE_DIVIDE`, `AGE_BUCKET`,
  `VALUE_BUCKET`, `CLEAN_STRING`, `MASK_EMAIL`, `MASK_PHONE`, and `MASK_SSN`. The `MASK_*` functions
  are presentation masking for reports and diagnostics, not a security control.

**Authoring & CLI**
- Added string variable interpolation inside string literals — `${@var}` and `${var}` — resolved
  across statement options, file paths, dynamic connection settings, and expressions. An undeclared
  name is left intact as literal text so shell and regex strings are not corrupted.
- Added `etl-sql edit`, which opens the browser-based script editor, and a unified script editor
  workbench shared by the Workstation and Portal hosts.
- Added `SHOW SCHEMA` as a statement, plus `--mock` mode and `--json` output options for
  scripting and agent use.
- Added `SET DATA_QUALITY_DRY_RUN` so a rule set's impact can be previewed without quarantining,
  warning, or failing a run.

**Workstation editor**
- Added a Git status surface with a header branch badge, a formatter settings panel persisted to
  `.etlsql-formatter.json`, and local run history.
- Added a memory ceiling and a destructive-statement guard for local runs, plus cancellable runs
  with visible elapsed time and a graceful exit path.
- Added column lineage and report preview, and compact colour-coded hover help.

**MOCKDB**
- Added built-in `Numbers`, `Dates`, `Times`, `Geography`, `Currencies`, and `Flags` dimension
  tables, with `Numbers` expanded to 1M rows and `Dates` covering a 200-year range (1900–2100), so
  demos and tests no longer need an external database.

**Portal & designer**
- Promoted Lineage to a top-level Governance workspace with its own sidebar, and added a docs
  endpoint so in-Portal documentation matches the Portal layout.
- Added governed multi-statement runs and workbench sidebar parity.
- Extended the visual designer with report-level theme persistence, custom colour-palette pickers, an
  interactive `@variable` parameter binder, Tidy Layout compacting, governance badges, live
  split-screen mode, snapping grid guides, hover drop-zone highlights, container box styling and
  group dragging, and `LAYOUT(COLSPAN, ROWSPAN, WIDTH, HEIGHT)` emission on `CREATE VISUAL`.
- Expanded ECharts option mapping so every visual type renders in snapshot mode.

**Engine & type safety**
- Added integer digit-precision and sign constraints on temp tables, and `INT(N,+)` / `INT(N,-)`
  sign enforcement for flat-file columns.

**Tooling**
- Added a VS Code Visual Flow (DAG) webview backed by the shared script DAG builder.
- Added Portal subject-module sub-choices to the Windows installer and Linux package configuration.

### Changed

**Connector assemblies**
- Split the monolithic `ETL-SQL.Connectors` assembly into per-domain projects — `.Cloud` (S3, Azure
  Blob, SharePoint), `.Messaging` (Kafka, SMTP), `.Remote` (FTP, SFTP, Directory, Active Directory)
  and `.Databases` (the ten database connectors, plus `DatabaseConnectionStringBuilder` and
  `ConnectorRetryPolicy`) — alongside the existing `.Common` and `.Files`. Hosts now reference only
  the connector groups they register, so a host no longer drags in every provider SDK transitively.
  Provider namespaces are unchanged, so scripts and connection syntax are unaffected.

**Report Designer lays out against the last compiled snapshot**
- The designer canvas now renders visuals using data from the report's most recent `.etlsnap`
  package instead of empty wireframe placeholders, so layout decisions are made against real shapes
  without touching a production database. Rows are capped at 500 per visual and the canvas badges a
  sampled snapshot.
- A report that has never run, or one whose output depends on the viewer's identity, has no shared
  snapshot and continues to show placeholders — identity-sensitive reports deliberately never
  persist one.

**Release gate**
- `Test-PreRelease.ps1` now fails when `THIRD-PARTY-INVENTORY.md` no longer matches the package
  graph, so the licence review and NOTICES cannot silently drift.
- Ten build and publish scripts were renamed from `under_scores` to `hyphens`
  (`publish-release.ps1`, `build-msi.ps1`, `build-linux-packages.sh` and so on). Anything invoking
  them by path needs updating; `scripts/README.md` lists all 90 scripts.

### Fixed

**Real column types for MOCKDB and SQLite**
- The schema and session explorers previously showed `ANY` for every MOCKDB and SQLite column. Both
  now report real declared types, including nullability and primary keys.
- **Note:** the schema cache is consulted before the connector, so an existing workstation keeps
  showing `ANY` until its cached entry ages out (14-day maximum) or `%LOCALAPPDATA%/ETL-SQL/SchemaCache`
  is cleared.

**Editor CLI rejects unknown options**
- `etl-sql-editor` previously ignored an unrecognised flag and then treated its value as the
  workspace path, so `--profile dev` silently opened a folder named `dev`. Unknown options now fail
  with usage. `--profile` was removed from the documented command shape; local connection profiles
  were deliberately not built.

**Result grid no longer renders unbounded result sets**
- The grid built one row of DOM for every row returned. Runs started from the Workstation editor and
  the Portal are capped, but the VS Code REPL streams whatever the CLI evaluated, so a large
  `SELECT` could hang the results panel. The grid now draws at most 5,000 rows and labels a
  truncated view "showing first N of M". Export is unaffected and still writes every row.

**`WAITFOR FILE UNLOCKED` no longer reports a false syntax error**
- The linter grammar modelled only `WAITFOR DELAY | TIME | (condition)`, so a valid
  `WAITFOR FILE UNLOCKED` statement was flagged as a syntax error in the editor and completion
  stopped offering next tokens. The parser always accepted it.

**`SHOW DATASETS` no longer reports a false parse failure**
- Fixed alongside the data-quality work; `QUARANTINE` was also unreserved as a keyword so existing
  scripts using it as an identifier keep parsing.

### Performance

- Removed 432 bytes of per-row validator allocation for passing synchronous data-quality rules by
  keeping their hot path out of async state machines and boxed interface enumeration. A 100,000-row
  allocation budget test allows no more than 4 KB total measurement noise; `EXPR` evaluation and
  real quarantine/warn writes remain asynchronous.
- Reduced Portal catalog-search allocation pressure by replacing the Levenshtein two-dimensional
  allocation with rolling buffers.
- Cached request-scoped Portal group lookups by user to avoid repeated `UserGroups` queries during
  catalog and permission checks.
- Compiled repeated variable-interpolation regular expressions and optimized soft equality byte-array
  comparison with span-based sequence comparison while preserving existing DateTime second-level
  semantics.

### Security

**SFTP host-key verification is now closed by default**
- The SFTP connector previously connected with only a logged warning when `HOST_KEY_FINGERPRINT` was
  unset, trusting whatever server answered. With no trust anchor the client cannot distinguish the
  real server from an interceptor, so an unpinned connection is now **rejected**.
- Added `ALLOW_UNPINNED_HOST_KEY` (default `false`) to opt out explicitly where an unverified
  connection is genuinely intended, making that an intentional choice rather than the default. A
  fingerprint that is set but does not match is still always rejected; the opt-out does not weaken it.
- **Breaking:** scripts using SFTP without `HOST_KEY_FINGERPRINT` now fail until they set either the
  pin (preferred — `ssh-keygen -lf <server_host_key>`) or `ALLOW_UNPINNED_HOST_KEY = 'TRUE'`.
  See [SFTP connector](docs/reference/connectors/services/sftp.md).

**Cached schema reads re-check egress policy**
- The Workstation editor's schema endpoint served table and column names straight from its cache
  without consulting the connector that enforces egress policy, so a host blocked after the cache
  warmed kept being completed in the editor. Policy is now re-checked on every request and a denied
  host returns `403`.
- Report access approval is now report-scoped by default through `ReportAcl` and audited atomically
  with the grant/denial mutation.

**Report authorship does not survive deprovisioning**
- Report authorship upgrades an existing grant to `Manage`; it is not standing permission on its own.
  An author with no remaining folder access and no report ACL loses access to reports they created,
  so removing a user from their groups or from the directory actually revokes that access.
- The same rule governs anonymous share and embed links: a link resolves only while its creator still
  has access, and otherwise reports as `PermissionLost` in the admin anonymous-access inventory
  instead of continuing to serve report data to unauthenticated visitors.

## [0.16.0] — 2026-07-19

### Added

**Central Security Events**
- Added a versioned, vendor-neutral security-event contract with correlated policy denials, lifecycle failures, override attempts, enrollment changes, and resource-limit violations across every host.
- Added a bounded durable local outbox, acknowledgement-based HTTPS delivery using enrolled machine identity, signed-policy severity filtering, bootstrap OS/file sinks, delivery diagnostics, and optional fail-closed health thresholds.
- Added fault-injection coverage for collector and acknowledgement failures, corrupt state, storage pressure, crash recovery, redaction, and enforcement independence from monitoring availability.
- Documented the collector protocol and example Splunk CIM, Elastic ECS, and Microsoft Sentinel ASIM field mappings.
- Added retained-evidence Windows and Linux enterprise certification lanes covering policy lifecycle, enforcement boundaries, standalone behavior, and security-event delivery.
- Certified enterprise policy bootstrap across Portal, Orchestrator, CLI, TUI, Report Player, Report Builder, Language Server, scheduled jobs, spawned runners, and parallel execution; corrected Report Player policy configuration ordering.
- Added retained malicious-input and policy-bypass drills; canonicalized connector aliases before policy enforcement and stripped log-forging characters from security events.
- Certified unenrolled standalone startup with no enterprise HTTP clients or remote event collector, unchanged local configuration, and unrestricted local workflows.

**Schema-resilient flat files**
- Added schema-resilient CSV and Excel ingestion modes: map columns by header, ignore extra source columns, and null missing columns so upstream schema drift no longer fails a load.

**Portal report editor**
- Added an in-designer report preview pane to the standalone script editor so authors can render a report without running a separate serve command.
- Separated **Save** from **Git commit** in the editor, each with its own action, so saving a draft no longer forces a commit.

**Engine**
- Added data-source cancellation hooks so long-running source reads observe cancellation and unwind promptly.

### Changed

- **Connector modularization.** Split the connector implementations into independently deployable projects — `ETL-SQL.Connectors.Common` (shared helpers) and `ETL-SQL.Connectors.Files` — and decoupled `ConnectionStringBuilder` from the database drivers so a host no longer loads every database, cloud, messaging, and native dependency to use one connector.
- **Thinner Portal controllers.** Extracted `ReportScriptInspectionService`, `ReportDependencyService`, and `ReportStructureService` out of `ReportsController`, moving report-parameter parsing, dependency resolution, and structure/AST work into application services.
- Renamed internal `ReportPortal` identities to `Portal` for a consistent namespace.
- **Documentation restructure.** Reorganized the docs tree around single-responsibility sections (`guides/`, `reference/`, `architecture/`, `administration/`, `releases/`) with thin guide hubs and a task index; embedded runtime-help filenames were preserved so in-app help keeps resolving.
- Enforced the documented source-tier layering with architecture boundary tests, so a new upward project reference or banned cross-layer package fails CI.

### Performance

- Bounded Portal storage sampling so usage reporting no longer scans unboundedly on large stores.
- Batched Portal user-role lookups to remove per-user round trips when listing users.

### Security

- Canonicalized connector aliases before policy enforcement and stripped log-forging characters from emitted security events.
- Corrected the Docker security-event outbox startup path so containerized hosts initialize delivery reliably.
- Resolved the security and release findings raised in the v0.16.0 sprint code review.

### Fixed

- Serialized enterprise policy initialization and ignored stale policy notifications so runtime configuration and security-event transport cannot regress during concurrent refreshes; disposed configuration roots now release their policy subscriptions.
- Restored true Release builds for `ETL-SQL.Analysis`, redacted fatal CLI/TUI startup exceptions, and passed report-launch arguments without string concatenation.
- Restored the repository format gate by correcting import ordering in enterprise security and fleet-policy files.
- Propagated cancellation through warehouse and data-source schema resolution so cancelled jobs stop promptly instead of completing schema work.
- Read Portal user lists using the paged API so large directories return complete, correct results.
- Included the split connector projects in the Docker restore so container builds resolve every connector assembly.

## [0.15.0] — 2026-07-12

### Added

**SQL Logic, Parser & Correctness Fuzzing (Phases 1-4 & Hardening)**
- Shipped pure in-memory `MOCKDB()` crash-testing fuzz harness, executing up to 1,000,000 queries in under 5 minutes without memory leaks or unhandled parser faults.
- Added **NoREC (No Relation Query Evaluation)** correctness checks, automatically comparing optimized count queries against unoptimized case-when sum queries on `MOCKDB()` to assert logical execution parity.
- Added **Token Corruption & Mutation Fuzzing** (5% probability) to ensure the parser recovers cleanly with structured `SyntaxException` warnings rather than unhandled index or reference crashes.
- Extended fuzzer query walks to support advanced relational syntax: windowing functions (`ROW_NUMBER()`, inline partition/order frames, and named `WINDOW` declarations), filtering clauses (`QUALIFY` and aggregate `FILTER(WHERE...)` clauses), and advanced grouping set combinations (`ROLLUP`, `CUBE`, `GROUPING SETS`, `ALL`).
- Added diagnostics, concurrency blocks (`PARALLEL BEGIN ... END`), transactional bounds (`COMMIT`/`ROLLBACK`), system options (`SHOW`/`SET`), and global variables (`@@NOW`, `@@TODAY`).
- Integrated recursive AST expression minimizer (`QueryMinimizer.cs`) to isolate and prune crashing queries to minimal reproduction cases.
- Configured fuzzer iterations using the `ETLSQL_FUZZ_ITERATIONS` environment variable, defaulting to 500 for check-ins.

**Column-to-Column Interactive Lineage Engine**
- Built an interactive, high-fidelity Vanilla JS column-to-column lineage graph engine featuring ReactFlow-style visuals, visual mapping ports, midpoint edge badges, and column path isolation.
- Added cursor-pinned zoom math, floating details sidebar, Ctrl-Click lineage filtering, node filters, PII toggles, inline formulas, and recursive BFS column lineage traces.

**Shared Connection Governance & Secret Hardening (Phase 7)**
- Added organization-designated sensitive connection metadata and per-connection use ACLs.
- Added connection catalog with `SHARED:alias` expansion.
- Shipped Portal secret store (admin API, provider, key-ring checks).
- Created native admin services (Slice E) and lifecycle CLIs (Slice A) for secrets.
- Hardened parsing to reject unquoted `SECRET:` or `ENC:` values and lint unresolvable references.

**High Availability (HA) & Soak Certification (Phase 6)**
- Added native HA large-job soak runner, HA fault-injection runner, and CLI commands.
- Integrated HA diagnostics bundle and metrics snapshots.
- Shipped sustained load workload templates, topology harness, and evidence validation gates for pre-release verification.

**Adaptive Execution & Resource Controller (Phase 2)**
- Integrated adaptive worker admission and concurrency caps for parallel loops.
- Wired adaptive batch and memory grant setpoints based on resource sampler.
- Gated spill writes with adaptive concurrency.

**Allocation Budgets & Spill Churn Reduction (Phase 1)**
- Met Gate F round-trip performance benchmarks: +74% throughput, -63% GC allocations at scale (10M / 50M rows and 1B scale certification).

## [0.14.0] — 2026-07-05

### Added

**Enterprise Policy Enforcement & Monitoring (Phase 3)**
- Added an administrator-only policy-authority API (`api/admin/policy-authority`) to validate, version, sign, publish (staged or active), activate a staged version, emergency-rollback, and retrieve organization policies per tenant/environment, backed by a durable append-only published-version history (dual-provider SQLite/PostgreSQL migrations).
- Added machine-authenticated policy distribution (`GET api/policy-authority/envelope`): enrolled machines retrieve their signed policy using enrollment headers plus an optional TLS client certificate; responses are bound to the registered tenant/environment, and unknown, revoked, or reassigned machine identities are refused and audited.
- Added a policy-authority availability health check and signing-key-rotation tracking; publication, activation, rollback, machine revocation, and distribution denials are recorded in the durable audit trail.
- Added staged rollout and emergency rollback with monotonic issuance, so clients that reject older issuance times always converge on the newer signed version.

**Billion-Row Columnar Execution Foundations**
- Designed and implemented a native, high-performance, append-only segmented `#temp` storage engine with `ColumnBatch` buffers to bypass row-at-a-time (`Row`/`DataTable`) overhead.
- Built a process-wide memory-grant arbiter (RAM governor) backing external sort, join, distinct, aggregates, and window query operations, dynamically controlling memory ceilings and triggering partition spilling.
- Optimized spilling to use large sequential spill extents (128 MB target) to reduce file metadata and reader/writer overhead.
- Integrated bounded double-buffered pipelining to overlap extent writing with chunk production.
- Optimized projection, UTF-8 selection slicing, and key-only/numeric aggregations directly on native buffers (columnar islands).
- Added adaptive hash partition sizing, window/join fan-out scaling, and sort run extraction without boxing.
- Integrated scale certification tiers: Smoke (1 GB), Standard (4 GB, 10M rows), Stress (8 GB, 5M rows), and Huge (16 GB, 50M rows).

**Row-Level Security (RLS) & Impersonation (Phase 1 & 2)**
- Added identity system variables (`@@CURRENT_USER`, `@@CURRENT_USER_ID`, `@@REAL_USER`, `@@IS_ADMIN`) and functions/predicates `HAS_GROUP('name')` / `HAS_ROLE('name')` with default-on admin bypass.
- Added table-valued `USER_GROUPS()` and `USER_ROLES()` to query active groups/roles in joins.
- Implemented secure preview-as/impersonation for folder editors and administrators, never-cached sensitive reports, and recipient-level execution identity resolution for subscription emails.

**File Connectors & Excel Write Support**
- Added write and append support for Excel (.xlsx) files via MiniExcel.
- Enforced stream-on-the-fly decryption and decompression for FlatFile, JSON, XML, and Excel connectors.
- Added support for `.etlds` extension for exported dataset files and `.etlsnap` for Apache Arrow snapshots.

**Host Metrics & Operational Alerting**
- Added persistent host metrics tracking disk/memory/CPU capacity, a new `SHOW HOST METRICS` statement, and daily rollups.
- Added automatic reconciliation of stale RUNNING jobs as `INTERRUPTED` on startup.
- Shipped Portal operational metrics digest email.

**SFTP Connector Hardening**
- Host-key verification using `HOST_KEY_FINGERPRINT` for MITM protection.
- Opt-in atomic upload (`ATOMIC_UPLOAD = true`) uploading to temporary files before renaming.

### Changed

- **Octocolee Product Naming:** Introduced Octocolee as the product name (ETL-SQL remains the engine name).
- **Default Columnar Temp Storage:** Configured columnar temp storage by default.
- **Release Infrastructure:** Added lightweight secret scan, SBOM generation, and pre-release gates.

### Fixed

- **Parser and Security Fixes:** Sanitized `QuoteIdentifier` routines to prevent SQL injection.
- **VS Code Extension & TUI Fixes:** Fixed VS Code extension vulnerabilities, terminal command builder escape bugs, and resolved window resize lag/input blocking on Unix in the TUI.

### Security

- **Execution Policy Enforcement Boundary:** Added execution policy snapshot context (`ExecutionPolicySnapshot`) and dynamic policy validation.
- **Shared Enforcement Snapshot:** An immutable policy snapshot is captured when execution begins and propagated unchanged through CLI, TUI, Report Player, Portal, Orchestrator, parallel branches, recursion, and scheduled jobs, making denials deterministic across in-process and spawned execution.
- **Governed Connector Egress:** Enforced enterprise connector-type, destination host, scheme, and port allowlists before DNS resolution and connection creation, including dynamic REST redirect/pagination/template targets. Local egress denials surface as a plain security error; organization-policy denials carry the governed key and correlation identity.
- **DNS-Rebinding & Proxy-Bypass Hardening:** The REST connector re-validates the DNS-resolved address at connect time and pins the socket to the validated set, and disables ambient proxy use — closing rebind-to-internal-IP and proxy-bypass paths. Obfuscated IP literals are normalized and loopback/link-local/private/CGNAT/ULA ranges are denied unless explicitly listed; URL-embedded credentials are rejected regardless of policy.
- **Filesystem Policy Boundary:** Restricted local paths in remote file transfers, directory synchronization, and recursive file/directory operations. `COPY FILE` and recursive directory copy stream through handle-validated opens (OS-resolved final-path re-check after open) to resist link-substitution races; delete/move/copy re-authorize immediately before the OS call.
- **Governed Resource Ceilings:** `MAX_PARALLEL_DEGREE`, `MAX_FILE_OPERATIONS`, `MAX_RECURSIVE_DEPTH`, `MAX_SMTP_EMAILS_PER_SCRIPT`, and `MAX_STRING_RESULT_SIZE` cannot be weakened by `SET`, configuration, environment variables, command-line options, restored sessions, or report parameters; the enterprise ceiling is bound from the immutable execution snapshot at execution start and re-checked at each operation boundary.
- **Allowed Extension Tightening:** Removed generic `.tmp` from whitelisted user file extensions to prevent insecure temp file usage.

## [0.13.0] — 2026-06-28

### Added

**Apache Arrow Snapshot Integration**
- Completed end-to-end Apache Arrow IPC snapshot support: the `SnapshotStore` now saves and loads secure `.etlsnap` zip packages by default in CLI and local execution contexts.
- Local and CLI snapshot packaging runs without explicit key configuration by falling back to host-bound at-rest encryption (see Security for the hardened behavior).
- The report runtime player now lazy-loads and decodes Arrow IPC streams on-demand with automatic fallback to JSON row endpoints for older clients.
- Downloaded and bundled the minified Apache Arrow JS library (`arrow.min.js`); synchronized front-end runtime assets across Portal, Player, and VS Code extension.
- Added test coverage verifying CLI/local `.etlsnap` roundtrip packaging.

**Portal Execution Metrics & Observability**
- Added persistence of per-execution resource metrics (CPU, memory, duration) to the Portal database so historical load can be trended over time (`AddPortalExecutionResourceMetrics` EF migration for both SQLite and PostgreSQL).
- Exposed a historical execution load metrics endpoint on `AdminController` for operators and monitoring systems.
- Added lazy-loading of Arrow snapshot rows in the Portal to avoid pulling large result payloads into memory until requested.

**`SHOW PORTAL USAGE METRICS` and `SHOW PORTAL OPERATIONAL METRICS` Statements**
- Added `SHOW PORTAL USAGE METRICS [INTO #t]` inside an `EXECUTE portal` block to return report view counts, unique viewers, refresh health, and subscription delivery failures for the requested period.
- Added `SHOW PORTAL OPERATIONAL METRICS [INTO #t]` to return live queue depth, execution concurrency caps, recent failure counts, storage size, schema migration status, and last-24-hour execution load/resource buckets — complementing the existing `GET /health` endpoint with a scriptable, queryable form.
- Wired both statements through the parser (`SystemParser`), AST (`ShowPortalUsageMetricsStatement`, `ShowPortalOperationalMetricsStatement`), and `PortalDataSource`; updated `PORTAL_SHOW.md` help file, `Grammar.md`, and `Syntax_Index.md`.

**`SHOW LOCKS` Statement**
- Added `SHOW LOCKS` to display currently held engine-level and orchestrator-level resource locks, aiding live diagnosis of stalled pipelines and contention scenarios.
- Documented `SHOW LOCKS` in `Grammar.md`, `Syntax_Index.md`, `User_Manual.md`, `PORTAL_SHOW.md` help file, and the `SHOW` keyword help document; wired a corresponding test in `SystemAndReportHandlerTests`.

**LSP Cross-File Declaration Resolution**
- Extended the Language Server's `DefinitionProvider` and `HoverProvider` to resolve `GO TO DEFINITION` and hover targets across all currently open files in the workspace, not just the active document.

### Changed

**Performance — Engine & Language Server**
- Indexed lineage in `LineageTracker` and cached parameter scans in `ParameterScanner` to avoid repeated linear walks during analysis and execution.
- Added parse-result caching to `RunScriptStatementHandler` so `RUN SCRIPT` targets that have not changed on disk are not re-parsed on every invocation.
- Cached LSP definition declarations in `DefinitionProvider` and `DocumentStateStore` to avoid redundant re-analysis on every keystroke.
- Hardened Portal metrics and scaled hot paths: added `AssetFingerprinter`, tuned spill-store and external sort/join engines, and improved scheduler throughput under load.

**Machine-Aware Orchestrator Throttling & Startup Sweep**
- `JobThrottle` now reads available logical processors and physical memory at startup to derive a machine-aware default concurrency ceiling, preventing over-subscription on small VMs.
- Added `ChildProcessTracker` to associate child processes spawned by the Orchestrator with their parent job, enabling clean resource reclamation on job cancellation.
- Added a startup temp-table sweep in `EngineRunner` to remove orphaned `#temp` working directories left by crashed sessions, preventing unbounded disk growth.

**Stabilization & Refactoring (Engine, Analysis, Portal, TUI, Tooling)**
- Completed a broad stabilization pass across the engine: audited and hardened all `ETL-SQL.Engine` statement handlers, `RelDateResolver`, `ResultFormatter`, `SessionStateManager`, `VariableScopeManager`, `CteManager`, `PushdownEngine`, `QueryCompiler`, `DataSourceManager`, `LineageManager`, and `SpillStore`.
- Hardened the `AliasScanner`, `SnippetLibrary`, and `SnapshotStore` in `ETL-SQL.Core` and `ETL-SQL.Reporting`; made the `sync-assets.js` asset-sync script idempotent and banner-aware.
- Tightened `AbsolutePathRule`, `CredentialLeakRule`, and `FileSystemSecurityRule` linting rules with additional corpus cases for path boundary and credential-leak scenarios; strengthened `SchemaValidationRule` in Analysis.
- Hardened `CryptoUtils`, `MachineBoundCrypto`, and `LruCache` in `ETL-SQL.Core.Common`; hardened `SqliteSessionMetadataStore` with retry semantics and tighter WAL mode configuration.
- Hardened engine cleanup and path handling across `RunScriptStatementHandler`, `ExecuteStatementHandler`, `BundleStatementHandlers`, `WaitForFileStatementHandler`, `CteManager`, `ProcedureExecutor`, and `SessionStateManager`.
- Hardened async export and backup paths in `BackupRestoreService`, `EngineRunner`, `BrowserReportPdfExporter`, `ExportController`, and the TUI `ConsoleEditor`.
- Added `AssetFingerprinter` to the Portal for cache-busting on static asset updates; added EF migration for PII column encryption on both SQLite and PostgreSQL providers.
- Stabilized `JobApiEndpoints` with improved cancellation propagation and error surfacing; tightened `NodeCapacityMonitor` assertions and added `SchedulerService` queue-wait-time argument fixes.

**TUI Frame Metadata Caching**
- `EditorRenderer` now caches rendered frame metadata between redraws, reducing CPU usage during idle periods and making the status bar and key-binding overlays allocation-free on unchanged frames.

**Documentation & Policy**
- Reconciled identity configuration reference in `Administrators_Guide.md` to match shipped OIDC behavior.
- Tightened contribution rules and compatibility policies in `CONTRIBUTING.md`.
- Documented future performance and scalability enhancements in `TODO.md`.

### Fixed

- **Support bundle redaction**: `SupportBundleBuilder` now redacts connection-string passwords, API keys, and JWT secrets from all diagnostic fields before archiving; added corresponding `OperatorToolingTests` coverage.
- **Portal database migration test failures**: Resolved a portal database upgrade migration ordering issue and fixed a metric timezone normalization bug that caused flaky test failures under certain locale configurations.
- **SFTP connector `ConnectionStringBuilder`**: Corrected option serialization for `SFTP` connector key-file auth paths.
- **TUI frame caching**: Fixed stale frame metadata being rendered after connection or tab changes in `EditorRenderer` and `StatusBar`.
- **Migration lint corpus**: Added a migration lint corpus (`test(compat)`) to catch invalid dialect usage introduced across schema migration scripts.
- **Scheduler test mock**: Fixed `SchedulerService` test mocks that passed an incorrect argument count for the queue-wait-time parameter after an API change.
- **GROUP BY ALL column expansion**: Resolved a bug in `SelectStatementHandler` where `GroupByAll` was expanded before output column expansion, resulting in engine crashes when star-modifiers (`* EXCLUDE (...)`) or qualified stars (`t.*`) were present in the query.
- **Positional reference star projection checks**: Hardened positional reference checks in `Parser.ResolvePositionalReference` to correctly identify and block qualified star and star-modifier projections from bypassing positional sorting/grouping syntax checks.

### Security

- **PII column encryption at rest**: Portal database columns storing user PII (email addresses, display names in audit records) are now encrypted at rest using a key derived from the configured Data Protection key ring, applied via a background maintenance service and corresponding EF Core migration for both SQLite and PostgreSQL.
- **Support bundle hardening**: Connection strings, JWT secrets, and API keys are now actively redacted from the support bundle rather than relying solely on config-key exclusion lists.
- **Crypto hardening**: Strengthened `MachineBoundCrypto` key derivation and `CryptoUtils` authenticated-encryption paths; added additional test coverage for encrypt/decrypt roundtrips and tamper-detection.
- **Service Account token exchange timing mitigation**: Hardened the service-credentials token endpoint against client-ID enumeration timing attacks by always executing password verification against a dummy hash when the Client ID is not found or is inactive.
- **Client certificate store handle leak cleanup**: Resolved an OS handle leak in `EnterprisePolicyRuntime` during OIDC/HTTPS policy certificate store searches by disposing non-matching certificate instances.
- **Egress sanitization & parameter utility ReDoS hardening**: Hardened regular expressions in `ConnectorExceptionWrapper` and `ParameterUtility` to use source-generated regex `[GeneratedRegex]` with a `1000ms` timeout to protect against catastrophic backtracking.
- **Snapshot at-rest encryption fallback hardening**: When `Portal:Dataset:AtRestKey` is unset, report snapshot (`.etlsnap`) packages now fall back to the same host-bound `ENCRYPT=MACHINE` protection used for dataset caches (DPAPI LocalMachine on Windows; authenticated AES-256-GCM keyed from the machine id elsewhere), instead of a source-public default key. Reading a key-managed snapshot now fails closed if the key is absent. `MachineBoundCrypto.Protect/Unprotect` are exposed for reuse, and a one-time warning is logged when the host-bound fallback is in effect.
- **Authenticated machine-bound generic encryption**: `CryptoUtils` machine-key protection on platforms without DPAPI is now encrypt-then-MAC (HKDF-SHA256 encryption/MAC sub-keys + HMAC-SHA256 verified in constant time) instead of unauthenticated AES-CBC; legacy CBC-only payloads remain readable.
- **`machine.key` permissions**: the generated machine key file is now created owner read/write only (`0600`, directory `0700`) on Unix, atomically, so it is never briefly world-readable.

## [0.12.0] — 2026-06-19

### Added

**Practical High Availability — Multi-Node Portal & Orchestrator**
- Made both the Portal (EF Core) and Orchestrator (hand-written) state stores **provider-selectable** between SQLite (default, unchanged) and PostgreSQL via configuration (`Portal:Database` / `Orchestrator:Database` Provider + ConnectionString), removing the previously hardcoded SQLite coupling. PostgreSQL is implemented end to end for both stores and verified against a real Postgres via Testcontainers: the Portal gained a dedicated migrations assembly for Postgres, and the Orchestrator store became a provider-neutral `RelationalJobHistoryStore` behind a dialect (portable SQL, with a Postgres `nocase` ICU collation backing `COLLATE NOCASE`).
- Added `etl-sql admin migrate-database --from sqlite --to postgres [--dry-run]` to copy existing single-node SQLite Portal/Orchestrator state into the configured PostgreSQL deployment: values are coerced to each target column's type, foreign-key ordering is bypassed for the load, identity sequences are resynced, and per-table row counts are verified — any mismatch fails closed (nothing is committed). `--dry-run` verifies counts and target-schema compatibility without writing.
- Added a unified `IArtifactStorage` interface with **Local** and **SMB/UNC** providers so reports, scripts, snapshots, and custom-map assets live on a shared root reachable by all nodes, with `SecurityService` guardrails enforced at the storage boundary.
- Added database-backed cluster coordination: **node heartbeats and a cluster registry** (liveness on the database clock, with expired rows pruned on the heartbeat loop), **monotonic fencing tokens** for state and shared-storage writes, and **database-backed leader election** that serializes migrations and singleton work. Stale writers are fenced and in-flight portal work is cancelled on node lease loss.
- Added per-node capacity gating with **job quarantine**, cross-node capacity claims, and snapshot write-failure recovery.
- Added a scalable **HAProxy** docker-compose with sticky (session-affinity) balancing, a configurable shared Data Protection key ring, and a lightweight `GET /healthz` load-balancer probe (richer diagnostics remain on `GET /health`). HA clusters require a shared artifact root, a shared key ring, identical JWT/orchestrator/dataset keys across nodes, and load-balancer session affinity for node-local interactive sessions.

**Job-Scoped State Persistence & Incremental Watermarking**
- Implemented `GET_JOB_STATE(key)` and `SET_JOB_STATE(key, value)` primitives for scheduled and ad-hoc incremental data loads.
- Buffered state updates during execution, committing them atomically to the orchestrator store (SQLite or PostgreSQL) only upon successful script completion.
- Added a developer CLI fallback that persists state in local `[script_name].etlstate` JSON files.

**JSON/Spec-Backed Schema Contract Checks**
- Extended the `EXPECT SCHEMA` syntax to validate schemas using a reviewed JSON specification contract file: `EXPECT SCHEMA target FROM 'path/to/spec.json' [ON DRIFT WARN];`.
- Added support for verifying column presence, type family matching, nullability constraints, string length limits, and decimal precision/scale settings loaded from the JSON `"schema"` array, respecting `context.ResolvePath()`.

**Certified OpenID Connect (OIDC) Authentication**
- Implemented federated login, logout, and token refresh in the Report Portal with support for external Identity Providers.
- Hardened user account binding by keying local profiles to the immutable OIDC `sub` (subject) claim to prevent takeover risks if usernames/emails are reassigned.
- Added dynamic group mapping to synchronize identity provider role/group claims to local Report Portal user groups at login.
- Added configuration diagnostics and redacted status checks to ensure OIDC provider availability can be monitored without exposing client secrets.
- Certified recovery scenarios (IdP outages, JWKS key rotation, claim modifications, and token revocation) with a robust integration test suite.

**VS Code Extension Enhancements**
- Cleaned up ESLint static analysis and type declarations across TypeScript sources.
- Stabilized the extension integration test suite by tuning Mocha bootstrap timeouts to accommodate headless environment activation delays.

### Changed

**Pushdown Aggregation & Staged Extracts**
- Enabled SQL pushdown for eligible `SELECT ... INTO #temp` queries containing `GROUP BY`, aggregates, `DISTINCT`, and compatible joins. Pushes aggregation down to the source database and streams only grouped/filtered results back.

**Cross-Connection Semi-Join Pushdown**
- Added an optimizer that rewrites joins between small local temp tables (1-1000 rows) and large remote SQL tables to push a parameterized key filter (`IN` clause) directly to the remote query, preventing full-table memory loading.
- Optimized compiling of the query key list using driver-parameterized values (`@p0`, `@p1`, etc.) to leverage caching and prevent injection, with plan visibility under `[SEMI-JOIN PUSHDOWN ON ...]`.

**Evaluator Performance Enhancements**
- Optimized hot-path identifier and column resolution by switching to allocation-free `Row.TryGetValue` instead of copying new row columns dictionaries, saving significant heap allocation during streaming query execution.
- Avoided redundant column lookups during variable and identifier evaluations using a unified `TryResolveIdentifier` check.

### Fixed

**Test Stability**
- Stabilized two timing-sensitive Docker integration-lane tests that failed intermittently only under full pre-release load: relaxed a `Retry-After` delay assertion to tolerate the ~15.6ms Windows timer quantum, and raised the orchestrator scheduled-job history poll timeout above the container's own job timeout so a job nearing its budget under load is not abandoned prematurely.

## [0.11.0] — 2026-06-14

### Added

**Secure Datasets**
- Reworked the DATASET subsystem for multi-user safety: globally unique dataset names with stable-Id storage paths, dataset→folder linkage where `PUBLIC` resolves to folder-read permission, and caller-identity threading that closes an ACL bypass.
- Added portal-managed at-rest encryption for the dataset cache (parquet encrypted at rest), failing closed on a missing or weak at-rest key, with at-rest key rotation and a verification deck.
- Added `EXPORT DATASET` (a portable transport-encrypted copy) and `PUBLISH DATASET` (import a portable file and re-encrypt at rest).
- Added serve-stale-with-warning behavior plus an editor/owner refresh gate, refresh triggers, and authorization/atomicity hardening.

**Script-First Portal Reconstruction**
- Added `EXPORT PORTAL CONFIGURATION` to export users, groups, memberships, folders, ACLs, report publications, dataset metadata/grants, SMTP aliases, subscriptions, and alerts as a versioned, idempotent `.etlsql` bootstrap script that emits logical names (never database IDs).
- Excluded all credentials, keys, and cached values from the export, emitting `${...}` secret placeholders with a generated requirements header.
- Made bootstrap import deterministic and rerun-safe (create-or-skip by logical name) with `SET WHAT_IF ON` dry-run validation that fails closed on missing secrets or references.
- Added a companion content manifest / recovery runbook, and an automated clean-server round-trip reconstruction proof.

**Multi-User Correctness & Recovery**
- Fixed the folder/asset ownership lifecycle (ownership now implies Manage) with explicit ownership transfer/reassignment before user deletion.
- Made audit recording part of the operation contract: security-sensitive mutations and their audit rows now commit atomically, with correlation IDs for background work and opt-in retention.
- Added a durable per-job execution lease (Orchestrator), a recoverable subscription lifecycle, and a durable subscription delivery ledger with at-most-once semantics and idempotency/failure tests.
- Added per-user execution fairness limits, scriptable SMTP connection management, refresh-token reuse detection/purge with cached-token validation, and bounded report-snapshot retention.

**Operator Tooling (CLI)**
- Added an `etl-sql admin` command group with `admin doctor` (a backward-compatible alias of `doctor`) and `admin support-bundle`, which produces a credential-redacted archive (config, health snapshot, recent logs, database metrics).
- Added `etl-sql init` to scaffold a starter configuration (with a generated JWT secret) and a first runnable `.etlsql` script for CLI-first onboarding.
- Added `etl-sql admin backup` (split-custody data + keys archives) and `etl-sql admin restore` with fail-closed `--validate` (matching backup-id pair, key-version coverage, per-file checksums, and version compatibility).
- Surfaced database schema migration status on the operational metrics endpoint, and wired the N→N+1 in-place upgrade-path drill into `Test-PreRelease.ps1` as a release gate.

**Verification & Observability**
- Added a hosted-service integration lane, genuine multi-process coordination tests, fault-injection/recovery tests, an automated backup/restore drill, and an admin operational metrics endpoint (queue depth, active executions, failure rates, dataset/snapshot disk usage).

**Language & Engine**
- Added inline tags in `CREATE TABLE` and `INT(N)` fixed-width digit precision.
- Added a memory-grant arbiter, tag value validation, and lineage cycle warnings.

### Changed

- **Licensing:** Relicensed ETL-SQL from PolyForm Noncommercial 1.0.0 to the Apache License 2.0 and aligned the installer, VS Code extension metadata, bundled browser assets, contribution policy, and public documentation.
- **Documentation validation:** Added connector-aware checks for `CREATE CONNECTION` examples so unsupported option names and published option values fail the documentation test suite instead of passing grammar-only validation. Connector metadata now exposes supported named `PATH`, `HOST`, and flat-file truncation options used by public examples.
- Formalized automatic SQLite schema migrations on Portal startup: the applied migration set is logged and a migration failure now fails fast rather than serving a half-migrated catalog.
- Realigned the `CREATE` `ENCRYPT` clause as transport-only and removed the cleartext-credential dataset-refresh sidecar.
- Adopted an optimistic-concurrency contract for concurrent administration, batched dataset-listing permission checks for performance, and refreshed branding, trademark, logo, and README positioning.

### Fixed

- Resolved FLATFILE connectors with EXCEL/JSON/XML/PARQUET/AVRO formats to their correct dialects in `PipelineGenerator`, and fixed a `FlatFileDataSource` compiler error.
- Fixed `SessionCache` race leaks and stale admin caller context, a refresh debounce race, and disabled accounts surviving LDAP login; removed the hardcoded first-run admin password.
- Corrected dataset at-rest encryption metadata to be truthful, required Manage to change dataset access level, and regenerated the dataset-refresh-permission migration via EF tooling.

### Security

- Backup secret artifacts (keys archive, key ring, re-injected config) are written with owner-only permissions, and backup manifest validation rejects path-traversal entries.
- Hardened portal sessions and anonymous delivery, added authentication rate limiting and a content security policy, and added runtime secret rotation.
- Closed authentication, SSRF, injection, key-handling (.p8), and audit release blockers; added Dependabot for the NuGet and npm ecosystems.

## [0.10.0] — 2026-06-08

### Added

**Experimental: Specification-Driven Development (Beta)**
- Added `gen-script` CLI command to compile standardized JSON specification contracts into ETL-SQL starter scripts. Generated templates include source layout review notes, confidence/source-evidence comments, casting expressions, inline lineage tags, `EXPECT SCHEMA` gates, validation issue summaries, optional quarantine tables, and outbound load scaffolding.
- Added `extract-spec` CLI command utilizing PDFsharp to automatically trim and extract data dictionary pages from large vendor PDF documents using heuristic keyword scoring.
- Added workflow guide `Docs/Reference/Spec_Driven_Development.md`, prompt instruction guide `Docs/data_spec_parser_instructions.md`, machine-readable contract `Docs/Reference/spec_pipeline.schema.json`, and Cookbook recipe 25 with a runnable customer-feed example.
- Added [PipelineGenerator](./src/ETL-SQL.App/App/PipelineGenerator.cs#L14) and [SpecExtractor](./src/ETL-SQL.App/App/SpecExtractor.cs#L12) test suites under `tests/ETL-SQL.Tests/App/` covering contract validation, generated-script parsing, review metadata, validation gates, and PDF trimming scoring.
- *Note on limits*: This is a developer productivity feature, not an automated production-pipeline generator. LLM spec parsing and vendor formats are variable; generated scripts are intended as reviewed starting points. Developers must verify the JSON, complete the extraction query, review evidence/low-confidence fields, and test against real vendor files.

**Terminal IDE (TUI) Modernization**
- Implemented collapsible sidebar file explorer tree and tabbed multi-file support in [ConsoleEditor.cs](./src/ETL-SQL.TUI/UI/ConsoleEditor.cs#L29).
- Added support for multi-cursor editing, F1 help dialog shortcuts, and drag-to-select text in the editor.
- Added in-editor text find/search with result highlighting and `F3`/`Shift+F3` navigation.
- Added live query diagnostics while editing and visual gutter diagnostic markers.
- Added non-blocking, cancellable script execution, allowing queries to run asynchronously in the background.
- Added a Schema Explorer in the sidebar showing database tables and views with lazy loading support.
- Added a Variables explorer tab in the bottom pane matching the VS Code Variable Explorer functionality.
- Added query result-cell navigation and inspection, along with cell-value inspection popups.
- Added automatic workspace persistence and recovery, preserving open files and tabs across TUI restarts.
- Added customizable JSON-based editor themes with a preset theme library and `F3` theme-cycling hotkey.
- Re-implemented robust console keyboard input via Win32 ReadConsoleInput, resolving terminal input lockups.
- Added per-tab caching for query results, execution messages, active execution tree, and performance metrics.
- Added a new `rollback-all-transactions` command to abort all active transactions.
- Added an Output tab to act as a durable, clickable home for served URLs and export paths.
- Added custom terminal rendering features including braille line charts, fractional-block bar charts, buttons, containers, and `RELDATEPICKER` controls.
- Added a TUI Command Palette (`Alt+P`) and support for exporting reports directly to Markdown or PDF.
- Added a `serve` utility (`Ctrl+Shift+R`) to run report previews directly in the browser via dynamic self-invocation, supporting serve-folder multi-report launching.
- Added Publish to Portal support (matching VS Code publish features) and connection reset commands.

**Connectors & Integrations**
- Added a native **Neo4j** graph database connector supporting key merging, validation, and metadata queries (see [Neo4jConnector.cs](./src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jConnector.cs) and [Neo4jDataSource.cs](./src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jDataSource.cs)).
- Added outbound writing support and completed production gaps for the REST API connector.
- Enhanced Azure Blob, SFTP, S3, and local Directory connectors to include fallback decryption and structured path parsing.

**Language, Lineage & Governance**
- Added `CREATE TAG` and `CREATE LINEAGE FROM ...` syntax to support programmatic importing of curated lineage assets and metadata tags.
- Added the `DIFFERENCE(s1, s2)` Soundex similarity scoring string function (see [FuzzyFunctions.cs](./src/ETL-SQL.Engine/Functions/FuzzyFunctions.cs)).
- Added a cross-platform CLI `etl-sql purge` command for cleaning up old data and session histories.
- Expanded SQL Logic Test (SLT) coverage for index creation, table truncation, table alteration, `LEFT SEMI`/`LEFT ANTI` joins, and `QUALIFY` statements.

**Verification & Orchestration Hardening**
- Added job scheduler chaos coverage and concurrency race verification tests (scheduler, subscription, and active-work).
- Added a subscription delivery diagnostics UI and preserved subscription failures in the history store.
- Added verification tests for Report Portal user permission models and user workflows.
- Added a new capacity planning guide (`docs/architecture/roadmaps/Capacity_Planning.md` or similar) and published service capacity baselines.
- Added capacity workload templates and row-volume capacity planning profiles.
- Added scaling tests for portal administration catalogs and enterprise identity lifecycle verification.

### Fixed
- **Query Parser:** Fixed parser bugs for `LEFT SEMI`/`LEFT ANTI` joins and tolerated trailing semicolons (`;`) for statements inside `BEGIN`/`TRY` blocks.
- **Cookbook Recipes:** Audited and fixed all 23 Cookbook recipes to ensure they compile and parse cleanly, fixing issues with `ENCRYPT`, `SEND EMAIL`, `EXEC`, `DECLARE`, and deprecated `WITH PARAMETERS` report options.
- **TUI Editor:** Implemented file overwrite warnings when a file changes on disk, fixed sidebar layout wipeout during redraw by clearing partial line width, and resolved keyboard input lockups on Windows.
- **TUI Autocomplete:** Fixed snippet triggers (`$mssql`) showing inside the autocomplete suggestions and prevented crashes when brackets appeared in prompt titles.
- **TUI Metadata:** Restored temp table querying inside [TuiMetadataManager](./src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L106).
- **Report Preview:** Fixed report preview wrapping bugs, added rounding for Card/Table numbers, and added page navigation arrows via keyboard/mouse.
- **Test Integrity:** Resolved parallel test conflicts in Neo4j tests, and excluded Docker LDAP portal tests from non-Docker lanes.

### Changed
- **Dependencies:** Upgraded `SQLitePCLRaw` package reference to `3.0.3` to resolve pre-release auditing and scoped it exclusively to Core instead of globally.
- **Code Refactoring:** Refactored `ConsoleEditor` dependencies to use dependency injection instead of service-locating patterns.
- **Platform Infrastructure:** Hardened shell scripts and systemd unit files to use Unix LF line endings.
- **Packaging:** Brought the Linux `.deb` installer to parity with the Windows MSI (including uninstall prompts and service configuration) and published VSIX as a standalone asset.
- **Release Tooling:** Made the pre-release NuGet dependency audit reliable on the pinned .NET 10.0.300 SDK with central package management — solution-level `--deprecated`/`--vulnerable` checks fall back to per-project auditing and fail with an actionable message rather than silently skipping when no authoritative audit can run.

### Security
Hardening from the v0.10.0 release-readiness security review:
- **Orchestrator API authentication:** The ad-hoc job API (`POST /jobs`, `DELETE /jobs/{id}`, `GET /jobs/{id}`) now requires the `X-Orchestrator-Key` header like the scheduled-job and management routes; only `/health` and `/metrics` remain open. The service fails fast at startup when no API key is configured while bound to a non-loopback address, and the MSI/Linux installers generate and mirror matching `Orchestrator:ApiKey` / `Portal:Orchestrator:ApiKey` values.
- **Spec module injection:** Restricted spec dataset names to a documented safe-identifier format, normalized each generated module path to stay within the modules directory, and escaped generated ETL-SQL string literals — preventing path traversal and ETL-SQL injection in `gen-script` output.
- **REST egress / SSRF:** Disabled automatic HTTP redirects in the REST connector; redirects are now followed explicitly with a bounded count, every hop's host is re-validated against the egress allowlist, and credential headers are stripped on cross-host or HTTPS→HTTP redirects.
- **Path Validation:** Enforced zero-trust path validation for the Snowflake `PRIVATE_KEY_FILE` option while accepting the documented `.p8` PKCS#8 key extension.
- **Token Permissions:** Restricted portal token file permissions strictly to the owner.

---

## [0.9.0] — 2026-06-01

### Added

**Reporting: Export Fidelity**
- Server-side ECharts SSR export path: report chart visuals can render real ECharts output into SVG for PDF generation.
- PDF export now includes chart-rendering coverage through `EChartsSsrRenderer` and `PdfExporter` tests, including a PDF magic-header assertion and chart visual rendering path.
- Markdown/table export formatting tightened through the shared report cell formatter so exported tables preserve cleaner display values across report outputs.

**Language: Pipeline Checkpoint / State Resume**
- `LabelName:` syntax as `SectionLabelStatement` — top-level labels auto-serialize `#temp` table contents (Apache Arrow spill) and variable scope (JSON) as named checkpoints.
- `GOTO LabelName;` control-flow statement with full scoping guardrails: GOTO may jump OUT of nested loops, conditionals, and `TRY…CATCH` blocks; jumping INTO nested blocks is a compile-time error; cross-script jumps blocked.
- `--session <id>` and `--resume` CLI flags: `--session` names the state store; `--resume` restores the most recent checkpoint and skips already-completed labels. Passing `--resume` without `--session` or without a saved checkpoint is a fail-fast error.
- LSP: section labels exposed in document outline for folding and symbol navigation; `GOTO` autocomplete lists reachable label names.
- Grammar, User Manual, and Specialized_Operations.md updated with label/GOTO syntax, scoping rules, and `--resume` CLI reference.

**Connector: Native MySQL / MariaDB**
- `MySqlConnector` provider built on the `MySqlConnector` NuGet package — eliminates the ODBC bridge dependency, delivers native dialect parsing, and wraps all provider exceptions as sanitised `ExecutionException`s at the connector boundary.
- Procedure/routine metadata discovery via `MySqlCatalogProvider`.
- Dedicated `MySqlFixture` / `[Collection("MySQL")]` so non-MySQL database tests no longer pay MySQL container startup cost.
- Third-party inventory updated with MySqlConnector 2.3.7 and Testcontainers.MySql 4.11.0.

**Diagnostics: EXPLAIN / EXPLAIN ANALYZE**
- `EXPLAIN <statement>` produces a query-plan table (ID, Operation, Details, Cost, Mode, Est. Rows).
- `EXPLAIN ANALYZE <statement>` adds Actual Rows, Actual Time, and Spill (bytes) columns by executing the statement under instrumentation.
- Available as a `--explain` CLI flag for whole-script plan output.

**Observability: Spill & Memory Metrics**
- `--perf` summary table now includes a "Disk Spilled: X MB" row.
- `--verbose` JSON telemetry packet includes `spilledMb`.
- `SHOW PROFILE` tracks `SpilledBytes` per statement alongside elapsed time and row counts.
- `ExecutionTelemetryManager` exposes `TotalSpilledBytes`, `SubquerySpilledBytes`, and `SortSpillCount` for downstream reporting.
- `Docs/Reference/Performance.md` (new): all four external engine thresholds and activation conditions, `SET` threshold overrides, `appsettings.json` defaults, spill storage and encryption, observability reference, memory model, tuning guidance table, and scale certification tier definitions.

**Governance: Execution Audit Log for Ad-Hoc Runs**
- `Engine:AuditAdHocRuns` appsetting (default: `false`) gates audit logging for standalone `--run` executions.
- When enabled, `EngineRunner` calls `IJobHistoryStore.LogJobStartAsync` / `LogJobEndAsync` so script runs appear in the Orchestrator execution history alongside scheduled jobs.

**Release Infrastructure**
- `scripts/Test-PreRelease.ps1`: local pre-release validation runner with resumable phases (source-hash fingerprinting prevents reusing stale results after code changes). Phases: sync-assets drift, restore, build, smoke/fast test lanes, Node.js unit tests, sample smoke, Smoke-tier scale cert. Optional switches: `-IncludeDockerIntegration`, `-IncludeStandardScale`, `-BuildInstallers`, `-SkipNode`, `-SkipScale`, `-Resume`.
- `scripts/Compare-CertBaseline.ps1`: diffs a `cert-report.json` against a stored baseline — exact pass/fail, result-row count, checksum, and elapsed-time regression (±50% threshold). Exits 1 with a regression table on any failure.
- `docs/architecture/roadmaps/Release_Capability_Matrix.md`: release claim matrix tying public product claims to concrete evidence and preventing release notes from overstating tested behavior.
- `scripts/Get-TestLaneInventory.ps1`: static lane inventory report showing discovered xUnit tests by lane, category trait, project, and fast-lane exclusion reason.
- `perf` lane now runs engine hardening performance tests plus the dedicated perf project; `fast`, `portal`, and `full` lanes include the Node lineage UI smoke test.
- Scale certification baselines committed: `certification-results/baseline-smoke.json` (Smoke, 1×) and `certification-results/baseline-standard.json` (Standard, 10×, 13 scenarios, all passing).
- `.github/CODEOWNERS` and Dependabot configuration added.
- Four GitHub workflow templates under `.github/workflow-templates/` (local-validated-release, manual-docker-certification, manual-release-validation, manual-scale-certification) — staged for future activation; not yet wired to automatic triggers.
- `docs/architecture/roadmaps/Release_Workflows.md` documents the local-first release ownership model and workflow template activation guide.
- Windows release packaging scripts hardened for reliable local/CI builds: resolved WiX tool lookup, WiX 3.x Program Files discovery, explicit MSI failure handling, and local validated release workflow WiX installation.

**Documentation**
- `Docs/Architecture/Lineage.md` (new): what is tracked, `LineageEntry` data model, `SHOW LINEAGE` syntax variants, Mermaid and OpenLineage export, `SHOW LINEAGE HISTORY` cross-run catalog, metadata inheritance rules, and Orchestrator (`etlsql.db`) integration.
- `Docs/Reference/Performance.md` (new): see Observability above.
- `docs/architecture/roadmaps/Release_Workflows.md` (new): see Release Infrastructure above.
- Architecture documentation expanded for connector, engine, expression evaluation, language server, lineage, orchestrator, parser/lexer, portal UI, report portal, reporting, TUI editor, variable scoping, and VS Code extension boundaries.
- `docs/guides/testing.md`, `docs/architecture/roadmaps/Test_Strategy.md`, and `scripts/README.md` reorganized around the current lane model, pre-release phases, SLT usage, coverage expectations, and installer prerequisites.
- Connector standards and reference docs corrected for current connector option naming rules, supported connector inventory, and source-boundary guidance.

**Tests**
- `ResumeEdgeCaseTests.cs` — 5 integration tests covering: fail-fast on IsResuming without checkpoint; fresh-variable guarantee on `--session` without `--resume`; GOTO keyword-target parse diagnostic; SaveSession graceful return for non-Evaluator contexts; mid-script resume uses loaded checkpoint state.
- `ParserErrorQualityTests.cs` — 17 parameterized cases across 4 constructs (GOTO, CREATE CONNECTION, SEND EMAIL, RUN SCRIPT) asserting error messages name the construct and expected token.
- `ExampleOutputCorrectnessTests.cs` — 6 assertion-based tests verifying correct output (row counts, column values, specific cell values) for self-contained scripts in `01_Basics/` and `07_Real_World/`: function library, window deduplication, incremental MERGE, data masking, anti-join reconciliation, and PIVOT.
- `CrossHostConsistencyTests.cs` — verifies that the same `.rptsql` fixture produces identical manifest structure (title, visual count, visual names, row counts, column names) when executed via `DashboardService` directly and via the Portal API execute → snapshot path.
- `MySqlTests.cs` — Docker real-integration tests for the new native MySQL connector.
- ETL scenario golden tests expanded to 27 scenarios covering staged ETL, cleansing, JSON extraction, file round trip, lineage tags/source columns, `WHAT_IF`, loops, `TRY...CATCH`, transactions, DML audit, merge, hash-change detection, set ops, recursive CTE, pivot/unpivot, semi/anti joins, and modular scripts.
- SLT release evidence added for custom ETL-SQL semantics plus the explicit `slt` lane; the release branch SLT lane passed on 2026-06-01.
- Docker-backed integration lane audited and stabilized; the release branch integration lane passed on 2026-06-01 with 97 tests covering connector and platform service boundaries.
- Standard scale certification evidence recorded on 2026-06-01: 13 scenarios passed at 10× row scale.
- Windows package evidence recorded on 2026-06-01: `publish_release.ps1 -Platforms win-x64` produced ZIP/VSIX assets and `build_msi.ps1` produced `ETL-SQL-Enterprise-v0.9.0.msi`.
- UI sandbox and Node smoke coverage added for lineage DAG, designer, script editor, VS Code webviews, datasets admin, and lineage catalog browser-side surfaces.

### Fixed

- **Report export rendering**: PDF chart export now uses the ECharts SSR pipeline so chart visuals render as real chart images; table and filter visual formatting paths were tightened for PDF/Markdown output.
- **VS Code Extension cross-platform hardening**: Added automatic execute permissions setup (`chmod +x`) on Linux/macOS for bundled executables, resolved terminal commands using dynamic shell detection (fixing PowerShell-only `&` operator errors on zsh/bash/cmd), fixed notebook engine lookup in packaged environments, resolved broken welcome links using a GitHub repository fallback in production, added auto-cleanup of temporary scripts, and implemented child spawn error listeners to prevent crashes.
- **`--resume` silently ignored**: passing `--resume` without `--session` would run the full script from the beginning with no warning. Now fails fast with a descriptive error.
- **Stale session state on fresh runs**: `LoadSessionState` fired whenever a `--session` ID was supplied, restoring variables from prior runs even without `--resume`. Now only called when `--resume` is explicitly set.
- **GOTO keyword targets**: the GOTO validation guard used `&&` so keyword tokens (e.g. `SELECT`) passed validation and produced a `GotoStatement` with a keyword target — a silent parse error that deferred to a confusing runtime failure. Targets now restricted to `TokenType.IDENTIFIER`.
- **`SaveSession` ArgumentException on mocks**: `SessionStateManager.SaveSession` hard-cast `IExecutionContext` to `Evaluator` and threw `ArgumentException` for any stub, mock, or sub-evaluator. Now returns early gracefully for non-Evaluator contexts.
- **BigQuery null dereference**: `t.Reference.TableId` in `GetTablesAsync`/`GetViewsAsync` had no null guard; `t.Reference?.TableId` added with a skip on null entries.
- **MySQL double-dispose**: `RollbackAsync` disposed `_transactionalConnection` in its `finally` block then nulled the field; if that `DisposeAsync` threw, the null-assignment was skipped and `DisposeAsync` was called a second time. Connection is now captured locally and nulled before the call in both `CommitAsync` and `RollbackAsync`.
- **Parser error messages**: 12 messages across `DataParser.cs` (CREATE CONNECTION), `ExtensionParser.cs` (SEND EMAIL), and `SystemParser.cs` (RUN SCRIPT) updated to name both the construct and the expected token, matching the quality bar of the core engine.
- **Docker platform service tests**: Report Portal and Orchestrator service Docker tests now build images through a direct `docker build` helper and `.dockerignore` excludes local databases/logs/generated output from build context archives.
- **Windows MSI discovery**: `build_msi.ps1` now detects installed WiX 3.x toolsets under Program Files, including v3.14 installations, before compiling the MSI.

### Security

- **JWT secret hardening**: `JwtSecretValidationService` rejects default or weak JWT secrets at portal startup in production mode.
- **CI workflow hardening**: CODEOWNERS enforces review requirements; Dependabot tracks dependency updates; `sync-assets.js -Check` runs in CI to prevent stale shared report runtime assets from shipping.

---

## [0.8.0] — 2026-05-25

### Added

**Connector Testing & Certification**
- **Connector Certification Matrix**: Formal 4-class certification framework (`MetadataOnly`, `MockedIntegration`, `LocalRealIntegration`, `DockerRealIntegration`) across all 21 connectors. `Connector` and `CertificationClass` traits on every test class enable targeted release gate selection.
- **FTP Docker real-integration**: `delfer/alpine-ftp-server` Testcontainers fixture covering connection, upload/download round-trip, root listing, wrong-password provider-failure wrapping, and `PORT` option handling.
- **REST API real-integration**: Loopback HTTP server tests for PUT and DELETE requests with Basic, Bearer, and API key auth; PUT body verification.
- **Azure Blob (Azurite) integration**: Smoke, upload/list round-trip, download, bad account key, expired SAS token, and host-allowlist enforcement.
- **SMTP (Mailpit) integration**: Docker-backed send-and-verify, multi-row batch, connection-refused and host-allowlist failure paths.
- **BigQuery emulator integration**: `ghcr.io/goccy/bigquery-emulator` Testcontainers coverage for T1 smoke plus T2–T4 unit coverage (invalid credentials, credential masking, host allowlist).
- **Snowflake emulator integration**: Emulator-backed tests plus unit coverage for JWT connection properties, host suffix normalisation, and host-allowlist enforcement. Fixed a `StackOverflowException` in `SnowflakeDataSource.CreateCommand`.
- **Parquet/Avro corrupt-file coverage**: Real-file negative-path reads that verify corrupt provider errors are wrapped as sanitised `ExecutionException`s.
- **Exception wrapping (T4)**: Provider-exception wrapping verified for 11 connectors: ORACLE, ODBC, EXCEL, PARQUET, AVRO, FTP, AZURE_BLOB, API, SMTP, REPORTPORTAL, ORCHESTRATOR.

**`etl-sql doctor` Enhancements**
- `--profile quick|full` — quick profile stays fast; full profile runs report-manifest smoke, PDF export smoke, Graphviz/browser capability checks, and service probes (Report Portal `/health`, Orchestrator `/health`, SMTP, SFTP, Azure Blob).
- `--json` output mode for automation.
- `--strict` flag returns non-zero on warnings.
- Full runtime-path write checks, parser/engine/linter/security/encryption/file/report-asset/Node/portal-DB health probes.

**Scale Certification Harness**
- `scripts/Test-ScaleCertification.ps1` runs smoke/standard/stress tiers with `CERT_ROW_SCALE`-driven row counts.
- Certified scenarios: external sort, aggregate, join, temp-table spill, result cap, window spill, CUBE grouping-set spill, scalar subquery cache, and non-persistent spill cleanup after success and forced failure.
- Each scenario asserts correct row count, `TotalSpilledBytes > 0` for spill paths, tier-derived managed-memory bounds, and cleanup completion.
- `FullyMaterializingDml` warnings for uncapped `MERGE`/`UPDATE`/`DELETE` paths documented with explicit limits.
- 50k-row `CREATE DATASET` Parquet snapshot/reload certified with row count and checksum (`Cert_Smoke_ReportDatasetSnapshotReload_50kRows`).

**Persistent Lineage & Stewardship Catalog**
- `ILineageCatalogStore` interface with `SaveLineageAsync`, `GetHistoryForTableAsync`, `GetHistoryForTagAsync`; implemented in `SQLiteJobHistoryStore` (`LineageHistory` table, auto-migrated).
- New statements: `SHOW LINEAGE HISTORY FOR TABLE <name>` and `SHOW LINEAGE HISTORY FOR TAG <key> [= 'value']`, both supporting `LIMIT` and `INTO #t`.
- Portal Lineage catalog view: target/source/source-file/tag/job queries, column and date filters, tags list, jobs list, source-file links, report links, CSV export, and saved query presets.
- Lineage catalog persistence for portal in-process report executions, bundle publish events, and `CREATE DATASET`/`CREATE VISUAL` runtime events.
- Authenticated portal APIs for table, source, source-file, tag, and job lineage history with report context attached.

**Report Portal Hardening**
- Concurrent snapshot/history/report/list reads during refresh and duplicate-refresh debounce verified by integration test.
- `EXPORT_CSV` and `EXPORT_PDF` audit events added to `ExportController`.
- Read-only report access: snapshot/export allowed, execute/refresh denied, private dataset ACL filtering on dependency and dataset-list endpoints.
- Report history modal updated with dedicated table rendering and horizontal scroll fallback for long hashes.

**Snippet Library Phase 4**
- 13 new built-in snippets covering common connector, lineage, reporting, and scheduling patterns.
- User-defined snippets loaded from disk at startup.
- TUI tab-stop navigation inside snippet placeholders.
- F1 reference integration: snippets surface in `HELP SNIPPETS` and the snippet reference panel.

**Documentation**
- Doc sanity tests: SQL blocks in `Grammar.md`, `Syntax_Index.md`, and all bundled help files parse without syntax errors; help link resolution verified; stale roadmap language guardrail for reference docs.
- Connector Standards doc updated to reflect XML streaming refactor (Rule 7 compliance).
- Scale certification claims page added (`docs/architecture/standards/ScaleCertification.md`).
- SLT corpus coverage documented in `docs/architecture/standards/SLT_Coverage.md`.

### Fixed
- **Snowflake StackOverflow**: `SnowflakeDataSource.CreateCommand` was recursively calling itself; fixed to delegate to the underlying connection.
- **VS Code password prompt**: "requires an interactive console" error when an `ENC:`-protected connection was opened in VS Code; password masking now works via the VS Code input mechanism.
- **Test coverage gate**: Coverage had slipped below 70%; restored to 70.8%+ with T4 exception-wrapping test additions.
- **SLT DML gap**: Added `dml.test`, `insert.test`, and `merge.test` to the SLT corpus; `MergeStatementHandler` was missing from `SltRunner` and is now registered. All 40 SLT files pass.
- **Oracle negative-path coverage**: `gvenzl/oracle-free` Testcontainers fixture extended with missing-table and invalid-SQL failure paths.
- **Azure Blob expired SAS**: `AzureBlobIntegrationTests` now generates and tests an expired account SAS token.

### Changed
- **XML streaming refactor**: XML connector refactored from full-DOM accumulation to streaming `XmlReader`, eliminating full materialisation of large XML files (Rule 7).
- **ODBC/Excel async exceptions**: Accepted exceptions documented with inline comments in `OdbcConnector.cs` and `ExcelDataSource.cs`.
- **`SET SHOW_SECRETS`**: `SET SHOW_PASSWORDS` is now an alias for the preferred `SET SHOW_SECRETS` form.
- **`v0.7.0` baseline notes moved**: Migration Guide updated to reflect 0.8.0 as the current baseline.

---

## [0.7.0] — 2026-05-18

### Added

**Reporting & Interactive Dashboards**
- **Advanced Drill-Down**: Implemented `DRILL_IN` and `DRILL_DOWN` for hierarchical, in-place data exploration; added `DRILL_TO` for cross-report navigation with parameter state passing.
- **Paginated Reports**: Support for `PAGINATED = ON` reports featuring automatic header/footer repetition, multi-page data grid spans, and specialized snapshot formats.
- **ETL Notebooks (`.etlnb`)**: Native VS Code notebook support with cell-based execution, stateful REPL persistence, and cross-cell IntelliSense for connections and variables.
- **Cross-Visual Highlighting**: Power BI-style interactive filtering where clicking a chart segment highlights related data across all other visuals.
- **Ghost Rendering**: Enhanced interaction logic with "ghosting" (dimming) support for Line, Scatter, Pie, and Donut charts during highlighting.
- **New Visual Types**:
    - **MAP**: Integrated ECharts-based mapping with custom GeoJSON support (`MAP_FILE`).
    - **Specialized Charts**: Added `GAUGE`, `BOXPLOT`, `WATERFALL`, `BUBBLE`, `RADAR`, and `CANDLESTICK`.
    - **Input Visuals**: Added `TEXTBOX`, `NUMBERBOX`, and `CHECKBOX` for direct scalar parameter input.
    - **Interactive Slicers**: Support for `SLIDER` and `SEARCH` visual types with immediate dashboard re-rendering.
    - **Interactive Multi-Select**: New `MULTISELECT` visual type rendering as a checkbox list with automatic parameter synchronization.
- **Collapsible Containers**: Support for `COLLAPSABLE = ON`, `ICON`, and pinning logic for overlay drawers and sidebar panels.
- **Deferred Execution**: Added `RUN` button support with staged parameter batching (prevents report refresh on every slicer change).
- **Visibility Engine**: Standardized `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`); added support for dynamic visibility via `@variables`.
- **Enhanced Date Picking**: Native `RELDATEPICKER` (hybrid text + calendar) support.
- **Markdown Tables**: Full support for GFM-style tables in `TEXT` visuals via `marked.js` integration.

**Data, Lineage & Orchestration**
- **Shared Datasets**: Implemented a global dataset registry allowing reports to consume cached, shared data with automated background refreshes and access control.
- **OpenLineage Integration**: Support for exporting data lineage in OpenLineage-compliant JSON format.
- **Lineage 2.0 Engine**: 
    - **Standard Tag Library**: Defined 20 core lineage tags (`@pii`, `@sensitive`, etc.) with `@pii: true-wins` inheritance logic.
    - **Transformation Tracking**: Automated recording of transformation types (`Cast`, `Aggregation`, etc.) across the pipeline.
    - **Visualization**: Enhanced Mermaid-based lineage graphs with distinct shapes for Reports and Datasets.
- **Data Lake Connectors**: Native support for **Snowflake** and **BigQuery**.
- **Batch Separator**: Added `GO` keyword support for separating execution batches.
- **Improved Loops**: `FOR` loops now support implicit start values with `FOR @i TO 10`.
- **QUALIFY Clause**: Added T-SQL/Snowflake-style `QUALIFY` clause for filtering results based on window function values.
- **Window FILTER**: Support for the `FILTER (WHERE ...)` clause inside aggregate window functions.
- **@@FETCH_STATUS**: Added support for checking cursor/foreach fetch status.

**Security & Governance**
- **JWT Secret Generation**: New `GENERATE JWT_SECRET` command for securing report portal communications.
- **Proactive Guardrails**: Linter now warns on high-risk operations and blocks sensitive directory access more aggressively.
- **Decompression**: Added `DECOMPRESS FILE` and `DECOMPRESS DIRECTORY` statements to the specialized operations library.
- **PGP Engine Hardening**: Improved `PGP_KEY_PAIR` generation and validation logic.

**IDE, Tooling & UX**
- **Terminal IDE (TUI) 2.0**: Massive overhaul of the TUI with scrolling, smart copy, message panel optimization, and specialized visual rendering.
- **Unified IntelliSense**: 
    - New dot-aware suggestion engine with priority-based ranking and member-access discovery.
    - LSP support for `@`-prefix tag completions and documentation hovers.
    - Finalized purge of unstable semantic features for improved stability.
- **VS Code Preview**: Support for new chart types (Bubble, Radar, Candlestick, Map) and improved sidebar variable discovery.
- **Report SQL Audit**: Comprehensive rewrite of `Report_SQL_Guide.md` and inline help files to match current production state.
- **Deployment Packaging**: Integrated Windows MSI/ZIP, Linux `.deb`/ZIP, macOS DMG/ZIP, and platform-targeted VSIX generation into the release pipeline.

### Fixed
- **Multi-Select Regression**: Fixed a duplication bug where legacy dropdown logic was overwriting the new checkbox-list implementation.
- **Markdown Rendering**: Resolved issues where Markdown tables were displayed as raw text due to library interface mismatches.
- **IntelliSense Regressions**: Fixed missing connector option suggestions and asterisk expansion failures.
- **Portal State Bugs**: Resolved "white screen" and state synchronization issues in the report portal.
- **Slicer Logic**: Fixed null-reference errors in `renderSlicer` when actions were undefined.
- **Cross-Filesystem Paths**: Fixed portal publish flow failures when handling paths across different drives.
- **Gauge Rendering**: Resolved template string errors and implemented auto-formatting for decimal values.
- **Notebook Reliability**: Fixed "REPL process exited unexpectedly" and communication deadlocks by implementing atomic process lifecycle management and heartbeat checks.
- **Protocol Standardization**: Migrated REPL communication to strict PascalCase JSON with mandatory CRLF endings for Windows pipe stability.

### Changed
- **Sample Reorganization**: Expanded the curated `samples/` library and redirected generated sample outputs under `samples/output/` patterns for repository cleanliness.
- **Visibility Syntax**: Standardized report visibility on the unified `VISIBLE` property.
- **Directory Connections**: Statements like `COPY DIRECTORY` and `FILE_LIST` now natively accept `DIRECTORY` connection aliases as path arguments.

## [Unofficial 0.6.0] — 2026-05-11

### Added

- **Hierarchical Drill-Down and Drill-Through:** Implemented `DRILL_IN` and `DRILL_DOWN` (supporting multi-key drill parameters) for interactive, in-place dashboard exploration.
- **Power BI-style Cross-Visual Highlights:** Added cross-visual highlight filtering with dual-direction updates and dimming/ghosting effects for chart visuals (Line, Scatter, Pie, Donut).
- **Shared Dataset Management:** Built dataset explorer features including persistence, cross-report consumption, access control, LS dataset awareness, and portal-triggered refreshes with async execution.
- **Advanced Parameter & Execution Controls:** Added textbox, numberbox, checkbox scalar inputs, and deferred execution support (RUN button) with staged parameter batching.
- **New Visual Enhancements:** Added collapsible containers, standard `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`), and support for custom GeoJSON maps (`MAP_FILE`) with build-time validation.
- **Interactive Tooling:** Added `serve` command and dynamic `ReportPlayer` lifecycle management for live report previews in-browser.
- **OpenLineage Integration:** Added OpenLineage export support and database catalog metadata imports.

### Changed

- **Sample Reorganization:** Cleaned up and renamed all sample scripts, redirecting outputs to standard `samples/output/` patterns.

### Fixed

- **Portal Reactivity:** Stabilized slicer reactivity, multiselect visual components, and cross-filesystem path handling during portal publishing.

## [Unofficial 0.5.0] — 2026-05-04

### Added

- **Report Portal Subsystem (Phases 1–6):** Introduced the `ETL-SQL.Portal` web application. Features include JWT authentication, role-based access control (RBAC), folder structure organization, report publishing, execution/snapshot tracking, and web-based ECharts/Markdown rendering.
- **Automated Report Subscriptions:** Shipped report subscriptions allowing scheduled report exports via `EXPORT REPORT` sent as Link or Markdown emails, complete with SMTP connection management.
- **Portal Observability & Administration:** Added a `/health` endpoint with JSON diagnostics of database and orchestrator status, audit logs CSV exports, and administrative endpoints.
- **Portal Security Hardening:** Implemented JWT secret validation on startup via hosted service, a path traversal guard, and HSTS security configurations.
- **Apache Arrow Spill Format & Decryption:** Integrated Apache Arrow IPC spill format for high-speed serialized temp table caching, and implemented client-side credential auto-decryption.
- **Unified IntelliSense Engine:** Built a priority-based suggestion ranking, dot-notation autocomplete prefix filtering, dynamic option discovery, and member-access resolution.
- **Data Lake Connectors:** Native support for **Snowflake** and **BigQuery** databases.
- **Security & Encryption:** Added `GENERATE JWT_SECRET` for secure Report Portal communications.
- **Language Syntax Additions:** Implemented `QUALIFY` clause filtering, window function `FILTER (WHERE ...)` support, cursor status checks (`@@FETCH_STATUS`), and `FOR` loop syntax support for implicit start values.
- **TUI IDE Completion:** Overhauled TUI console with path completion, Smart Copy, screen stability, Compare Mode, SHOW commands, and a two-line status bar.
- **Installer & Packaging Release Pipelines:** Integrated MSI, Linux `.deb`, and macOS DMG installer packages with install bootstrap configurations.

### Changed

- **Security Auditing:** Standardized security overrides by migrating legacy comments to formal `SET ALLOW_... ON/OFF` statements.

### Fixed

- **TUI & Telemetry bugs:** Resolved rendering artifacts, status bar layout errors, and stabilized TUI telemetry.
- **LSP Cleanup:** Purged experimental unstable features (Quick Fixes, Smart Rename) for stability.

## [Unofficial 0.4.0] — 2026-04-20

### Added

- **Report-SQL Scripting and `CREATE VISUAL` Support (Phases 9A–9D):** Introduced native support for Report-SQL scripts (`.rptsql`) with `CREATE VISUAL`, `CREATE PAGE`, and `CREATE DATASET` statements. Added full grammar for visual types (BAR, LINE, PIE, SCATTER, TABLE, CARD, SLICER), axes, column mappings, and page slot layout definitions.
- **ReportBuilder Library and CLI Tooling:** Created `ETL-SQL.ReportBuilder` for Chart.js rendering, GFM markdown generation, and snapshot serialization. Shipped the report builder command-line utility with build, refresh, and serve commands.
- **VS Code Extension Preview Integration:** Added a WebviewPanel to the VS Code extension for live report previews, displaying rendered Chart.js charts, tables, cards, and interactive slicers.
- **ReportPlayer Web Dashboard:** Shipped a Kestrel-hosted local dashboard server (`ReportPlayer`) supporting live parameter injection, interactive updates, and auto-refresh endpoints.
- **Orchestration & Scale Hardening:** Implemented job retry logic with exponential backoff and session persistence in the Orchestrator, alongside `#temp` table spill-to-disk and result capping logic.
- **Hyper-scale Window Spilling:** Added deep-spilling mechanism for window query execution to partition results under high-volume workloads.
- **ANSI SQL Functions & Statistical Aggregates:** Implemented standard ANSI string functions (`SUBSTRING`, `POSITION`, `OVERLAY`, `TRIM`, `EXTRACT`), date arithmetic enhancements, and statistical aggregate calculations.
- **Script Assertions:** Added the `ASSERT` statement to natively validate data qualities and script outcomes.
- **JSON & XML Security Hardening:** Replaced bare catch blocks with explicit system exception filters and added security sandbox protections for remote file transfers.
- **LSP & UI Enhancements:** Modernized results panel, TUI performance dashboard, and stabilized telemetry pipelines.
- **PIVOT & UNPIVOT Validation:** Added linter validation for PIVOT columns, quarter-based `DATEPART` support, and query metadata derivations.

### Fixed

- **SMTP Attachment Leak:** Fixed a handle leak for SMTP attachments.
- **3VL Null Handling:** Implemented three-valued logic (3VL) null propagation and fixed substring start index boundary behaviors.

## [Unofficial 0.3.0] — 2026-04-06

### Added

- **VS Code Extension v0.1 Alpha:** Integrated LSP parser with formatting, lineage hover, and smart CLI execution.
- **Security & Encryption Utilities:** Added SSH key pairing (`GENERATE SSH_KEY_PAIR`), connection altering (`ALTER CONNECTION`), and file encryption/decryption (`ENCRYPT FILE`, `DECRYPT FILE`).
- **Serilog Logging Infrastructure:** Integrated Serilog for application-wide logging and consolidated logs to the `logs/` directory.
- **Join Optimization:** Implemented `CompoundKey` to optimize hash joins and handle mixed-type comparisons (string/numeric/date) across diverse sources.
- **Bulk Insert Lineage:** Added explicit column mapping support and column-level lineage tracking.
- **SQL Pushdown:** Enabled SQL pushdown execution and support for standalone `EXECUTE INTO #temp`.
- **Syntax Enhancements:** Supported `LIKE ESCAPE` and grouping sets (`ROLLUP` / `CUBE`).

### Changed

- **Syntax Standardization:** Migrated `ON FILE` to `ON FLATFILE` for file connections.

### Fixed

- **Thread Safety:** Eliminated deadlocks and silent exception swallowing under concurrent execution contexts.

## [Unofficial 0.2.0] — 2026-03-23

### Added

- **Core Query Dialect & Standard Library:** Support for `DISTINCT`, `TOP`, `LIMIT`, `MERGE`, `OFFSET`, `NTILE`, `STRING_AGG`, and transactional statements (`COMMIT`, `ROLLBACK`, `THROW`).
- **Database Connectors:** Added initial support for MSSQL, Postgres, and Oracle database engines.
- **File Connectors:** Read/write capabilities for XML and JSON files.
- **Temp Tables & Indexes:** Support for `#temp` tables with query plan indexes (`CREATE INDEX`) and query plan tracing via `EXPLAIN`.
- **Control Flow & Parallel Execution:** Parallel execution pipelines (`PARALLEL`), cross-script execution (`RUN SCRIPT`), and directory synchronization tasks.
- **Notifications & Transfer Connectors:** Added `SEND EMAIL` and file transfer connectors (SFTP/SSH, FTP, Azure Blob).
- **Linter & UI Foundations:** Added a command-line script editor, local test harness (`--test`), and baseline security linter.


## [Unofficial 0.1.0] — 2026-03-13

### Added

- **Proof of Concept Completed:** Successfully loaded flat files (CSV) and joined them into in-memory `#temp` tables.
- **Abstract Syntax Tree (AST) Parser:** Implemented the initial AST parser to parse SQL statements and evaluate expression trees.
- **Core SQL Execution Engine:** Developed the core engine to execute queries, process DML scripts, and return formatted results.
- **Terminal IDE (TUI) Foundations:** Added a basic console editor interface to write scripts and display execution output.
- **Git Repository Initialized:** Initialized the git repository and established the project structure.
- **Development Kickoff:** Work began on March 6, 2026, to design and prototype the initial engine proof of concept.
