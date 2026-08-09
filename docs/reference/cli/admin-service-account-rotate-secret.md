# etl-sql admin service-account rotate-secret

Rotate a service account secret

## Synopsis

```text
etl-sql admin service-account rotate-secret [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--if-version` | Fail unless the record is still at this version. Guards against a concurrent edit. |
| `--json` | Output results and messages in structured JSON format. |
| `--name` | Service-account name. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--secret-out` | New file that receives the one-time secret. The secret is never printed. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
