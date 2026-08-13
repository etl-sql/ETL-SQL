# etl-sql admin promotion

Inspect and prepare deployment-profile promotions

## Synopsis

```text
etl-sql admin promotion <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`export`](admin-promotion-export.md) | Export eligible Orchestrator catalog and governance state |
| [`import`](admin-promotion-import.md) | Import an Orchestrator promotion package idempotently |
| [`preflight`](admin-promotion-preflight.md) | Create a secret-safe, mutation-free promotion inventory |
| [`saas-delete`](admin-promotion-saas-delete.md) | Delete one Managed Dedicated tenant boundary under signed retention/legal authorization |
| [`saas-onboard`](admin-promotion-saas-onboard.md) | Create and populate one physically isolated SaaS tenant boundary |
| [`saas-upgrade`](admin-promotion-saas-upgrade.md) | Drain and upgrade one Managed Dedicated tenant boundary |
| [`validate`](admin-promotion-validate.md) | Validate mappings and collisions without changing the target |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
