# etl-sql admin service-account create

Create a Portal service account

## Synopsis

```text
etl-sql admin service-account create [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--capability` | Studio capability to grant. Repeatable; on update, replaces the whole grant. |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--description` | Service-account description. |
| `--expires-at` | UTC expiry as an ISO-8601 timestamp. |
| `--if-not-exists` | Succeed without changes when the record already exists, so a re-run is a no-op. |
| `--json` | Output results and messages in structured JSON format. |
| `--name` | Service-account name. |
| `--owner` | Portal username that owns the service account. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--role` | Role to grant. Repeatable and accepted only when creating the account. |
| `--scope` | Scope to grant. Repeatable; on update, replaces the whole scope set. |
| `--secret-out` | New file that receives the one-time secret. The secret is never printed. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
