# etl-sql admin restore

Validate and restore a backup (data + keys archives)

## Synopsis

```text
etl-sql admin restore [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--from` | Path to the data backup archive (etl-sql-backup-*.zip). |
| `--keys` | Path to the matching keys archive (etl-sql-keys-*.zip). |
| `--report` | Write a machine-readable JSON recovery report to this path. |
| `--to` | Target directory to restore into (required unless --validate). |
| `--validate` | Verify catalog and key versions and archive integrity without writing any files. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
