# etl-sql gateway resource

Administer the protected Gateway-local resource registry

## Synopsis

```text
etl-sql gateway resource <subcommand> [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--connector` | Registered connector type. |
| `--credential-ref` | Local credential reference in ENV:name form. |
| `--operations` | Comma-separated READ, WRITE, EXECUTE operation classes. |
| `--resource-id` | Stable local resource ID. |
| `--target` | Local connector target; use ${CREDENTIAL} for the resolved credential. |

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`approve`](gateway-resource-approve.md) | approve a local Gateway resource |
| [`disable`](gateway-resource-disable.md) | disable a local Gateway resource |
| [`list`](gateway-resource-list.md) | List local Gateway resources without revealing targets or credentials |
| [`propose`](gateway-resource-propose.md) | Propose a local connector resource |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
