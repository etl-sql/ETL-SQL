# etl-sql encrypt

Utility to encrypt a string for secure connections

## Synopsis

```text
etl-sql encrypt <value> [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `value` | yes | The string to encrypt. |

## Options

| Option | Description |
| :--- | :--- |
| `--pass` | Master password for encryption. |

## Examples

```bash
# Encrypt a connection string
ETL-SQL encrypt "Server=prod-sql;Database=DW;User Id=sa;Password=S3cr3t!" --pass MyMasterKey

# Output:
# Encrypted: ENC:U2FsdGVkX1+...

# Use in a script:
# CREATE CONNECTION prod AS MSSQL('ENC:U2FsdGVkX1+...', TRUSTED_CONNECTION=FALSE);
```

> [!IMPORTANT]
> The master password must be the same each time you run scripts referencing `ENC:` strings. Pass it at runtime with `--pass MyMasterKey` or set `USE PASSWORD = '...';` at the top of your script.

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
