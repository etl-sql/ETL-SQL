# Testing

For the overall lane model and cleanup guidance, see [Test_Strategy.md](Strategy/Test_Strategy.md).

## Current Release-Confidence Status

The testing foundation now has three complementary layers:

| Layer | What it proves | Current status |
| :--- | :--- | :--- |
| Unit / functional xUnit tests | Parser, evaluator, handlers, functions, security rules, reporting, portal APIs, language tooling | Broad coverage. Use `fast` as the default confidence lane. |
| ETL scenario golden tests | Cross-feature ETL-SQL workflows that are easy to miss with isolated tests | 27 scenarios currently cover staged ETL, cleansing, JSON extraction, file round trip, lineage tags/source columns, `WHAT_IF`, loops, `TRY...CATCH`, transactions, DML audit, merge, hash-change detection, set ops, recursive CTE, pivot/unpivot, semi/anti joins, and modular scripts. |
| SQL Logic Tests | SQL compatibility semantics: SELECT, joins, NULLs, aggregates, DML, set ops, windows, type coercion | Full SLT corpus is explicit/deployment-only. Custom ETL-SQL SLT files cover function and DML areas that SQLite SLT does not. |

Recent focused checks:

```powershell
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter "FullyQualifiedName~EtlScenarioGoldenTests" --no-restore
```

Result on 2026-06-01: 27 passed, 0 failed, 0 skipped.

```powershell
.\scripts\test-lane.ps1 -Lane perf -NoRestore
```

Result on 2026-06-01: engine performance tests 44 passed; dedicated perf project 5 passed.

The integration-folder audit is complete as of 2026-06-01. Metadata-only connector tests, UI/editor tests, local file/format tests, and local engine/orchestration/security tests have been moved back into fast-covered folders. `Get-TestLaneInventory.ps1` now reports 0 test methods that are excluded from `fast`/`engine` by name but not selected by a targeted lane.

```powershell
.\scripts\test-lane.ps1 -Lane fast -NoRestore
```

Result on 2026-06-01: engine test project 3,015 passed; language server 71 passed; report portal 70 passed; lineage UI smoke passed.

## Testing-Foundation Maintenance

Keep this list small and actionable. The one-time lane/scenario/SLT cleanup is complete as of 2026-06-01. When adding a new release claim, add evidence in one of the existing layers instead of creating a fourth testing style.

| Priority | Item | Why it matters | Preferred evidence |
| :---: | :--- | :--- | :--- |
| P0 | Keep `Test-PreRelease.ps1 -Explain`, this document, `Docs/Strategy/Test_Strategy.md`, and `scripts/README.md` aligned. | Release validation is only useful if the documented plan and actual script agree. | Script output checked against docs; update all three docs when phases change. |
| P1 | Add ETL scenario tests only for uncovered release claims, not for every unit-testable branch. | Scenarios should protect workflows and product claims, not duplicate isolated handler tests. Current release matrix audit found no uncovered ETL orchestration claims. | New `tests\etl_scenarios\<name>\script.etlsql` + `expected.json`. |
| P1 | Expand custom SLT only when SQL semantics change or a SQL feature has low/medium confidence in `Docs/Standards/SLT_Coverage.md`. | SLT is the best evidence for SQL correctness but should remain intentional because full runs are slow. | New/updated `tests\slt_data\*.test` plus `Test-SltCorpus.ps1` result. |
| P2 | Keep the compact lane inventory report useful as the suite evolves. | Helps a solo maintainer see what `fast`, `smoke`, `integration`, `slt`, and `release` actually cover without reverse-engineering filters. | `.\scripts\Get-TestLaneInventory.ps1` output reviewed when lanes or test categories change. |

## Smoke Lanes

Use `scripts/test-smoke.ps1` for fast confidence checks before running the full suite.

```powershell
.\scripts\test-smoke.ps1 -Lane all
.\scripts\test-smoke.ps1 -Lane core
.\scripts\test-smoke.ps1 -Lane security
.\scripts\test-smoke.ps1 -Lane reporting
.\scripts\test-smoke.ps1 -Lane portal
```

The lanes use xUnit traits:

