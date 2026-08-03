### Added

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

### Changed

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

### Fixed

- Governance mutations reloaded data without redrawing, leaving the steward looking at the state
  before their change — which reads as the change having failed. Caught by the new browser lane test.
- Five parallel dashboard reads on a cold database raced to create the singleton settings row and
  returned a 500. The unique index makes the race safe: one insert wins and the losers read the
  winner's row. Deliberately not serialised behind a process-local lock — Portal runs multi-node, and
  the other node is not holding your lock.
