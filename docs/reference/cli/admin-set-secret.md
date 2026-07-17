# etl-sql admin set-secret

Encrypt and store a named secret in the configured secret store (machine scope)

## Synopsis

```text
etl-sql admin set-secret [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--name, -n` | Name of the secret (letters, numbers, period, underscore, hyphen). |
| `--value` | Secret value. Omit to enter it at a masked prompt or pipe it via stdin; --value can persist in shell history. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
