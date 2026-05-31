# Release Capability Matrix

This matrix maps product claims to release evidence. Use it before tagging a release, and keep the public release notes no stronger than the strongest evidence in this file.

Status meanings:

- **Green**: covered by fast tests plus at least one scenario/sample/SLT-style end-to-end proof.
- **Yellow**: covered by unit or focused integration tests, but missing a representative end-to-end proof.
- **Red**: known gap or no automated proof.

| Capability | Status | Required evidence before release | Current proof |
| :--- | :---: | :--- | :--- |
| Core parser and evaluator correctness | Green | Fast lane passes; SLT lane passes for SQL compatibility claims. | `.\scripts\test-lane.ps1 -Lane fast`; optional `-Lane slt` |
| Zero-trust file and credential guardrails | Green | Smoke/fast security tests pass; samples do not require unsafe paths or secrets. | `.\scripts\test-smoke.ps1 -Lane security`; fast lane |
| WHAT_IF dry-run behavior | Green | DML is suppressed in focused tests and scenario tests. | `StmtWhatIfTests`; `tests/etl_scenarios/what_if_suppresses_destructive_dml` |
| ETL control flow loops | Green | At least one scenario proves loop output, plus focused statement tests. | `tests/etl_scenarios/loop_control_flow_materializes_expected_rows` |
| Static lineage with inherited tags | Green | Scenario proves source metadata survives a SELECT INTO transformation. | `tests/etl_scenarios/lineage_tags_survive_select_into` |
| SQL logic compatibility | Yellow | SLT corpus passes on the release branch. | `.\scripts\test-lane.ps1 -Lane slt` |
| Connector integration boundaries | Yellow | Docker-backed integration lane passes on release candidate hardware. | `.\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration` |
| Report Portal behavior | Green | Portal tests are part of the fast lane and dedicated portal lane. | `.\scripts\test-lane.ps1 -Lane fast`; `-Lane portal` |
| Published samples | Green | Sample runner passes in pre-release validation. | `.\scripts\Test-AllSamples.ps1`; pre-release script |
| Scale certification | Yellow | Smoke certification passes locally; standard tier passes before public release claims about scale. | `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke`; optional Standard |
| Installers and packaged artifacts | Yellow | Installer build requested and validated for each platform being released. | `.\scripts\Test-PreRelease.ps1 -BuildInstallers` |

## Release Gate

Minimum local release gate:

```powershell
.\scripts\Test-PreRelease.ps1 -IncludeSlt
```

Use the heavier gate before announcing connector, Docker, scale, or installer claims:

```powershell
.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers
```

Use `-Explain` first when you only want to see what will run:

```powershell
.\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt -IncludeDockerIntegration
```

## Adding Evidence

For each release-significant feature, add one of these before marking it Green:

- A focused unit/integration test that proves the edge case.
- A scenario under `tests/etl_scenarios/<name>/` with `script.etlsql` and `expected.json`.
- A sample that is executed by `Test-AllSamples.ps1`.
- An SLT corpus entry when the claim is SQL compatibility rather than ETL orchestration.

Prefer scenario tests for cross-feature claims such as lineage plus tags, `WHAT_IF` plus DML, loops plus output shape, or staged extract-transform-load behavior.
