# etl-sql admin gateway resource

Administer the protected Gateway-local resource registry

## Synopsis

```text
etl-sql admin gateway resource <subcommand> [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--connector` | Registered connector type. |
| `--credential-ref` | Local credential reference in ENV:name form. |
| `--executing-credential-id` | Expected PostgreSQL session_user for audit; enables verified viewer context. |
| `--operations` | Comma-separated READ, WRITE, EXECUTE operation classes. |
| `--resource-id` | Stable local resource ID. |
| `--target` | Local connector target; use ${CREDENTIAL} for the resolved credential. |
| `--viewer-claims` | Allowlist: viewer_groups, viewer_roles, viewer_scopes, is_admin. |
| `--viewer-context-ttl-seconds` | Signed viewer context lifetime from 1 to 300 seconds (default 60). |

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`approve`](admin-gateway-resource-approve.md) | approve a local Gateway resource |
| [`disable`](admin-gateway-resource-disable.md) | disable a local Gateway resource |
| [`list`](admin-gateway-resource-list.md) | List local Gateway resources without revealing targets or credentials |
| [`propose`](admin-gateway-resource-propose.md) | Propose a local connector resource |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