| Lane | Filter | Project |
| :--- | :--- | :--- |
| Core language behavior | `Category=Smoke.Core` | `tests\ETL-SQL.Tests` |
| Security and path guardrails | `Category=Smoke.Security` | `tests\ETL-SQL.Tests`, selected portal path checks |
| Reporting manifest/runtime behavior | `Category=Smoke.Reporting` | `tests\ETL-SQL.Tests` |
| Report Portal publish/execute/snapshot basics | `Category=Smoke.Portal` | `tests\ETL-SQL.ReportPortal.Tests` |

Each lane should stay small enough for quick local runs. Keep the full suite as the release and CI validation path.

## General Lanes

Use `scripts/test-lane.ps1` when you want a named suite rather than only smoke tests.

```powershell
.\scripts\test-lane.ps1 -Lane fast
.\scripts\test-lane.ps1 -Lane engine
.\scripts\test-lane.ps1 -Lane portal
.\scripts\test-lane.ps1 -Lane integration
.\scripts\test-lane.ps1 -Lane perf
.\scripts\test-lane.ps1 -Lane release
.\scripts\test-lane.ps1 -Lane full
.\scripts\test-lane.ps1 -Lane benchmarks
.\scripts\test-lane.ps1 -Lane slt        # deployment-only
```

Lane intent:

| Lane | Scope |
| :--- | :--- |
| `smoke` | Hand-picked core/security/reporting/portal smoke tests |
| `fast` | Engine, Language Server, and Report Portal tests excluding explicit integration/performance/scale categories |
| `engine` | `ETL-SQL.Tests` only, with the fast filter |
| `portal` | Report Portal tests only |
| `integration` | External-boundary tests tagged `Category=Integration` |
| `perf` | Performance tests tagged `Category=Performance` in `tests\ETL-SQL.Tests` and `tests\ETL-SQL.PerfTests` |
| `release` | Smoke + fast + SLT, without benchmarks or installer packaging |
| `full` | Normal xUnit projects, excluding deployment-only SLT and benchmark executable |
| `benchmarks` | BenchmarkDotNet executable |
| `slt` | SQL Logic Test corpus with `ETL_SQL_RUN_SLT=1` set by the lane script |

To inspect lane organization without running the suite, generate a static inventory:

```powershell
.\scripts\Get-TestLaneInventory.ps1
.\scripts\Get-TestLaneInventory.ps1 -Format Json -OutFile test-inventory.json
```

The inventory reports discovered xUnit test methods by lane, category trait, project, and fast-lane exclusion reason. It is a visibility tool, not a pass/fail gate; `test-lane.ps1` remains authoritative for execution.

## Local Pre-Release Validation

Use `scripts/Test-PreRelease.ps1` (Windows) or `scripts/test-pre-release.sh` (Linux/macOS) before pushing a release branch, tag, or installer build. It is the local-first release gate: GitHub Actions should not be the first place release failures are discovered.

```powershell
# PowerShell — normal local release confidence run
.\scripts\Test-PreRelease.ps1

# Resume after fixing a failed phase
.\scripts\Test-PreRelease.ps1 -Resume

# Show the exact phase list without running it
.\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt

# Quicker confidence run: skips Node, scale, Docker, and installer phases
.\scripts\Test-PreRelease.ps1 -Quick -IncludeSlt

# Include SQL Logic Tests in the release gate
.\scripts\Test-PreRelease.ps1 -IncludeSlt

# Include Docker-backed connector coverage
.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration

# Include Standard-scale certification
.\scripts\Test-PreRelease.ps1 -IncludeStandardScale

# Build release artifacts after validation
.\scripts\Test-PreRelease.ps1 -BuildInstallers -Platforms win-x64
```

```bash
# Bash — same phases, same flag semantics
./scripts/test-pre-release.sh
./scripts/test-pre-release.sh --resume
./scripts/test-pre-release.sh --explain --include-slt
./scripts/test-pre-release.sh --quick --include-slt
./scripts/test-pre-release.sh --include-slt
./scripts/test-pre-release.sh --include-docker-integration
./scripts/test-pre-release.sh --include-standard-scale
./scripts/test-pre-release.sh --build-installers --platforms linux-x64
./scripts/test-pre-release.sh --build-installers --platforms osx-arm64
```

