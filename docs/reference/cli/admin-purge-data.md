# etl-sql admin purge-data

Delete all installed ETL-SQL runtime data (reports, snapshots, databases, logs, sessions, secrets)

## Synopsis

```text
etl-sql admin purge-data [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--confirm` | Confirm permanent deletion of all installed runtime data (required to delete). |
| `--dry-run` | List the installed runtime data that would be removed without deleting anything. |
| `--install-dir` | Installation directory whose configured runtime data should be purged (default: current install directory). |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
