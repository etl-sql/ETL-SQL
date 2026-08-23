# Golden Scenarios and SQL Logic Tests (SLT)

In addition to isolated unit tests, ETL-SQL relies on two integration-level test suites: **ETL Scenario Golden Tests** (for multi-step pipeline verification) and **SQL Logic Tests (SLT)** (for standard ANSI SQL compliance).

---

> **Applies to:** contributors and maintainers writing new syntax, engine functions, or connector capabilities.

## 1. ETL Scenario Golden Tests

Golden tests execute real `.etlsql` scripts against simulated sources and compare output tables and session variables against strict `expected.json` files.

### Running Scenario Tests

```powershell
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter "FullyQualifiedName~EtlScenarioGoldenTests" --no-restore
```

### Golden Test Directory Layout

Scenario tests live under `tests/etl_scenarios/<scenario-name>/`:

```
tests/etl_scenarios/01_staged_transform/
  ├── script.etlsql      -- Runnable ETL-SQL script
  ├── input_data.csv     -- Input test fixtures
  └── expected.json      -- Expected output schema, rows, and variables
```

### Adding a New Golden Scenario

1. Create a new directory under `tests/etl_scenarios/` with a descriptive kebab-case name.
2. Add `script.etlsql` containing the pipeline logic. Use `MOCKDB()` or bundled CSV/JSON files.
3. Define the expected results in `expected.json`.
4. Run `dotnet test` to verify that the golden runner discovers and executes the new scenario.

---

## 2. SQL Logic Tests (SLT)

ETL-SQL adopts the SQLite SQL Logic Test (SLT) format for verifying SQL grammar and query engine correctness (joins, aggregates, window functions, and type coercions).

### Running the Custom SLT Suite

```powershell
.\scripts\Test-SltCorpus.ps1
```

Custom test files are stored in `tests/slt_data/` and cover:
- Cross-dialect function mappings
- Complex windowing and partitioning
- Set operations (`UNION`, `EXCEPT`, `INTERSECT`)
- Recursive Common Table Expressions (CTE)

---

## Related Topics

- [Test Lanes and Suite Execution](test-lanes-and-execution.md) — Running targeted test lanes.
- [Enterprise Certification Testing](enterprise-certification-testing.md) — Hardening verification.
- [Pipeline Unit Testing](../pipelines/pipeline-unit-testing.md) — Testing user pipelines with `MOCKDB`.
