### Added

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

### Fixed

- **A composite rule naming a column the statement does not project now fails instead of passing.**
  Row lookup by name yields NULL for an absent column and a NULL key part skips the rule, so a
  single typo in `UNIQUE WITH (TenantId, BokingRef)` produced a rule that reported clean because it
  never ran on any row. Both `UNIQUE WITH` and the new `EXISTS WITH` now reject an unprojected
  column at statement start, naming the column.

- **`EXISTS IN` probed its reference key set with a linear scan.** The set was built with the right
  comparer but queried through `Enumerable.Contains` with an explicit comparer, which bypasses the
  `HashSet` and walks every key — making a dimension lookup O(rows x keys) per statement. It now
  probes the set's own comparer.
