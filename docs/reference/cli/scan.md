# etl-sql scan

Inspect local or cataloged database schemas for stewardship gaps

## Synopsis

```text
etl-sql scan [source] [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `source` | no | Local file/directory or SHARED: connection alias to inspect (default: current directory). |

## Options

| Option | Description |
| :--- | :--- |
| `--json` | Output results and messages in structured JSON format. |
| `--pii` | Suggest protected-data tags from schema names and etlsql-policy.json. |
| `--table` | Database table whose schema should be inspected when source is SHARED:alias. |

## Examples

```bash
# Inspect one local file without reading or printing row values
ETL-SQL scan ./data/customers.parquet --pii

# Inspect supported schema files under a directory and emit the versioned JSON contract
ETL-SQL scan ./data --pii --json

# Inspect one table through a credential-safe shared connection-catalog alias
ETL-SQL scan SHARED:warehouse --pii --table sales.customers --json
```

The scanner reads schema names only. It supports CSV/TSV/text, JSON, XML, Parquet, Excel, and Avro files, recurses at most five directory levels, and stops at 100 files. Database scans require a configured `SHARED:` alias and an explicit `--table`; raw connection strings and credentials are not accepted. Suggestions and transparent component scores use the nearest `etlsql-policy.json`.

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