The script writes timestamped JSON/Markdown reports and phase logs under `release-validation/`, which is ignored by Git. The `latest/state.json` file lets `--resume` skip phases that already passed for the same source fingerprint. If code changes after a failed run, rerun from the beginning unless you intentionally use `--force-resume`.

`Test-PreRelease.ps1 -Explain -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64` currently produces this full local release plan:

| # | Phase | Enabled by |
| ---: | :--- | :--- |
| 1 | Asset drift check | Always |
| 2 | Dotnet restore | Always |
| 3 | NuGet dependency audit | Always |
| 4 | Dotnet build | Always |
| 5 | Smoke lane | Always |
| 6 | Fast lane | Always |
| 7 | Sample scripts | Always |
| 8 | SLT lane | `-IncludeSlt` |
| 9 | VS Code npm ci | Default; skipped by `-SkipNode` or `-Quick` |
| 10 | VS Code npm audit | Default; skipped by `-SkipNode` or `-Quick` |
| 11 | VS Code compile | Default; skipped by `-SkipNode` or `-Quick` |
| 12 | VS Code unit tests | Default; skipped by `-SkipNode` or `-Quick` |
| 13 | Scale certification smoke | Default; skipped by `-SkipScale` or `-Quick` |
| 14 | Cert baseline regression check (smoke) | Default; skipped by `-SkipScale` or `-Quick` |
| 15 | Docker integration lane | `-IncludeDockerIntegration`; disabled by `-Quick` |
| 16 | Scale certification standard | `-IncludeStandardScale`; disabled by `-Quick` |
| 17 | Cert baseline regression check (standard) | `-IncludeStandardScale`; disabled by `-Quick` |
| 18 | Release publish artifacts | `-BuildInstallers`; disabled by `-Quick` |
| 19 | Windows MSI | `-BuildInstallers -Platforms win-x64`; disabled by `-Quick` |

`fast` is the default local correctness lane. `full` runs the normal xUnit test projects and skips the benchmark executable and deployment-only SLT corpus so `dotnet test` output stays meaningful.

For release claim tracking, see [Release_Capability_Matrix.md](Strategy/Release_Capability_Matrix.md). Keep release notes aligned with the strongest automated evidence in that matrix.

## ETL Scenario Golden Tests

Scenario tests live under `tests/etl_scenarios/<scenario-name>/` and are executed by `EtlScenarioGoldenTests` in `tests\ETL-SQL.Tests`. Each scenario has:

- `script.etlsql`: the script under test.
- `expected.json`: expected runtime query output and/or static lineage expectations.

Use scenario tests for cross-feature release claims that are easy to miss with isolated unit tests: lineage plus inherited tags, `WHAT_IF` plus destructive DML, loops that produce final output, staged ETL flows, and error-handling workflows. Use SLT for SQL compatibility claims; use scenarios for ETL-SQL orchestration claims.

### Category tag reference

| Category | Requires Docker? | Included in fast/coverage run? | When to use |
| :--- | :---: | :---: | :--- |
| *(no tag)* | No | Yes | Default — most unit and functional tests |
| `Smoke.*` | No | Yes | Hand-picked fast confidence checks |
| `Portal` | No | Yes | Report Portal `WebApplicationFactory` tests backed by SQLite |
| `Integration` | **Yes** | No | Tests that need a real external service (Docker SFTP, real DB, cloud) |
| `Performance` | No | No | Timing-sensitive assertions with scale data |
| `SLT` | No | No | SQL Logic Test corpus — run explicitly only |

> **Portal vs Integration:** `WebApplicationFactory` tests run the portal in-process with a temp SQLite database — no Docker. Tag these `Portal` so they run in normal CI. Only use `Integration` when a test genuinely needs an external container or cloud endpoint.

## SFTP Integration Tests

Docker-based SFTP tests live in `tests/ETL-SQL.Tests/Integration/Connectors/` and are tagged `Category=Integration`. They require Docker Desktop to be running.

```powershell
# Run Docker-dependent connector integration tests
dotnet test ETL-SQL.slnx --filter "Category=Integration"

# Run only the SFTP lane
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter "FullyQualifiedName~SftpIntegration"
```

The `SftpFixture` starts an `atmoz/sftp` container once per collection. Tests cover password auth, private-key auth, upload/download round-trips, list, delete, overwrite semantics, large-file checksum, `ReadBatches`, credential masking, and host allowlist enforcement. Container startup typically takes 3–8 seconds.

