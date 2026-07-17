# etl-sql run

Execute an ETL-SQL script

## Synopsis

```text
etl-sql run <script> [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `script` | yes | The ETL-SQL script to execute. |

## Options

| Option | Description |
| :--- | :--- |
| `--batch-size, -b` | The size of data chunks to process in memory. |
| `--json` | Output results and messages in structured JSON format. |
| `--log, -l` | Enable logging. Optional: specify path/directory. |
| `--page, -pa` | Pause and page between multiple result sets in the console. |
| `--perf, -p` | Display performance metrics after execution. |
| `--preview, -pr` | Preview top N results (e.g. 20, 100, *) |
| `--progress, -g` | Display real-time graphical execution progress. |
| `--resume` | Resume execution of a persistent session from the last successfully completed checkpoint. |
| `--session` | Enable session persistence with the specified session ID. |
| `--silent, -s` | Remove all console messages. |
| `--var, -d` | Inject a variable into the script (e.g. @Name=Value). |
| `--verbose, -v` | Print detailed execution tracking. |

## Examples

```bash
# Simplest run
ETL-SQL run nightly_load.etlsql

# With perf metrics and logging
ETL-SQL run nightly_load.etlsql --perf --log C:\Logs\etlsql\

# Inject runtime parameters
ETL-SQL run monthly_report.etlsql --var @env=PROD --var @month=2026-03

# Headless with JSON output for automation
ETL-SQL run nightly_load.etlsql --json --silent

# Persistent session — connections survive between runs
ETL-SQL run setup_connections.etlsql --session prod-session
ETL-SQL run nightly_load.etlsql --session prod-session

# Live progress tree in the terminal
ETL-SQL run heavy_transform.etlsql --progress --perf
```

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
