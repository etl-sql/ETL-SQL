# etl-sql admin orchestrator show

Show the grants on one Orchestrator object

## Synopsis

```text
etl-sql admin orchestrator show [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--json` | Output results and messages in structured JSON format. |
| `--kind` | Object kind: JOB, SCHEDULE, or NOTIFICATION. |
| `--object` | Object name, resolved in your own tenant. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
