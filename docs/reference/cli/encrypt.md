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
> The master password must be the same each time you run scripts referencing `ENC:` strings. For headless or production jobs, pass it at runtime with `--pass MyMasterKey` or `ETL_SQL_MASTER_PASSWORD`, or use a `SECRET:` vault reference. Literal `USE PASSWORD = '...';` is for initial authoring only: when `ALLOW_PLAINTEXT_SECRETS` is `OFF` (default), save tools and first-run execution automatically seal secrets and rewrite it to `USE PASSWORD PROMPT;`.

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
