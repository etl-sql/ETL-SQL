# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Feature implementation for this sprint has moved to `CHANGELOG.md` and
`docs/releases/v0.17.0.md`. Only release verification remains open here.

- [x] Redesign Portal Governance into a data-steward-first dashboard.
      See [Governance_Dashboard_Strategy.md](docs/architecture/roadmaps/Governance_Dashboard_Strategy.md).

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [x] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Run the recovery drill and retain the report: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] Run HA failure certification and retain the transcripts: `etl-sql admin ha-soak fault-run` then `etl-sql admin ha-soak validate`.
- [ ] Confirm the documentation boundary guards still pass:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests`.
- [ ] Collect the evidence required by [Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)
      — that document is the authoritative list; the entries above are the commands, not a substitute for it.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

### Parser / Tag Pipeline

- [x] Make comment-tag splitting quote-aware in `Parser.ParseMetadataTags` (`src/ETL-SQL.Core/Parser/Parser.cs:1865`).
      Today `tagContent.Split(';')` splits on **every** `;`, so a `;` inside a quoted tag value breaks parsing,
      and a comma between tags silently swallows the following `@tag` into the previous value. Replace the naive
      split with a small top-level scanner that tracks `'`/`"` quote state and only splits on `;` outside quotes.
      Prerequisite for the Data Quality `@expect`/`@on_fail` rule tags — values contain commas, parens, operators,
      and (for `MATCHES`) regexes. Rule: values are quoted; `;`/`,`/`@` inside quotes are literal; **no** backslash
      escaping (it collides with `MATCHES` regexes); a same-kind literal quote is doubled (SQL `''`). See the tag
      value grammar note in [DataQualityRules.md](docs/architecture/decisions/DataQualityRules.md).

### Data Quality V3 — Join Replay

- [x] Promote the v3 join replay direction in `docs/architecture/decisions/DataQualityRules.md`
      into a full design section with implementation slices, manifest fields, replay decision tree,
      and documentation requirements.
- [x] Extend `QuarantineReplayManifest` with backward-compatible replay-mode/provenance fields:
      single-table vs probe-side join replay, probe source table, join table, observed N:1 status,
      and join replay non-replayable reason.
- [x] Capture probe-side provenance through the streaming hash-join path so quarantined join output
      can persist the original probe row rather than the combined post-join row.
- [x] Add the observed N:1 gate by detecting build-side duplicate keys during hash table build and
      recording replayability in the manifest.
- [x] Extend `REPLAY QUARANTINE` to substitute released rows at the probe source for replayable
      N:1 joins while keeping existing lease and disposition semantics.
- [x] Update docs/help/LSP surfaces for `REPLAY QUARANTINE`, data-quality remediation, and the
      fan-out non-replayable diagnostic.

### Data Quality — Steward Gaps

Gaps found by walking the shipped system as a data steward would use it. The first is done; the
rest are ordered by how much they affect day-to-day stewardship.

- [x] **No read surface for the persisted per-run metrics.** The engine had been recording
      `RowsQuarantined` / `RowsWarned` / `DataQualityFailures` per run since v1, and nothing in the
      Portal read any of it — the stated purpose of persisting them (trend visibility) was never
      delivered. Added `GET /api/data-quality/trend` plus a trend panel on the quarantine queue:
      per-run rates, a sparkline, the rules firing most, and the latest run compared against the
      mean of the preceding ones.
- [x] **Rules are invisible without reading the script.** Added `SHOW DATA QUALITY RULES
      [FOR [TABLE] t] [COLUMN c] [INTO #t]`, one row per individual rule with its bound action.
- [x] **Disposition records neither actor nor reason.** Release, discard, and replay now write an
      audit row with the acting user, the rows affected, and an optional steward-supplied reason
      (surfaced as "Reason (audited)" in the row editor). Deliberately not a `__dq_*` column: the
      capture schema freezes on first write, and an audit row cannot be edited by the person being
      audited.
- [x] **Alerts do not reach the column's owner.** `ASSERT JOB … ALERT` payloads now carry `Owners`
      and `FailingColumns`, resolved from `@steward`/`@owner`/`@contact`, with owners named inline
      in the alert text. Still counts and column names only — never sample values.
- [x] **No way to preview a rule's impact before enforcing it.** Added
      `SET DATA_QUALITY_DRY_RUN = ON`: rules are evaluated and counted, nothing is diverted,
      written, or thrown. Affected rows tally separately as `RowsDryRunAffected` so a dry run
      cannot be mistaken for enforcement.
- [ ] **The row editor only works for Portal-resolvable targets.** Pre-projection capture plus
      in-Portal editing is the strongest part of the workflow, but it is unavailable for durable
      quarantine tables on named production connections — which is where real quarantine data
      lives. Decide whether Portal should be able to reach those targets, or whether the queue
      should state plainly that a target is view-only and why.

### Data Quality — Residual Gaps From Code Review

Two gaps left open deliberately after the v2 review fixes (commit `168b70a5`). Both are decisions
about intended semantics rather than defects, so they are recorded for review rather than patched.

- [ ] **Column-list INSERT guard does not cover set-based copies.**
      `InsertStatementHandler.GuardDataQualityEvidenceInsert` returns early when `stmt.Columns` is
      null, so it blocks `INSERT INTO q (__dq_status, …) VALUES (…)` but not
      `INSERT INTO q SELECT * FROM other_quarantine`. The second form can still land a row carrying
      `__dq_status = 'released'` in a quarantine target, which `REPLAY QUARANTINE` would then inject
      into the production target as if it had been validated and remediated.
      Deciding factor: catching it needs the engine to know the destination *is* a quarantine
      target (e.g. probing the target schema for `__dq_status`, or consulting the replay manifest)
      and then to inspect projected column names — which also risks rejecting legitimate
      table-to-table copies of capture data, such as archiving a quarantine table.
      Review question: is copying evidence between tables a supported workflow? If yes, guard on
      the *destination being a live replay target* rather than on the shape of the INSERT.

- [ ] **Retention prunes a shared durable capture target as a whole.**
      `SqliteDataSource.PruneDataQualityRowsAsync` (and the in-memory path in `QuarantineWriter`)
      delete every row past the cutoff except `released` ones, with no scoping to the job or run
      that wrote them. Two jobs quarantining into the same durable table therefore share whichever
      `WITH (RETENTION = …)` window is shortest, and one job's window silently prunes the other's
      evidence.
      `__dq_run_id` is already written on every captured row (`QuarantineWriter.cs:58`), so
      run-scoped or job-scoped pruning is mechanically straightforward.
      Review question: should `RETENTION` mean "this statement's captured rows" or "this table"?
      Scoping it per writer is safer but changes the meaning of the clause and would leave rows
      from a removed job un-pruned forever, so it needs a deliberate call plus a documentation
      change either way.

