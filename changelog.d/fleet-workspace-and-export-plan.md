### Added

- Added the read-only Fleet/Operations workspace at `GET /api/fleet/workspace`: every configured environment polled at once, merged into one report with compatibility metadata, policy/configuration/version divergence findings, migration state, grouping and filtering, plus an upgrade preflight or postflight report. The aggregation had been built but had nothing to aggregate — no configuration named the environments, so it was machinery with no way in. `Portal:Fleet:Environments` is that way in.

  Naming an environment grants visibility, never authority: the workspace issues one scoped read-only `GET /api/fleet/status` per environment and nothing else, and a departmental deployment is not administered from another one's Portal. Per-environment tokens are never echoed, only counted. An unreachable environment is reported as unreachable rather than failing the whole view, because a partial outage is exactly when the view is needed.

- Added a guided configuration export workflow. `GET /api/admin/configuration/export/plan` returns what leaves this Portal, what will not, and what must be moved separately, without the script body — the export endpoint already computed all of that and wrote it only to the audit line, so the only way to learn what an export omitted was to read the file.

  `POST /api/admin/configuration/validate` now returns a per-resource plan of `Create`, `Match`, or `Collision` alongside its findings. Findings carry only collisions, because that is what needs a decision; a plan needs the whole picture, or an operator cannot tell an empty target from an identical one.

  Approval is enforceable rather than advisory: passing `?acknowledgedPlan=<hash>` to the export refuses with `409` when the configuration changed after the plan was reviewed. The hash is derived from the plan contents rather than the script text, so cosmetic churn does not invalidate a review while a real change to what would be promoted always does. The audit records the acknowledged plan, or that none was.
