# Test Lanes and Suite Execution

To provide fast, deterministic feedback during engine and tool development, the ETL-SQL test suite is organized into distinct **execution lanes** categorized by speed and subsystem boundary.

---

> **Applies to:** contributors and maintainers developing the ETL-SQL engine, language server, or Portal.

## Fast Local Validation (`Test-PrePush.ps1`)

Before pushing code or submitting a PR, always execute the local fast pre-push validation script:

```powershell
.\scripts\Test-PrePush.ps1
```

This runs an automated 8-stage gate in ~30 seconds:
1. Code formatting check
2. Shared report browser asset synchronization
3. Syntax index synchronization
4. Syntax index link and reference page coverage audit
5. Flaky test pattern check
6. Shell script LF line ending check
7. Test lane inventory & milestone structure audit
8. Fast contract, architecture, and smoke test suite

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
