# etl-sql admin machine tool grant

Grant use of a machine-local tool

## Synopsis

```text
etl-sql admin machine tool grant [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--name, -n` | Name of the tool. |
| `--principal` | Principal key. The stable identifier, not a username — a username can be reassigned. |
| `--principal-kind` | Principal kind: USER, GROUP, or SERVICE. |
| `--tenant` | Tenant ID from the configured Orchestrator identity authority. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
