### Fixed

- **`PARTITION BY` returned bucket-wide window values once a partition spilled.** The external
  window engine hash-partitions rows into buckets, and its partition-replay path scanned a whole
  bucket once and wrote that single aggregate onto every row in it. That is sound only when a bucket
  *is* the logical partition. Buckets are hash partitions, so with a `PARTITION BY` of higher
  cardinality than the bucket count — the ordinary case — one bucket holds many partitions and every
  row received the bucket's aggregate instead of its own.

  `SELECT COUNT(*) OVER (PARTITION BY customer_id)` over a large table therefore returned silently
  wrong numbers: no error, no warning. Reached whenever a bucket exceeded `WindowSpillThreshold`
  (default 10,000 rows) with `COUNT`/`SUM`/`MIN`/`MAX`/`AVG` and no `ORDER BY` or frame.

  Both scan passes now fold one accumulator set per partition key, and the replay pass looks up each
  row's own key. The columnar fast path builds the key from batch ordinals and declines — falling
  back to the row scan — when a partition expression is not a plain column it carries, so the
  optimization is kept rather than traded away for correctness.

  Keys are rendered to text through one shared helper, because the scan may read a value from a
  column batch while the replay reads it from a materialized row: the same column can come back as
  `long` one way and `int` the other, and boxed equality would then file one partition under two
  keys — reintroducing the defect in a subtler form.
