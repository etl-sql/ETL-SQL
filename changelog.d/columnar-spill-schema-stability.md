### Fixed

- **A spilling query could fail when a column's CLR type varied between batches.** Engine rows are
  dynamically typed, so the same column can hold a `DateTime` in one batch and the same instant as a
  formatted string in the next — or be entirely NULL in one batch, leaving no type evidence at all.
  `ColumnBatchAdapter` inferred each batch's types independently from that batch's own values, while
  the columnar spill writer locks its Arrow schema on the first batch and rejects every later one
  that disagrees. The result was `Column batch field N ('JoinDate', utf8) does not match spill field
  'JoinDate' (timestamp)` partway through a large write.

  Both spilling paths now establish the logical schema once for the whole relation and build every
  later batch against it. Fixes the `flatfile_sink` and `window_sink` samples.

### Added

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
