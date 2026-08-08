# etl-sql admin

Operator and administration commands

## Synopsis

```text
etl-sql admin <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`backup`](admin-backup.md) | Back up portal/orchestrator state into split-custody data and keys archives |
| [`delete-connection`](admin-delete-connection.md) | Permanently remove a shared connection from the catalog |
| [`delete-secret`](admin-delete-secret.md) | Permanently remove a named secret from the secret store |
| [`disable-connection`](admin-disable-connection.md) | Disable a shared connection so SHARED:alias fails until it is re-enabled |
| [`disable-secret`](admin-disable-secret.md) | Disable a named secret so resolution fails until it is re-enabled |
| [`doctor`](admin-doctor.md) | Perform a system health check to verify the environment |
| [`enable-connection`](admin-enable-connection.md) | Re-enable a disabled shared connection; its stored definition is retained |
| [`enable-secret`](admin-enable-secret.md) | Re-enable a disabled secret; the stored value resolves again |
| [`group`](admin-group.md) | Inspect Portal groups |
| [`ha-soak`](admin-ha-soak.md) | Prepare and collect PostgreSQL HA soak certification artifacts |
| [`list-connections`](admin-list-connections.md) | List shared connection catalog entries and their status |
| [`migrate-database`](admin-migrate-database.md) | Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment |
| [`portal-whoami`](admin-portal-whoami.md) | Resolve Portal credentials and print the identity, roles, and scopes (never a secret) |
| [`promotion`](admin-promotion.md) | Inspect and prepare deployment-profile promotions |
| [`restore`](admin-restore.md) | Validate and restore a backup (data + keys archives) |
| [`rotate-secret`](admin-rotate-secret.md) | Replace the value of an existing named secret |
| [`session`](admin-session.md) | Inspect Portal sign-in sessions |
| [`set-connection`](admin-set-connection.md) | Store a shared connection in the catalog for scripts to use as SHARED:alias |
| [`set-secret`](admin-set-secret.md) | Encrypt and store a named secret in the configured secret store (machine scope) |
| [`support-bundle`](admin-support-bundle.md) | Collect a redacted support archive (config, health, logs, database metrics) |
| [`user`](admin-user.md) | Inspect Portal users |
| [`verify-connection`](admin-verify-connection.md) | Prove a shared connection's definition and secret references resolve, without printing values |
| [`verify-secret`](admin-verify-secret.md) | Resolve a named secret to prove it is readable, without printing the value |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
