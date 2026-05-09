# Testing

For the overall lane model and cleanup roadmap, see [Test_Strategy.md](/Docs/Test_Strategy.md).

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
```

`fast` is the default local correctness lane. `full` runs the real xUnit test projects and skips the benchmark executable so `dotnet test` output stays meaningful.
