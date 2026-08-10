### Fixed

- **A `TIME` column aborted a spilling query.** `ColumnBatchAdapter.GetPhysicalType` maps `TIME` to
  `TimeSpan`, but the columnar spill writer had no case for it and failed the whole write with
  `Native spill writing does not support 'TimeSpan' columns` — the same shape as the UUID gap fixed
  earlier. Spans are stored round-trip as text rather than an Arrow time type, because a `TimeSpan`
  may be negative or exceed 24 hours and Arrow's time types model a time of day. The row-based path
  also labelled spans `Json`, so they came back as bare strings; they now carry `TIME` and restore
  as spans. Fixes the `golden_workflow.rptsql` sample.

### Added

- `EveryPhysicalTypeTheAdapterCanProduce_HasAColumnBuffer` enumerates the logical-to-physical type
  map and pushes a value of each through the adapter, so a type added to one side cannot go missing
  from the other — which is how both this gap and the UUID one arose. Companion round-trip coverage
  pins negative and over-24-hour spans specifically, since those are the reason the encoding is text.
