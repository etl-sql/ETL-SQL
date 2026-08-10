# etl-sql admin tenant preflight

Report what a target must supply before a bundle can be imported

## Synopsis

```text
etl-sql admin tenant preflight [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--binding` | Target binding as SOURCE=TARGET (repeatable); preflight also accepts a supplied logical id. |
| `--bundle` | Path to the tenant portability bundle directory. |
| `--operator-key` | Published operator public key used to verify the bundle signature. |
| `--require-signature` | Fail unless the bundle carries a signature that verifies against --operator-key. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
