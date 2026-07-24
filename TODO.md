# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Feature implementation for this sprint has moved to `CHANGELOG.md` and
`docs/releases/v0.17.0.md`. Only release verification remains open here.

- [x] Redesign Portal Governance into a data-steward-first dashboard.
      See [Governance_Dashboard_Strategy.md](docs/architecture/roadmaps/Governance_Dashboard_Strategy.md).

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

### Parser / Tag Pipeline

- [ ] Make comment-tag splitting quote-aware in `Parser.ParseMetadataTags` (`src/ETL-SQL.Core/Parser/Parser.cs:1865`).
      Today `tagContent.Split(';')` splits on **every** `;`, so a `;` inside a quoted tag value breaks parsing,
      and a comma between tags silently swallows the following `@tag` into the previous value. Replace the naive
      split with a small top-level scanner that tracks `'`/`"` quote state and only splits on `;` outside quotes.
      Prerequisite for the Data Quality `@expect`/`@on_fail` rule tags — values contain commas, parens, operators,
      and (for `MATCHES`) regexes. Rule: values are quoted; `;`/`,`/`@` inside quotes are literal; **no** backslash
      escaping (it collides with `MATCHES` regexes); a same-kind literal quote is doubled (SQL `''`). See the tag
      value grammar note in [DataQualityRules.md](docs/architecture/decisions/DataQualityRules.md).

