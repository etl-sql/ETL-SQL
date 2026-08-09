# etl-sql admin machine connection set

Store a machine-local SHARED: connection

## Synopsis

```text
etl-sql admin machine connection set [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--alias, -a` | Catalog alias scripts reference as SHARED:alias (letters, numbers, period, underscore, hyphen). |
| `--option` | Connection option as KEY=VALUE (repeatable). Credential fields must reference SECRET:name. |
| `--sensitive` | Field name this entry classifies as sensitive (repeatable): masked in displays and SECRET:-resolvable. |
| `--target` | Optional connection-string target. Credential fields must reference SECRET:name, never raw values. |
| `--type, -t` | Connector type of the shared connection (MSSQL, POSTGRES, S3, ...). |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
