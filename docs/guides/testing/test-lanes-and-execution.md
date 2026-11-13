# Test Lanes and Suite Execution

To provide fast, deterministic feedback during engine and tool development, the ETL-SQL test suite is organized into distinct **execution lanes** categorized by speed and subsystem boundary.

---

> **Applies to:** contributors and maintainers developing the ETL-SQL engine, language server, or Portal.

## Fast Local Validation (`Test-PrePush.ps1`)

The pre-push hook runs the fast tier on every push: an environment check, source preflight and
formatting, about three minutes. CI runs everything else.

```powershell
.\scripts\Test-PrePush.ps1 -Fast                     # what the hook runs
.\scripts\Test-PrePush.ps1                           # the complete local set, before a risky batch
.\scripts\Test-Environment.ps1 -For Integration      # Docker and the ODBC driver, in seconds
.\scripts\Test-Environment.ps1 -For Timing           # refuses a busy machine
```

The `integration` lane of `scripts\test-lane.ps1` runs the Integration check first, and the
`perf` and `benchmarks` lanes run the Timing check, so a lane stops in seconds on a machine that
cannot run it. CI skips the check; pass `-SkipEnvironmentCheck` to skip it locally.

Set `ETLSQL_PREPUSH_FULL=1` to make the hook run the complete set. The rest of this section
describes the complete set.

The script runs source preflight, then uses the shared CI impact selection to choose local checks.
Classified documentation-only edits stop after source checks. Extension-only edits run the engine
integration build and both extension and UI package checks.

For browser-test-only edits, it builds and formats the browser test project, then runs its complete
browser inventory once. That run includes the Studio performance budget test and retains its local
platform result beside the inventory, TRX and coverage receipt under a unique
`release-validation/pre-push-browser/` directory. Missing performance coverage or evidence fails
validation. Chromium installs on first use unless `ETLSQL_PLAYWRIGHT_SKIP_INSTALL=1` is already set
for an installed browser. Runtime depends on the selected tests and browser setup.

UI source selections add the engine integration build, both extension/UI package checks and the
complete browser inventory described above. They retain the existing solution-format, generated-contract,
asset, type/lint, consumer-contract and smoke checks. The complete publication gate is still required
before removing existing coverage. Any failing stage stops the run; Studio performance evidence and
the caller's performance-output environment setting are checked/restored as in browser-only validation.

Shared inputs, unknown paths and other mixed selections retain broader formatting, asset, browser
type/lint, consumer-contract and engine smoke checks. Local pre-push results do not replace full
candidate certification before release. See [CI impact selection](../../../scripts/README.md#ci-impact-selection).

---

## Test Lanes Overview (`test-lane.ps1`)

Execute targeted test lanes using the PowerShell lane runner:

```powershell
.\scripts\test-lane.ps1 -Lane <lane-name> [-NoRestore] [-NoBuild]
```

| Lane | Focus Area | Execution Time |
| :--- | :--- | ---: |
| **`smoke`** | Critical path end-to-end smoke verification across core, security, reporting, and Portal. | ~5-10s |
| **`fast`** | Fast bounded suite combining core smoke and language server tests. | ~15-20s |
| **`engine`** | Comprehensive engine tests: parser, AST, evaluator, handlers, functions, and connectors. | ~60s |
| **`portal`** | Web Portal controllers, Razor views, permissions, authentication, and API tests. | ~45s |
| **`perf`** | Throughput benchmarks, large dataset batches, and memory allocation profiling. | ~30s |
| **`integration`** | Database container fixtures and external live service tests. | ~2m |
| **`full`** | Complete combined test suite across all projects. | ~3-4m |

---

## Focused Smoke Testing (`test-smoke.ps1`)

Run sub-lane smoke tests for instant feedback while editing specific subsystems:

```powershell
# Run all smoke tests
.\scripts\test-smoke.ps1 -Lane all

# Run subsystem-specific smoke tests
.\scripts\test-smoke.ps1 -Lane core
.\scripts\test-smoke.ps1 -Lane security
.\scripts\test-smoke.ps1 -Lane reporting
.\scripts\test-smoke.ps1 -Lane portal
```

---

## Related Topics

- [Golden Scenarios & SQL Logic Tests](golden-scenarios-and-slt.md) — Cross-feature scenario testing.
- [Enterprise Certification Testing](enterprise-certification-testing.md) — Multi-platform hardening tests.
- [Testing Index](README.md) — Testing documentation index.
