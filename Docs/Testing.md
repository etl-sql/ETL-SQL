# Testing

For the overall lane model and cleanup guidance, see [Test_Strategy.md](Strategy/Test_Strategy.md).

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
.\scripts\test-lane.ps1 -Lane full
.\scripts\test-lane.ps1 -Lane benchmarks
.\scripts\test-lane.ps1 -Lane slt        # deployment-only
```

## Local Pre-Release Validation

Use `scripts/Test-PreRelease.ps1` before pushing a release branch, tag, or installer build. It is the local-first release gate: GitHub Actions should not be the first place release failures are discovered.

```powershell
# Normal local release confidence run
.\scripts\Test-PreRelease.ps1

# Resume after fixing a failed phase
.\scripts\Test-PreRelease.ps1 -Resume

# Include Docker-backed connector coverage
.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration

# Include Standard-scale certification
.\scripts\Test-PreRelease.ps1 -IncludeStandardScale

# Build release artifacts after validation
.\scripts\Test-PreRelease.ps1 -BuildInstallers -Platforms win-x64
```

The script writes timestamped JSON/Markdown reports and phase logs under `release-validation/`, which is ignored by Git. The `latest/state.json` file lets `-Resume` skip phases that already passed for the same source fingerprint. If code changes after a failed run, rerun from the beginning unless you intentionally use `-ForceResume`.

`fast` is the default local correctness lane. `full` runs the normal xUnit test projects and skips the benchmark executable and deployment-only SLT corpus so `dotnet test` output stays meaningful.

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
