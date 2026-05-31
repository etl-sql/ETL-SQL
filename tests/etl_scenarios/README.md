# ETL Scenario Golden Tests

Use this folder for release-significant ETL-SQL workflows that are broader than a single unit test. The xUnit harness in `tests/ETL-SQL.Tests/Scenarios/EtlScenarioGoldenTests.cs` discovers each immediate child folder and runs its `script.etlsql` against `expected.json`.

Each scenario folder contains:

- `script.etlsql`: the ETL-SQL script under test.
- `expected.json`: runtime query expectations and/or static lineage expectations.

Supported `expected.json` sections:

```json
{
  "setupFiles": [
    {
      "path": "input.csv",
      "content": "id,name\n1,Ada\n"
    }
  ],
  "seedLineage": [
    {
      "table": "#Source",
      "column": "Email",
      "metadata": { "pii": "true" }
    }
  ],
  "staticLineage": [
    {
      "targetTable": "#Target",
      "targetColumn": "Email",
      "operation": "SELECT",
      "sourceTables": [ "#Source" ],
      "sourceColumns": [ "Email" ],
      "metadata": { "pii": "true" }
    }
  ],
  "runtimeQueries": [
    {
      "sql": "SELECT COUNT(*) AS C FROM #Target;",
      "rows": [
        { "C": 1 }
      ]
    }
  ],
  "failure": {
    "messageContains": "expected error text"
  }
}
```

Use `{ScenarioTempDir}` in `script.etlsql` or `runtimeQueries[].sql` when a scenario needs temporary files. The harness creates a fresh temp directory for each scenario, writes `setupFiles` into it, replaces the token with a forward-slash path, and removes the directory after the test.

Use `failure` only for scenarios where the script is expected to abort. Runtime and lineage expectations are skipped for failure scenarios.

Prefer these tests for cross-feature claims such as:

- staged ingest-transform-publish flows;
- DML audit capture with `OUTPUT ... INTO`;
- file connector read/write round trips;
- modular orchestration with `RUN SCRIPT`;
- staged `MERGE` upsert workflows;
- query-row `FOR @row IN (...)` iteration;
- lineage, source-table, and tag propagation through multi-step publish flows;
- `WHAT_IF` behavior around destructive DML and staged `MERGE`;
- loops that produce final tables, including `BREAK` / `CONTINUE` behavior;
- `TRY...CATCH` error recovery behavior.
- transaction rollback from failed quality gates inside `TRY...CATCH`;
- fatal error behavior outside recovery blocks.

Use SQL Logic Tests for SQL compatibility claims. Use this scenario harness for ETL-SQL orchestration claims.
