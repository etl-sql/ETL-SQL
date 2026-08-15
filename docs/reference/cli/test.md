# etl-sql test

Run native ETL-SQL test suites (*.test.etlsql) and table assertions

## Synopsis

```text
etl-sql test [target] [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `target` | no | Test file, directory, or pattern to execute (e.g. tests/, *.test.etlsql). |

## Options

| Option | Description |
| :--- | :--- |
| `--json` | Output results and messages in structured JSON format. |
| `--perf, -p` | Display performance metrics after execution. |
| `--verbose, -v` | Print detailed execution tracking. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
