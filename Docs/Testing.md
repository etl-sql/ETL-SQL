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

`fast` is the default local correctness lane. `full` runs the normal xUnit test projects and skips the benchmark executable and deployment-only SLT corpus so `dotnet test` output stays meaningful.

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
