### Added

- Added an online-safe support bundle to the Portal. `GET /api/admin/support-bundle/review` returns every section as a reviewable document — health, deployment identity and versions, migration state, catalog counts, audit-outbox state, and the redacted Portal configuration — together with the redaction note and an explicit list of what it leaves out. `GET /api/admin/support-bundle` downloads it. Both are audited.

  Two properties make it safe to expose: it collects counts, versions and states rather than content — no report data, no dataset rows, no log bodies — and all text passes through the same redactor the CLI bundle uses. Tests assert that a report's name and title, the JWT secret, and the dataset at-rest key are absent from the entire response.

  `?acknowledgedContent=<hash>` refuses with `409` when the disclosure changed after review. The hash covers the deployment and its configuration rather than live counters: reviewing the bundle audits the review, which moves the outbox counts the bundle reports, so hashing everything would make every review stale the instant it was made and the check would degrade into noise an operator learns to bypass.

  The CLI's `etl-sql admin support-bundle` remains the recovery path for when the Portal is unavailable — it reads host files and configuration the Portal cannot.

### Changed

- Moved the support-bundle redaction rules to `ETL_SQL.Core.Common.SupportBundleRedactor`, with the CLI builder now delegating to them. Two hosts producing support material from two nearly-identical rule sets would eventually diverge, and redaction that is *almost* the same in two places is worse than none: it yields two artifacts that look equally safe and are not. Behaviour is unchanged.
