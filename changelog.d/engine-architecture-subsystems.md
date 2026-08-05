### Changed

- **`Engine.md` now documents four engine subsystems it had never mentioned** — data-quality rules,
  the columnar plan family, row-level security, and `SECRET:`/organization-policy enforcement. It
  had described the v0.10-era engine accurately and stopped growing with it: 69 mentions of the
  external spill engines, zero of any of these.

  Extended rather than split into new pages. The document is organised by mechanism and these are
  mechanisms; splitting would have put the fast-path *disqualifiers* in a different file from the
  fast paths, which is the exact confusion that prompted the work. The new sections explain how the
  pieces fit and link to `DataQualityRules.md` and `RowLevelSecurity.md` for detail rather than
  restating it.

### Fixed

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
