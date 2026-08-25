# Contributor Testing Guide

> **Applies to:** contributors to the ETL-SQL repository. For validating your own data pipelines, see [Pipeline Unit Testing & Mocking](../pipelines/pipeline-unit-testing.md) and [Validating Data Quality](../data-quality/column-quality-rules.md).

ETL-SQL employs a layered, deterministic testing methodology to ensure engine correctness, dialect portability, enterprise zero-trust security boundaries, and high-performance cross-source orchestration.

## Testing Architecture & Confidence Layers

| Layer | Purpose | Primary Scope | Commands |
| :--- | :--- | :--- | :--- |
| **Unit & Functional Tests** | Validates lexer, parser, AST nodes, evaluator, statement handlers, built-in standard library functions, and portal APIs. | Core, Engine, Analysis, Portal | `dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT&Category!=Fuzz"` |
| **Smoke & Fast Lanes** | Rapid pre-commit feedback covering core parser, evaluator, and language server capabilities in under 30 seconds. | Core, LanguageServer | `pwsh scripts/test-lane.ps1 -Lane fast -NoRestore` |
| **Golden ETL Scenarios** | End-to-end integration scenarios verifying multi-step extraction, staging, transformations, transactions, and error recovery. | `tests/etl_scenarios/` | `dotnet test --filter "FullyQualifiedName~EtlScenarioGoldenTests"` |
| **SQL Logic Tests (SLT)** | Formal verification against the SQLLogicTest corpus for ANSI SQL compliance, joins, aggregations, and subqueries. | Core SQL Evaluator | `pwsh scripts/test-lane.ps1 -Lane slt` |
| **Enterprise Certification** | Zero-trust validation of organization policies, security boundary enforcement, sandboxing, and dual-platform (Windows / Linux) compliance. | Security & Host Isolation | `pwsh scripts/Test-EnterpriseHardeningCertification.ps1` |

## Contributor Guides

For in-depth procedures on running and authoring tests across specific subsystems:

- [Test Lanes & Execution](../testing/test-lanes-and-execution.md) — Comprehensive guide to PowerShell lane runners (`test-lane.ps1`, `test-smoke.ps1`), test categorization, and pre-push gates.
- [Golden Scenarios & SQL Logic Tests](../testing/golden-scenarios-and-slt.md) — How to author new multi-step golden scenarios and run the SLT compliance harness.
- [Enterprise Certification Testing](../testing/enterprise-certification-testing.md) — Hardened security test execution, policy engine verification, and evidence generation.

## Related References

- [Architecture: Test Strategy](../../architecture/roadmaps/test-strategy.md)
- [Enterprise Release Gates](../../architecture/decisions/enterprise-release-gates.md)
- [Deployment Profile Certification](../../administration/platform/deployment-profile-certification.md)
