# etl-sql admin group set-capabilities

Replace a group's Studio capabilities with the given set

## Synopsis

```text
etl-sql admin group set-capabilities [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--capability` | Studio capability to grant. Repeatable. Replaces the group's whole grant. |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--json` | Output results and messages in structured JSON format. |
| `--name` | Target group name. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
