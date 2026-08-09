# etl-sql admin machine secret

Manage the machine-local Governance:Secrets provider

## Synopsis

```text
etl-sql admin machine secret <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`delete`](admin-machine-secret-delete.md) | Permanently remove a machine-local secret |
| [`disable`](admin-machine-secret-disable.md) | Disable a machine-local secret |
| [`enable`](admin-machine-secret-enable.md) | Re-enable a disabled machine-local secret |
| [`list`](admin-machine-secret-list.md) | List names and status from the machine-local secret store |
| [`rotate`](admin-machine-secret-rotate.md) | Replace an existing machine-local secret |
| [`set`](admin-machine-secret-set.md) | Encrypt and store a named machine-local secret |
| [`verify`](admin-machine-secret-verify.md) | Resolve a machine-local secret without printing the value |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
