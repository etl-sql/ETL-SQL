# etl-sql admin orchestrator grant

Grant a principal a permission on an object

## Synopsis

```text
etl-sql admin orchestrator grant [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--json` | Output results and messages in structured JSON format. |
| `--kind` | Object kind: JOB, SCHEDULE, or NOTIFICATION. |
| `--object` | Object name, resolved in your own tenant. |
| `--permission` | READ, EXECUTE, OVERRIDE, or MANAGE. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--principal` | Principal key. The stable identifier, not a username — a username can be reassigned. |
| `--principal-kind` | Principal kind: USER, GROUP, or SERVICE. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
