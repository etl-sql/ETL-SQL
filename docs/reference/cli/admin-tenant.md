# etl-sql admin tenant

Export, inspect, and import tenant portability bundles

## Synopsis

```text
etl-sql admin tenant <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`export`](admin-tenant-export.md) | Compose a signed, optionally tenant-encrypted portability bundle |
| [`import`](admin-tenant-import.md) | Preflight and apply a bundle with workloads disabled |
| [`preflight`](admin-tenant-preflight.md) | Report what a target must supply before a bundle can be imported |
| [`validate`](admin-tenant-validate.md) | Verify a bundle's integrity and, with --operator-key, its authenticity |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
