### Fixed

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

### Added

- `ColumnQualityCostTests` (Performance lane) reports what each `@expect` rule shape costs against
  the same statement with no rules, and what `QUALIFY` costs against the same windowed query
  without it. It reports rather than asserts a budget: the value is knowing which shapes are
  expensive and catching one that gets much worse.
