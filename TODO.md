# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Feature implementation for this sprint has moved to `CHANGELOG.md` and
`docs/releases/v0.17.0.md`. Only release verification remains open here.

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Run the recovery drill and retain the report: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] Run HA failure certification and retain the transcripts: `etl-sql admin ha-soak fault-run` then `etl-sql admin ha-soak validate`.
- [ ] Confirm the documentation boundary guards still pass:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests`.
- [ ] Collect the evidence required by [Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)
      — that document is the authoritative list; the entries above are the commands, not a substitute for it.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.

---

## Post-v0.17.0 Backlog: Developer Productivity & Data Prep Helpers

- [x] **`SAME_PERIOD_LAST_YEAR(d)` & Period-Over-Period Scalar Helpers.**
      Add engine scalar functions `SAME_PERIOD_LAST_YEAR(d)`, `START_OF_MONTH(d)`, `END_OF_MONTH(d)`, `START_OF_QUARTER(d)`, and `START_OF_WEEK(d)` to make period-over-period date arithmetic explicit, readable, and leap-year safe without writing complex `DATEADD`/`DATEDIFF` subqueries.

- [x] **`GENERATE CALENDAR` Table Generator Statement.**
      Add statement syntax `GENERATE CALENDAR FROM @StartDate TO @EndDate INTO #calendar` that generates a standard Date/Calendar dimension `#temp` table in memory (`DateKey`, `Date`, `Year`, `Quarter`, `MonthName`, `DayOfWeek`, `IsWeekend`, `FiscalYear`).
      Clarification: The output MUST be a standard inspectable `#temp` table so users can run `SELECT * FROM #calendar` and perform explicit outer joins.

- [x] **`FILL_DATES` Time-Series Gap Filler.**
      Add engine helper `FILL_DATES(#temp_sales, DATE_COL = 'OrderDate', GAPS_FILL = 0, BY_GROUP = 'Region')` that fills missing dates in time-series datasets to prevent visual chart distortions when days have zero activity. Output is saved to an inspectable `#temp` table.

- [x] **`SAFE_DIVIDE(num, den, fallback)` & `CLEAN_STRING(text)` Utility Functions.**
      Add `SAFE_DIVIDE(a, b, [fallback=0])` to eliminate repetitive `CASE WHEN b = 0 THEN 0 ELSE a/b END` blocks, and `CLEAN_STRING(text)` to trim whitespace, strip control characters, and collapse double spaces in dirty file feeds.

- [x] **Standard PII Masking Helpers (`MASK_EMAIL`, `MASK_PHONE`, `MASK_SSN`).**
      Add built-in governance scalar functions `MASK_EMAIL(e)` (`j***n@example.com`), `MASK_PHONE(p)` (`***-***-1234`), and `MASK_SSN(s)` (`***-**-6789`) for one-liner data sanitization when staging public or low-privilege report datasets.

- [x] **`AGE_BUCKET(days)` & `VALUE_BUCKET(val, [ranges], [labels])` Data Binning.**
      Add `AGE_BUCKET(days)` (e.g. `'0-30'`, `'31-60'`, `'61-90'`, `'90+'`) and `VALUE_BUCKET(val, [0, 50, 100, 500], ['Low', 'Med', 'High'])` to eliminate repetitive 10-line `CASE WHEN` statements in financial aging and metric binning reports.

- [x] **`COMPARE DATASETS` Change Data Capture / Table Delta Statement.**
      Add statement syntax `COMPARE DATASETS #today WITH #yesterday KEY (Id) [EXCLUDE (col1, col2)] INTO #diff` that outputs an inspectable table with `_change_type` (`'INSERT'`, `'UPDATE'`, `'DELETE'`, `'UNCHANGED'`), `_changed_columns`, and old/new attribute deltas for fast ETL pipeline reconciliation.
      Clarification before implementation: large-scale datasets (10M+ rows) must not cause OutOfMemoryExceptions. Implementation must use a 64-bit row hash index (`XXHash64`) for in-memory matching (~24 bytes/row), automatic disk-backed sorted stream merge when memory thresholds (`Engine:MemorySpillThresholdMb`) are exceeded, 10,000-row streaming output chunks to `#diff`, and native SQL `EXCEPT`/`JOIN` pushdown when both datasets originate from the same remote SQL connection.

