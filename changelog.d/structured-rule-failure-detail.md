### Fixed

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

### Added

- A sandbox fixture covering the case that motivated the change: two structured rows differing only
  in target table, plus one counts-only row. Reproducible without a Portal, a database or a login.
