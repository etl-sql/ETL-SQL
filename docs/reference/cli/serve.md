# etl-sql serve

Start a live preview server for a Report-SQL script

## Synopsis

```text
etl-sql serve [script] [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `script` | no | The .rptsql file to serve (omit if using --manifest) |

## Options

| Option | Description |
| :--- | :--- |
| `--manifest, -m` | Serve multiple reports defined in a JSON manifest |
| `--no-browser` | Do not automatically open the browser on start |
| `--port, -p` | Port to listen on (default: auto-assigned ephemeral port) |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
