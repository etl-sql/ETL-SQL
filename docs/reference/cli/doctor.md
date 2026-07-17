# etl-sql doctor

Perform a system health check to verify the environment

## Synopsis

```text
etl-sql doctor [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--json` | Output results and messages in structured JSON format. |
| `--profile` | Check depth: 'quick' (fast local checks) or 'full' (adds engine, report, asset, runtime, and configured service probes). |
| `--strict` | Exit with code 1 if any check produces a WARN or FAIL result. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
