# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`Docs/Operations/v0.15.0-flaky-tests.md` and `Docs/Operations/v0.15.0-performance-results.md`.

### Restore the 70% coverage gate

`ci.yml`'s threshold was lowered **70.0 -> 69.5** to ship v0.15.0 (landed at 69.8%). Analysis
from 2026-07-13 found that the v0.15.0 headline feature (`Core.Adaptive.*`) is already well-covered;
the remaining gap is infrastructure coverage.

- [ ] `App.*` runners (`WarmJobRunner`, `EnterpriseEnrollmentManager`, `DatabaseMigrationService`) are
      the biggest untested chunk but hardcode elevation checks, stores, and file I/O. Meaningful tests
      need a testability seam first, not error-path-only tests.
- [ ] Iterate CI-in-the-loop: add tests, push, read the CI coverage percentage (the authoritative
      scope; a local run excluding Portal reports around 50%, not comparable), repeat until >= 70.0,
      then restore the `ci.yml` threshold to **70.0**.

---

## Active Development

### Schema-Resilient Flat File Modes
- [x] Support `IGNORE_EXTRA_COLUMNS = ON`, `NULL_MISSING_COLUMNS = ON`, and `MAP_BY_HEADER_NAME = ON` in `FLATFILE` (CSV) connector.
- [x] Support the same schema resilience options in `EXCEL` connector.
- [x] Pass destination table columns as `templateSchema` in `BULK INSERT` statement execution.
- [x] Add unit tests in `FlatFileTests.cs` and `ExcelTests.cs` to verify positional mapping, header name mapping, extra columns ignoring, and missing columns nulling.

