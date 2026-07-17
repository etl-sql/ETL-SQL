# etl-sql ui repl

Start the JSON-based REPL protocol for IDE integration

## Synopsis

```text
etl-sql ui repl [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--batch-size, -b` | The size of data chunks to process in memory. |
| `--json` | Output results and messages in structured JSON format. |
| `--log, -l` | Enable logging. Optional: specify path/directory. |
| `--perf, -p` | Display performance metrics after execution. |
| `--session` | Enable session persistence with the specified session ID. |
| `--var, -d` | Inject a variable into the script (e.g. @Name=Value). |
| `--verbose, -v` | Print detailed execution tracking. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
