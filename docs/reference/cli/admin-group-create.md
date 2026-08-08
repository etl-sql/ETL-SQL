# etl-sql admin group create

Create a Portal group

## Synopsis

```text
etl-sql admin group create [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--description` | Description for the new group. |
| `--if-not-exists` | Succeed without changes when the record already exists, so a re-run is a no-op. |
| `--json` | Output results and messages in structured JSON format. |
| `--name` | Target group name. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
