# etl-sql admin tenant export

Compose a signed, optionally tenant-encrypted portability bundle

## Synopsis

```text
etl-sql admin tenant export [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--artifact` | Portable source artifact to include (repeatable). |
| `--artifact-root` | Root used to preserve relative artifact paths. |
| `--bundle` | Path to the tenant portability bundle directory. |
| `--client-id` | Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID. |
| `--orchestrator-alias` | Portal Orchestrator alias recorded by configuration export. |
| `--orchestrator-package` | Optional existing Orchestrator promotion package to include. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--recipient-key` | Recipient public key for export or tenant private key for import. |
| `--signing-key` | Operator private key used to sign an exported bundle. |
| `--source-profile` | Source profile: Solo, Team, Enterprise, or SaaS. |
| `--tenant` | Stable tenant export identity recorded in the bundle manifest. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
