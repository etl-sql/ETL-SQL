# Contributor Testing Guides

[« Back to Guides](../README.md)

These guides describe how to execute, extend, and audit the test suites that guarantee the stability and security of the ETL-SQL engine.

---

> **Note for Users:** If you want to write automated tests for your own `.etlsql` data pipelines, see [Pipeline Unit Testing & Mocking](../pipelines/pipeline-unit-testing.md).

---

## Guides in this Section

| Guide | Description |
| :--- | :--- |
| [Test Lanes & Execution](test-lanes-and-execution.md) | PowerShell lane runners (`test-lane.ps1`, `test-smoke.ps1`), pre-push validation, and execution times. |
| [Golden Scenarios & SQL Logic Tests](golden-scenarios-and-slt.md) | Multi-step pipeline golden tests and ANSI SQL compliance testing with SLT. |
| [Enterprise Certification Testing](enterprise-certification-testing.md) | Zero-trust security certification and dual-platform (Windows / Linux) compliance. |

---

## Related References

- [Architecture: Test Strategy](../../architecture/roadmaps/test-strategy.md)
- [Enterprise Release Gates](../../architecture/decisions/enterprise-release-gates.md)
