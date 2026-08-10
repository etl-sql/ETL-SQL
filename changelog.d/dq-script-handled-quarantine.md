### Added

- **`ON FAILURE QUARANTINE TO <table> WITH (HANDLING = SCRIPT)`** — quarantine for rows the running
  script remediates, reroutes, or discards itself. The rows still leave the main output and still
  carry their `__dq_*` context, so a later statement in the same run can read the capture table and
  act on each cause differently; per-run quality metrics still record the counts.

  What the mode removes is the hand-off. No replay manifest is written, so the target does not
  become a Portal steward-queue item and `REPLAY QUARANTINE` cannot target it; no enclosing section
  label is required, and the linter stops recommending a durable target. Those three requirements
  all exist to serve remediation *after* the run — asking someone to remediate rows the script
  already fixed is worse than not asking.

  `HANDLING = STEWARD` states the existing behavior explicitly; omitting `HANDLING` keeps it.
  `HANDLING` on a non-QUARANTINE clause is a syntax error, since `WARN` diverts no rows.

  `ON FAILURE` `WITH (...)` now takes several comma-separated options, so `RETENTION` and `HANDLING`
  can appear together.

### Fixed

- **The formatter silently dropped `ON FAILURE` clauses.** `ToSql()` on a quarantining SELECT
  returned a statement whose `@fail: 'QUARANTINE'` tags routed nowhere, which is a hard error on the
  next run — the mirror image of the comment-stripping failure the symmetric clause/rule check
  exists to catch, and just as quiet at the point where it happened. A round-trip test now covers
  all three clause forms and both `WITH` options.
