# etl-sql admin promotion saas-delete

Delete one Managed Dedicated tenant boundary under signed retention/legal authorization

## Synopsis

```text
etl-sql admin promotion saas-delete [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--execute` | Perform deletion after signed authorization, retention, and legal-hold checks pass. |
| `--receipt-root` | External durable directory for the deletion completion record. |
| `--tenant` | Tenant assertion; must match the active signed operation authorization. |
| `--tenant-root` | Provisioned Managed Dedicated tenant boundary to delete. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
