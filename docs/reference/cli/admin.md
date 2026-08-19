# etl-sql admin

Operator and administration commands

## Synopsis

```text
etl-sql admin <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`access-simulate`](admin-access-simulate.md) | Simulate what a user can reach — the access question, answered without a browser |
| [`backup`](admin-backup.md) | Back up portal/orchestrator state into split-custody data and keys archives |
| [`doctor`](admin-doctor.md) | Perform a system health check to verify the environment |
| [`gateway`](admin-gateway.md) | On-premises Data Gateway administration and setup |
| [`group`](admin-group.md) | Manage Portal groups and their membership |
| [`ha-soak`](admin-ha-soak.md) | Prepare and collect PostgreSQL HA soak certification artifacts |
| [`machine`](admin-machine.md) | Manage machine-local governance stores |
| [`migrate-database`](admin-migrate-database.md) | Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment |
| [`orchestrator`](admin-orchestrator.md) | Manage per-object Orchestrator grants and ownership |
| [`portal-whoami`](admin-portal-whoami.md) | Resolve Portal credentials and print the identity, roles, and scopes (never a secret) |
| [`promotion`](admin-promotion.md) | Inspect and prepare deployment-profile promotions |
| [`restore`](admin-restore.md) | Validate and restore a backup (data + keys archives) |
| [`service-account`](admin-service-account.md) | Manage Portal service accounts |
| [`session`](admin-session.md) | Inspect and disconnect Portal sign-in sessions |
| [`support-bundle`](admin-support-bundle.md) | Collect a redacted support archive (config, health, logs, database metrics) |
| [`tenant`](admin-tenant.md) | Export, inspect, and import tenant portability bundles |
| [`user`](admin-user.md) | Manage Portal users |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