## Browser-side UI (sandbox, manual)

The portal and report-runtime JavaScript components — `renderDag` (structure/lineage DAG), the report designer (`createDesigner`), the script editor (`createScriptEditor`), and the extracted lineage/dependencies render module (`src/ETL-SQL.ReportPortal/wwwroot/js/lineage-ui.js`) — have **no automated browser test lane yet**. Verify them **manually** in the no-Docker UI sandbox:

```powershell
pwsh -File tools\ui-sandbox\serve.ps1
```

It serves a Storybook-style harness that imports the canonical/source files directly and drives each component with fixture data (and a mock `fetch` for API-backed surfaces) — no Docker, no portal build, no catalog DB. Pick a story + fixture from the sidebar; edits show on **↻ Reload**. See [`tools/ui-sandbox/README.md`](../tools/ui-sandbox/README.md) for the story list and how to add one.

The **data contracts** these components consume *are* covered by automated tests — e.g. the structure/lineage DTOs and the cross-script dataset bridge are asserted in `PortalIntegrationTests` (Portal lane, no Docker). The sandbox covers the **rendering** those tests don't. Automated browser coverage (Playwright) is tracked in `TODO.md`.

## Code Coverage

The CI minimum is **70% line coverage** across all non-integration test runs.

```powershell
# Collect coverage (Portal tests are included — they run without Docker)
dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT" `
    --collect:"XPlat Code Coverage" --results-directory ./coverage

# Generate an HTML + text summary report
dotnet reportgenerator `
    -reports:"./coverage/**/coverage.cobertura.xml" `
    -targetdir:"./coverage/report" `
    -reporttypes:"Html;TextSummary"

# Open the report
start ./coverage/report/index.html
```

The text summary is written to `./coverage/report/Summary.txt`. Key assemblies and their coverage targets:

| Assembly | Notes |
| :--- | :--- |
| `ETL-SQL.Core` | Parser, AST, security — keep above 80% |
| `ETL-SQL.Engine` | Evaluator, handlers — keep above 70% |
| `ETL-SQL.Analysis` | Linter rules — keep above 85% |
| `ETL-SQL.Connectors` | Many DataSource classes — lowest due to provider coupling |
| `ETL-SQL-Portal` | Covered by `Category=Portal` WebApplicationFactory tests |

`Category=Integration` tests (Docker-dependent) are **excluded** from the coverage run. Do not count Docker connector tests toward the 70% gate.

## SQL Logic Tests (SLT)

The SLT suite validates SQL correctness against the [SQLite Logic Test](https://www.sqlite.org/sqllogictest/doc/trunk/about.wiki) corpus. It is **not part of the default developer lane** because it takes a long time to complete — running the full corpus can exceed 15 minutes.

### Corpus location

| Path | Contents |
| :--- | :--- |
| `tests/slt_data/corpus/` | Upstream SQLite Logic Test files (select1–5.test, etc.) — thousands of queries |
| `tests/slt_data/` | Custom ETL-SQL test files for specific feature areas (aggregates, type coercion, etc.) |

### Running SLT tests

```powershell
# Run only SLT tests (explicit, slow — expect 15+ minutes for the full corpus)
.\scripts\test-lane.ps1 -Lane slt

# Direct project invocation also requires the deployment opt-in switch.
$env:ETL_SQL_RUN_SLT = '1'
dotnet test tests\ETL-SQL.SqlLogicTests\ETL-SQL.SqlLogicTests.csproj --filter "Category=SLT"
$env:ETL_SQL_RUN_SLT = $null
```

All SLT test cases in `ETL-SQL.SqlLogicTests` are tagged `[Trait("Category", "SLT")]` and are skipped unless `ETL_SQL_RUN_SLT=1` is set. This keeps them out of normal local, agent, PR, and full-suite runs even if someone invokes the SLT test project directly by mistake.

### When to run

Run the SLT corpus manually when:
- Adding or changing SQL expression evaluation, type coercion, or aggregate behavior.
- Validating join correctness after engine changes.
- Preparing a release and need a full SQL correctness sweep.

SLT corpus tests are not expected in CI's fast or PR lanes. Scheduled nightly or release CI may include them explicitly.
