### Added

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

### Fixed

- **The governance dashboard's KPI tiles were unreadable to a screen reader.** Five tiles rendered
  as sibling `div`s collapsed into one undifferentiated run of text: *"0/0 Governed assets 0% at or
  above 80 0 Below threshold Need follow-up…"*, with no number attached to any label. Each tile is
  now a list item carrying its whole meaning in an accessible name.

- **The governance state banners were anonymous bold runs**, so a user navigating by heading could
  not find the most important sentence on the page — that the estate has never been scanned, or that
  they are looking at a denial rather than an empty estate. They are now headings.

  Both were found by the new snapshots on their first run, on code added earlier in this release.
