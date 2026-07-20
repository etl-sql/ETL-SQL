# etl-sql edit

Open a script or workspace in the browser script editor

## Synopsis

```text
etl-sql edit [path] [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `path` | no | Script file or workspace folder to open (default: current directory) |

## Options

| Option | Description |
| :--- | :--- |
| `--open` | Open the editor in the default browser on start |
| `--port, -p` | Loopback port to listen on (default: auto-assigned ephemeral port) |
| `--readonly, --read-only` | Open the workspace read-only (saving is rejected) |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
