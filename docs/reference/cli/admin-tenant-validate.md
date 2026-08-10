# etl-sql admin tenant validate

Verify a bundle's integrity and, with --operator-key, its authenticity

## Synopsis

```text
etl-sql admin tenant validate [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--bundle` | Path to the tenant portability bundle directory. |
| `--operator-key` | Published operator public key used to verify the bundle signature. |
| `--require-signature` | Fail unless the bundle carries a signature that verifies against --operator-key. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
