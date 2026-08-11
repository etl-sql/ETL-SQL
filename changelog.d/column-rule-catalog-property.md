### Added

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
