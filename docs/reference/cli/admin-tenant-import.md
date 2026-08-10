# etl-sql admin tenant import

Preflight and apply a bundle with workloads disabled

## Synopsis

```text
etl-sql admin tenant import [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--binding` | Target binding as SOURCE=TARGET (repeatable); preflight also accepts a supplied logical id. |
| `--bundle` | Path to the tenant portability bundle directory. |
| `--collision` | Import collision policy: fail (default) or proceed. |
| `--dry-run` | Compute and print the import plan without changing the target. |
| `--operator-key` | Published operator public key used to verify the bundle signature. |
| `--portal-url` | Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable. |
| `--recipient-key` | Recipient public key for export or tenant private key for import. |
| `--require-signature` | Fail unless the bundle carries a signature that verifies against --operator-key. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
