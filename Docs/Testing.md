# Testing

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
