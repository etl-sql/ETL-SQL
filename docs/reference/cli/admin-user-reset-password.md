# etl-sql admin user reset-password

Set a user's password, read from stdin

## Synopsis

```text
etl-sql admin user reset-password [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--if-version` | Fail unless the record is still at this version. Guards against a concurrent edit. |
| `--json` | Output results and messages in structured JSON format. |
| `--password-stdin` | Read the password from standard input. Passwords are never accepted as arguments. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--username` | Target user name. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
