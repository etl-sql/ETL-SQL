# etl-sql admin service-account update

Update a Portal service account

## Synopsis

```text
etl-sql admin service-account update [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--capability` | Studio capability to grant. Repeatable; on update, replaces the whole grant. |
| `--clear-capabilities` | Remove every Studio capability. Mutually exclusive with --capability. |
| `--clear-expiry` | Remove the account expiry. Mutually exclusive with --expires-at. |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--disable` | Disable the account without revoking it. Mutually exclusive with --enable. |
| `--enable` | Enable the account. Mutually exclusive with --disable. |
| `--expires-at` | UTC expiry as an ISO-8601 timestamp. |
| `--if-version` | Fail unless the record is still at this version. Guards against a concurrent edit. |
| `--json` | Output results and messages in structured JSON format. |
| `--name` | Service-account name. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--scope` | Scope to grant. Repeatable; on update, replaces the whole scope set. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
