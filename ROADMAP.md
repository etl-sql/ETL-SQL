# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next
actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md` and the release notes
under `docs/releases/`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

### Data Quality v2

v1 (column `@expect`/`@fail` rules, quarantine/warn capture, `ASSERT JOB`, the `WEBHOOK` connector)
shipped in v0.17.0. The v2 metric-depth, alert-quality, and quarantine-remediation slices are
implemented; demand-triggered hardening work is tracked here. The full design, including the
as-built deviations v1 left behind, lives in
[`docs/architecture/decisions/DataQualityRules.md`](docs/architecture/decisions/DataQualityRules.md).

Recommended order (rationale in the design doc's "v2 sequencing" section):

1. **Metric depth** — shipped: `NULL_PERCENT ... OF HISTORICAL` (with a per-column run-metrics table),
   qualified `NULL_PERCENT(target.col)`, a `FRESHNESS(col) < interval` predicate, and
   `WITHIN n SIGMA OF HISTORICAL`.
2. **Alert quality** — shipped: transition-based alerting and recovery notifications, so a
   nightly-failing job cannot train people to mute the channel.
3. **Quarantine remediation** — shipped. The orchestrator manifest foundation is implemented:
   quarantining jobs persist the job/script/section/source/target replay metadata and mark joins
   non-replayable. `UPDATE` now enforces the quarantine disposition lifecycle. `REPLAY QUARANTINE`
   resolves manifests, substitutes released quarantine rows back into the recorded source table, and
   resumes the recorded section label, then flips consumed rows to `replayed` after success.
   Replay is fenced through the orchestrator cluster-lock store. The first Portal steward queue now
   lists replay manifests, replayability state, and can submit replay jobs through the configured
   Orchestrator job channel. The row editor for source-column fixes, release, and discard actions is
   built, but the Portal cannot currently read any real capture target, so the queue marks those
   targets view-only — see *Portal — Quarantine Row Access* below.
4. **Scale hardening** — spill-aware UNIQUE key map and single-pass UNIQUE batching shipped.
   Connector-side retention now has the opt-in data-source capability and SQLite support; additional
   durable connectors remain demand-triggered. Each has a recorded trigger in the design doc.
5. **Governance dashboard integration** — mostly delivered. The Portal surfaces a per-job quality
   trend (`GET /api/data-quality/trend` plus the trend panel on the quarantine queue), dispositions
   record actor and reason, alerts carry column owners, and `SET DATA_QUALITY_DRY_RUN` previews a
   rule's impact before enforcing it. Rule visibility shipped as the `SHOW DATA QUALITY RULES`
   statement only — there is no Portal surface for it, which is carried below.

v3 direction (join-statement replay via probe-side provenance) and nested-script replay are recorded
in the same document as directions only.

---

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

**Shipped in v0.17.0 (the honest interim).** `QuarantineTargetReadability` classifies each target;
the queue marks unreadable targets **View only** with the specific reason and offers the review
statement to run where the connection exists; `GET quarantine/rows` declines with `409` and that
reason instead of executing and returning a raw engine diagnostic as a `502`. Replay, disposition,
and trend are unaffected — those run as orchestrator jobs, which do have the connections.

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
   `quarantine/rows` test asserts a rejection, which is precisely why a non-functional row editor
   shipped as complete. Then: catalog miss, disabled entry, feature switch off, unauthorized
   identity, and a redaction assertion on the failure path.
6. **Docs + sandbox.** Administration guide: which connections become previewable, what the switch
   does, and what is audited. Flip the sandbox's view-only fixture to a readable catalog-backed
   target so both states stay developable
   (`tools/ui-sandbox/stories/data-quality-queue.story.js`).

Open decision for the sprint: whether a steward reviewing rows through a catalog connection should
be limited to connections their own role can already reach, or whether `DataQualityStewardAccess`
plus a manifest-bound target is authority enough. This changes slice 2's authorization check.

### Portal — Other Known Gaps

Collected while walking the Portal as a steward would use it. Ordered by how much each affects
day-to-day use.

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
4. **Preview reads are not audited.** Dispositions record actor, rows, and reason; reading the raw
   quarantined source rows records nothing. Folded into the quarantine-row-access slice above, but
   it applies to any Portal endpoint that executes engine scripts.
5. **Every preview spins a full engine.** Each request lexes, parses, lints, and evaluates through a
   new `ExecutionSession`. Acceptable at current volume; worth revisiting before any endpoint like
   this becomes a polled or dashboard-refreshed surface.
6. **Portal UI work needs sandbox stories first.** The stories under `tools/ui-sandbox/stories/`
   cover the portal surfaces unevenly and the README index had drifted from the registry. Treat
   `stories/index.js` as the checklist when picking up Portal UI work, and add the story before the
   change — the view-only treatment above was designed and verified there without a portal build.
