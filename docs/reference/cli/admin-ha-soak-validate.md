# etl-sql admin ha-soak validate

Validate completed HA soak evidence before citing it

## Synopsis

```text
etl-sql admin ha-soak validate [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--allow-dirty` | Allow evidence validation while the current worktree has uncommitted changes. |
| `--markdown-report` | Optional path for the HA soak evidence validation Markdown summary. |
| `--required-commit` | Source commit SHA required by topology metadata; defaults to current HEAD. |
| `--required-gate` | Evidence gate to validate: Sustained, LargeJob, FaultInjection, or All. |
| `--run-root, -r` | Path to a generated HA soak topology run root. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
