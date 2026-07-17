# etl-sql admin set-connection

Store a shared connection in the catalog for scripts to use as SHARED:alias

## Synopsis

```text
etl-sql admin set-connection [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--alias, -a` | Catalog alias scripts reference as SHARED:alias (letters, numbers, period, underscore, hyphen). |
| `--option` | Connection option as KEY=VALUE (repeatable). Credential fields must reference SECRET:name. |
| `--sensitive` | Field name this entry classifies as sensitive (repeatable): masked in displays and SECRET:-resolvable. |
| `--target` | Optional connection-string target. Credential fields must reference SECRET:name, never raw values. |
| `--type, -t` | Connector type of the shared connection (MSSQL, POSTGRES, S3, ...). |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
