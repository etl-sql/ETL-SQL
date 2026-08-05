### Added

- **Quarantine preview session startup is now a measured number rather than an intuition.**
  `QuarantinePreviewStartupMeasurement` times the per-request `ExecutionSession` that
  `GET /api/data-quality/quarantine/rows` builds: **~0.8 ms median, ~1.2 ms p95**, stable to 0.1 ms
  across three consecutive runs.

  The number is scoped narrowly on purpose — construct, execute, dispose, excluding the quarantine
  target's own connector read, because that read is what a preview mostly costs and is not what a
  reusable session would change.

  It reports rather than gates. Scale certification on this repository has produced a 56% spread
  between warmed and cold measurements of the same commit, wide enough to swamp any threshold worth
  setting, so the harness asserts only an order-of-magnitude structural ceiling and writes the real
  figure into the decision record.

### Changed

- **The reusable read-only preview path is deliberately not built.** The threshold for revisiting it
  is a 250 ms median or 500 ms p95 — where per-poll overhead becomes a visible fraction of a
  one-second poll interval. The measurement is roughly 300× under that, so the optimisation would
  buy about a millisecond per request while requiring the parsing, linting, policy, RLS, timeout,
  row-cap and redaction guarantees to be re-established across a shared session. Those guarantees
  are the whole reason the preview may read raw quarantined rows at all.

  Polling and dashboard refresh are therefore not blocked by session cost. Recorded with its trigger
  in `DataQualityRules.md` alongside the other demand-triggered scale items.
