# etl-sql gen-script

Compile a schema JSON specification into a validated ETL-SQL script template

## Synopsis

```text
etl-sql gen-script [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--output, -o` | Destination path for the generated ETL-SQL script. |
| `--schema, -s` | Path to the input JSON schema specification file. |

## Examples

```bash
ETL-SQL gen-script --schema ./specs/customer_feed.json --output ./scripts/load_customers.etlsql
```

Generated scripts include schema gates, casting, lineage tags, AI review/evidence comments when present, validation issue summaries, and optional quarantine scaffolding. Review the JSON, complete the generated `#staging` extraction block, and test with real vendor files before production use. See [Spec-Driven Development](../../spec-import/spec-driven-development.md) and Cookbook recipe 25 for the full workflow.

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
