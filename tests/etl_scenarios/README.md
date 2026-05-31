# ETL Scenario Golden Tests

Use this folder for release-significant ETL-SQL workflows that are broader than a single unit test. The xUnit harness in `tests/ETL-SQL.Tests/Scenarios/EtlScenarioGoldenTests.cs` discovers each immediate child folder and runs its `script.etlsql` against `expected.json`.

Each scenario folder contains:

- `script.etlsql`: the ETL-SQL script under test.
- `expected.json`: runtime query expectations and/or static lineage expectations.

Supported `expected.json` sections:

```json
{
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
  ]
}
```

Prefer these tests for cross-feature claims such as:

- staged ingest-transform-publish flows;
- lineage and tag propagation;
- `WHAT_IF` behavior around destructive DML;
- loops that produce final tables;
- `TRY...CATCH` error recovery behavior.

Use SQL Logic Tests for SQL compatibility claims. Use this scenario harness for ETL-SQL orchestration claims.
