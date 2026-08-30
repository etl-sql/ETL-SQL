# etl-sql studio

Launch or control local ETL-SQL Studio project hosts

## Synopsis

```text
etl-sql studio <subcommand> [project] [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `project` | no | Optional script file or project directory to open in Studio |

## Options

| Option | Description |
| :--- | :--- |
| `--idle-timeout-minutes` | Stop the host after this many idle minutes; zero disables idle shutdown |
| `--new-instance` | Start an independent host for the same project (advanced) |
| `--new-window` | Open another browser window against the healthy host for this project |
| `--no-browser` | Do not automatically open the browser on start |
| `--port, -p` | Port to listen on (default: auto-assigned ephemeral port) |

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`list`](studio-list.md) | List healthy local Studio instances |
| [`open`](studio-open.md) | Reconnect to or start a Studio project host |
| [`stop`](studio-stop.md) | Gracefully stop Studio instances for a project |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
