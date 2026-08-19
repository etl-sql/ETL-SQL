# etl-sql gateway setup

Configure and enroll this machine as an on-premises Data Gateway node

## Synopsis

```text
etl-sql gateway setup [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--gateway-id` | Logical gateway or cluster ID. |
| `--install-service` | Register as a background system service (Windows Service / systemd). |
| `--node-id` | Node machine identifier (default: host machine name). |
| `--non-interactive, -y` | Run in non-interactive mode without prompting. |
| `--portal` | Portal URL (e.g. https://portal.company.com). |
| `--token` | One-time enrollment token issued by Portal. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
