# etl-sql admin user create

Create a Portal user

## Synopsis

```text
etl-sql admin user create [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--email` | Email address for the new user. |
| `--if-not-exists` | Succeed without changes when the record already exists, so a re-run is a no-op. |
| `--json` | Output results and messages in structured JSON format. |
| `--password-stdin` | Read the password from standard input. Passwords are never accepted as arguments. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--provider` | Identity provider for the new user (Local or LDAP). |
| `--role` | Role to assign to the new user. |
| `--username` | Target user name. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
