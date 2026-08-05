### Changed

- **Every guide now says who it is for.** A one-line `> **Applies to:**` banner names the deployment
  profiles the guide covers. Deliberately a line rather than the four-row table the administration
  docs use: most guides describe the *language*, which is identical from a workstation to SaaS, and
  a four-row table repeating "same for all profiles" would bury the two guides where it genuinely
  differs — `portal-user.md` and `catalog-search.md` need a Portal, and each now names the Solo
  alternative instead of leaving a workstation reader stuck.

- **`data-stewardship-impact.md` no longer requires a Portal it does not need.** Its prerequisites
  said "Portal or Orchestrator must persist lineage", which reads as *deploy a service first*. The
  CLI writes lineage on its own during a plain `etl-sql run` — measured, not assumed: running
  `protected_data_audit.rptsql` through the CLI returns 177 rows on a workstation with no service
  anywhere. Corrected, and the one genuinely Portal-dependent bullet now says so.

- **Nine guides carried orphaned section numbering** from the same manual split as the
  administration docs — `sample-guide.md` alone had 39 numbered headings. Removed, along with a
  duplicate `# VS Code Extension` / `## VS Code Extension` pair.

### Fixed

- **The README generator published non-prose as page descriptions.** It skipped `/*` and `//` but
  not HTML comments, headings, blockquotes or code fences, so two guides described themselves as
  `<!-- SearchPortalCatalogStatement -->` (an AST-name marker), several as their first section
  heading, and one as `ETL-SQL run nightly_load.etlsql --log` — the first line inside a code block.
  It now skips all of those and tracks fences, which fixes the generated indexes across the whole
  of `docs/`, not just guides.
