# etl-sql ui edit

Start the modern windowed Terminal IDE (default)

## Synopsis

```text
etl-sql ui edit [file] [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `file` | no | Optional file to pre-load |

## Options

| Option | Description |
| :--- | :--- |
| `--batch-size, -b` | The size of data chunks to process in memory. |
| `--session` | Enable session persistence with the specified session ID. |
| `--verbose, -v` | Print detailed execution tracking. |

## Examples

```bash
# Open the IDE with a file pre-loaded
ETL-SQL ui edit nightly_load.etlsql

# Open the IDE with a persistent session
ETL-SQL ui edit --session dev-workspace
```

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
